using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>One microphone clock for VAD, pre-roll and finalized ASR segments.</summary>
[DefaultExecutionOrder(-100)]
public class MicrophoneRecorder : MonoBehaviour
{
    [Header("Recording Settings")] public int maxRecordingSeconds=15; public int sampleRate=16000;
    [Header("Continuous Capture")] [Range(1,4)] public int ringBufferSeconds=2;
    [Range(0,1000)] public int preRollMilliseconds=400;
    public TTSPlayer playbackSource;
    [Header("Echo Cancellation Foundation")]
    [Tooltip("EXPERIMENTAL only. Keep false for production until acoustic validation.")] public bool enableExperimentalEchoSuppression=false;
    [Range(0,500)] public float echoDelayEstimateMilliseconds=100;
    [Range(0,2)] public float experimentalEchoGain=.3f;
    public CompanionVoice.MicrophoneEchoPipeline echoPipeline;
    double captureBlockDspStart;
    [Header("Status")] public bool isRecording; public string microphoneName="";
    public int deviceSampleRate, deviceChannels;
    public double lastCaptureUpdateMilliseconds;
    public bool CaptureActive => deviceClip!=null;
    public float CurrentRms { get; private set; }
    public long SampleIndex => ring==null?0:ring.EndSample;
    public long SegmentStartSample { get; private set; }
    public long SegmentEndSample { get; private set; }
    public bool SegmentFull { get; private set; }
    public bool PreRollOverlapWithAiPlayback { get; private set; }
    public int PreRollSamples { get; private set; }
    public int RingCapacitySamples => ring==null?0:ring.Capacity;
    [Header("Recorded Audio")] public AudioClip recordedClip;
    [NonSerialized] public LatencyTrace.Turn recordingTrace;
    public int recordedSampleFrames;
    [Header("Conversation")] public ConversationStateManager conversationState;
    const int BlockFrames=256;
    AudioClip deviceClip; MicrophonePcmRing ring;
    float[] deviceBlock,monoBlock,segment;
    int readCursor,segmentCount; long preRollFloor;
    TurnLifecycleManager lifecycle; TurnContext recordingContext;
    string captureId; bool polling;
    public string CaptureId => captureId;
    // Chunks are borrowed until callback returns; copy only if retaining beyond this call.
    public event Action<float[],int,long,int> PcmChunk;
    // Borrowed selected PCM; consumers must copy synchronously. Same capture clock as PcmChunk.
    public event Action<TurnContext,float[],int,long,int> RecordingPcmStarted;
    public event Action<TurnContext,float[],int,long,int> RecordingPcmChunk;
    void OnEnable(){lifecycle=TurnLifecycleManager.Instance;lifecycle.OnTurnCancelled+=Cancelled;}
    public bool StartContinuousCapture()
    {
        if(CaptureActive)return true;
        if(!BackendEndpoints.RequireConfigured()||!QuestMicrophonePermission.Available())return false;
        if(Microphone.devices.Length==0){Debug.LogWarning("[Capture] No microphone available.");return false;}
        microphoneName=Microphone.devices[0];
        deviceClip=Microphone.Start(microphoneName,true,Mathf.Clamp(ringBufferSeconds,1,4),sampleRate);
        if(!deviceClip){Debug.LogWarning("[Capture] Microphone start failed.");return false;}
        Configure(deviceClip.frequency,deviceClip.channels);
        return true;
    }
    void Configure(int rate,int channels)
    {
        deviceSampleRate=rate;deviceChannels=channels;readCursor=0;preRollFloor=0;
        ring=new MicrophonePcmRing(Mathf.Clamp(ringBufferSeconds,1,4)*rate);
        deviceBlock=new float[BlockFrames*channels];monoBlock=new float[BlockFrames];
        // Bounded utterance storage separate from the history ring, including pre-roll/tail.
        segment=new float[(Mathf.Clamp(maxRecordingSeconds,15,120)+2)*rate];
        segmentCount=0;recordedSampleFrames=0;isRecording=false;SegmentFull=false;CurrentRms=0;
        captureId=Guid.NewGuid().ToString("N");
        if(!playbackSource)playbackSource=FindAnyObjectByType<TTSPlayer>();
        captureBlockDspStart=AudioSettings.dspTime;
        if(!echoPipeline)echoPipeline=GetComponent<CompanionVoice.MicrophoneEchoPipeline>();
        if(!echoPipeline)echoPipeline=gameObject.AddComponent<CompanionVoice.MicrophoneEchoPipeline>();
        echoPipeline.Configure(rate,BlockFrames,playbackSource,captureId,enableExperimentalEchoSuppression,echoDelayEstimateMilliseconds,experimentalEchoGain);
        LatencyTrace.CaptureMark(captureId,"mic_continuous_capture_started",new JObject{["sample_rate"]=rate,["requested_sample_rate"]=sampleRate,["device_channels"]=channels,["channels"]=1,["sample_format"]="float32 PCM",["start_sample_index"]=0});
        LatencyTrace.CaptureMark(captureId,"ring_buffer_ready",new JObject{["capacity_samples"]=ring.Capacity,["pre_roll_ms"]=preRollMilliseconds});
    }
    void Update(){PollCapture();}
    public void PollCapture()
    {
        if(!CaptureActive||polling)return;
        polling=true;
        long begun=System.Diagnostics.Stopwatch.GetTimestamp();
        int position=Microphone.GetPosition(microphoneName);
        if(position<0){polling=false;return;}
        int available=(position-readCursor+deviceClip.samples)%deviceClip.samples;
        // An editor stall longer than the device loop cannot be reconstructed. Never invent samples.
        while(available>=BlockFrames)
        {
            if(!deviceClip.GetData(deviceBlock,readCursor))break;
            for(int frame=0;frame<BlockFrames;frame++){float sum=0;for(int ch=0;ch<deviceChannels;ch++)sum+=deviceBlock[frame*deviceChannels+ch];monoBlock[frame]=sum/deviceChannels;}
            bool ai=playbackSource&&playbackSource.audioSource&&playbackSource.audioSource.isPlaying;
            captureBlockDspStart=AudioSettings.dspTime-(double)available/deviceSampleRate;
            AcceptPcm(monoBlock,BlockFrames,ai);
            readCursor=(readCursor+BlockFrames)%deviceClip.samples;available-=BlockFrames;
        }
        polling=false;
        lastCaptureUpdateMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-begun)*1000.0/System.Diagnostics.Stopwatch.Frequency;
    }
    void AcceptPcm(float[] data,int count,bool ai)
    {
        long start=ring.EndSample;
        // The selected raw/clean PCM goes to BOTH pre-roll history and speech/VAD consumers.
        if(echoPipeline)data=echoPipeline.Process(data,count,start,captureBlockDspStart);
        ring.Append(data,count,ai);
        double square=0;for(int i=0;i<count;i++)square+=data[i]*data[i];CurrentRms=(float)Math.Sqrt(square/count);
        if(isRecording)
        {
            if(recordingContext==null||recordingContext.IsTerminal){DiscardSegment();}
            else {int n=Math.Min(count,segment.Length-segmentCount);Array.Copy(data,0,segment,segmentCount,n);segmentCount+=n;SegmentEndSample=start+n;SegmentFull=segmentCount==segment.Length; if(n>0)RecordingPcmChunk?.Invoke(recordingContext,data,n,start,deviceSampleRate);}
        }
        PcmChunk?.Invoke(data,count,start,deviceSampleRate);
    }
    public void ToggleRecording(){if(isRecording)StopRecording();else StartRecording();}
    public void StartRecording()=>StartRecording(false);
    public void StartRecording(bool preserveCancelledPreRoll)=>StartRecording(preserveCancelledPreRoll,-1);
    public void StartRecording(bool preserveCancelledPreRoll,long speechOnsetSample)
    {
        if(isRecording||(!CaptureActive&&!StartContinuousCapture()))return;
        PollCapture();
        if(recordingTrace==null||recordingTrace.Ended||recordingTrace.AgentTurn>0)recordingTrace=LatencyTrace.Begin("microphone");
        recordingContext=lifecycle.Adopt(recordingTrace);if(!lifecycle.Guard(recordingContext,"recording_start"))return;
        LatencyTrace.Mark(recordingTrace,"pre_roll_copy_started");
        long began=System.Diagnostics.Stopwatch.GetTimestamp();
        int requestedPreRoll=Mathf.RoundToInt(preRollMilliseconds*.001f*deviceSampleRate);
        // Preserve the detected candidate onset plus the unchanged 400ms context.
        // Bound recovery to retained history and two seconds; never cross the normal turn floor.
        if(speechOnsetSample>=0)
        {
            long recovery=Math.Max(0,ring.EndSample-speechOnsetSample)+requestedPreRoll;
            requestedPreRoll=(int)Math.Min(Math.Min(recovery,ring.Capacity),deviceSampleRate*2L);
        }
        long start;bool overlap;
        segmentCount=preserveCancelledPreRoll
            ?ring.CopyForInterruptingSpeech(requestedPreRoll,segment,out start,out overlap)
            :ring.CopyRecent(requestedPreRoll,preRollFloor,segment,out start,out overlap);
        SegmentStartSample=start;SegmentEndSample=ring.EndSample;PreRollSamples=segmentCount;PreRollOverlapWithAiPlayback=overlap;recordedSampleFrames=0;SegmentFull=false;
        LatencyTrace.Mark(recordingTrace,"pre_roll_copy_finished",new JObject{["pre_roll_ms"]=1000.0*segmentCount/deviceSampleRate,["pre_roll_samples"]=segmentCount,["overlap_with_ai_playback"]=overlap,["pre_roll_overlap_with_ai_playback"]=overlap,["start_sample_index"]=start,["end_sample_index"]=SegmentEndSample,["sample_rate"]=deviceSampleRate,["copy_duration_ms"]=(System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000.0/System.Diagnostics.Stopwatch.Frequency});
        isRecording=true;lifecycle.Transition(recordingContext,TurnLifecycleState.Listening);
        LatencyTrace.Mark(recordingTrace,"recording_started");
        RecordingPcmStarted?.Invoke(recordingContext,segment,segmentCount,SegmentStartSample,deviceSampleRate);
        if(conversationState)conversationState.SetState(ConversationStateManager.ConversationState.USER_SPEAKING);
    }
    public void StopRecording()
    {
        if(!isRecording)return;PollCapture();if(!isRecording)return;
        if(!lifecycle.Guard(recordingContext,"capture_finalize")){DiscardSegment();return;}
        isRecording=false;recordedSampleFrames=segmentCount;preRollFloor=SampleIndex;
        if(recordedClip)Destroy(recordedClip);
        recordedClip=segmentCount>0?AudioClip.Create("Speech segment",segmentCount,1,deviceSampleRate,false):null;
        if(recordedClip){var pcm=new float[segmentCount];Array.Copy(segment,pcm,segmentCount);recordedClip.SetData(pcm,0);}
        LatencyTrace.Mark(recordingTrace,"recording_stopped",new JObject{["audio_duration"]=(double)segmentCount/deviceSampleRate});
        LatencyTrace.Mark(recordingTrace,"capture_segment_finalized",new JObject{["start_sample_index"]=SegmentStartSample,["end_sample_index"]=SegmentEndSample,["sample_rate"]=deviceSampleRate,["pre_roll_samples"]=PreRollSamples,["speech_samples"]=segmentCount-PreRollSamples,["overlap_with_ai_playback"]=PreRollOverlapWithAiPlayback,["segment_capacity_reached"]=SegmentFull});
        if(conversationState)conversationState.SetState(ConversationStateManager.ConversationState.WAITING_FOR_MORE);
    }
    void DiscardSegment(){isRecording=false;segmentCount=recordedSampleFrames=0;preRollFloor=SampleIndex;SegmentFull=false;}
    void Cancelled(TurnContext c){if(c!=recordingContext)return;DiscardSegment();if(recordedClip){Destroy(recordedClip);recordedClip=null;}LatencyTrace.Mark(c.Trace,"recording_aborted");}
    public void StopContinuousCapture()
    {
        if(recordingContext!=null&&!recordingContext.IsTerminal&&lifecycle!=null)lifecycle.Cancel(recordingContext,"capture_stopped");
        if(deviceClip){Microphone.End(microphoneName);Destroy(deviceClip);deviceClip=null;}
        DiscardSegment();ring=null;CurrentRms=0;readCursor=0;preRollFloor=0;recordingContext=null;recordingTrace=null;SegmentStartSample=SegmentEndSample=0;
    }
    void OnDisable(){if(lifecycle!=null)lifecycle.OnTurnCancelled-=Cancelled;StopContinuousCapture();}
    void OnDestroy(){StopContinuousCapture();if(recordedClip)Destroy(recordedClip);}
}

