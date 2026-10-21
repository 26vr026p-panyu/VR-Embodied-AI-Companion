using UnityEngine;

public class VoiceActivityDetector : MonoBehaviour
{
    [Header("References")]
    public MicrophoneRecorder recorder;
    public ConversationStateManager conversationState;
    public VoiceTranscriptionClient transcriptionClient;
    public TTSPlayer ttsPlayer;


    [Header("Quest PCVR candidate — remains off until physical acceptance")]
    public bool enableQuestPcvrProfile = false;
    public QuestPcvrVoiceProfile questPcvrProfile = new QuestPcvrVoiceProfile();
    public bool UsingQuestPcvrProfile { get; private set; }
    public float EffectiveNormalThreshold => UsingQuestPcvrProfile ? questPcvrProfile.normalThreshold : voiceThreshold;
    public float EffectiveNormalDuration => UsingQuestPcvrProfile ? questPcvrProfile.normalRequiredSeconds : requiredVoiceDuration;
    public float EffectiveBargeThreshold => UsingQuestPcvrProfile && questPcvrProfile.overrideBargeDetection ? questPcvrProfile.bargeThreshold : bargeInThreshold;
    public float EffectiveBargeDuration => UsingQuestPcvrProfile && questPcvrProfile.overrideBargeDetection ? questPcvrProfile.bargeRequiredSeconds : bargeInRequiredDuration;
    string profileCaptureId; bool profileOptIn;
    void ResolveCaptureProfile()
    {
        if(!recorder || !recorder.CaptureActive)return;
        if(profileCaptureId==recorder.CaptureId && profileOptIn==enableQuestPcvrProfile)return;
        profileCaptureId=recorder.CaptureId;profileOptIn=enableQuestPcvrProfile;
        var desktop=FindAnyObjectByType<WindsongIntegration.DesktopXRDebugController>();
        bool windows=Application.platform==RuntimePlatform.WindowsEditor || Application.platform==RuntimePlatform.WindowsPlayer;
        bool tracked=UnityEngine.XR.XRSettings.isDeviceActive && UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).isValid;
        UsingQuestPcvrProfile=questPcvrProfile!=null && QuestPcvrVoiceProfile.Applies(enableQuestPcvrProfile,windows,tracked,desktop&&desktop.IsDesktopActive,recorder.microphoneName);
        LatencyTrace.CaptureMark(recorder.CaptureId,"voice_detection_profile_selected",new Newtonsoft.Json.Linq.JObject{["profile"]=UsingQuestPcvrProfile?"quest_pcvr_candidate":"existing_baseline",["normal_threshold"]=EffectiveNormalThreshold,["normal_required_ms"]=EffectiveNormalDuration*1000,["barge_threshold"]=EffectiveBargeThreshold,["barge_required_ms"]=EffectiveBargeDuration*1000});
    }

    [Header("Microphone")]
    public int sampleRate = 16000;
    public int monitorBufferSeconds = 1;


    [Header("Normal Speech Start")]
    [Tooltip("普通状态下检测用户开口的阈值")]
    public float voiceThreshold = 0.025f;

    [Tooltip("声音持续多久才算真正开口")]
    public float requiredVoiceDuration = 0.20f;

    [Tooltip("正常进入LISTENING后的稳定时间")]
    public float listeningWarmupDuration = 0.7f;


    [Header("Supplement")]
    [Tooltip("WAITING_FOR_MORE时的麦克风稳定时间")]
    public float supplementWarmupDuration = 0.1f;


    [Header("Barge In")]
    public bool enableBargeIn = true;

    [Tooltip("AI说话时，用户打断所需的音量阈值")]
    public float bargeInThreshold = 0.030f;

    [Tooltip("声音持续多久才确认用户正在打断AI")]
    public float bargeInRequiredDuration = 0.18f;

    [Tooltip("进入AI_SPEAKING后多久开始检测打断")]
    public float bargeInWarmupDuration = 0.15f;


    [Header("Speech End")]
    [Tooltip("低于这个值认为接近安静")]
    public float silenceThreshold = 0.010f;

    [Tooltip("连续安静多久才认为用户真正说完")]
    public float silenceDuration = .7f;
    [Tooltip("Early evidence only; never finalizes audio.")] public float softEndpointSilence = .35f;


    [Header("Debug")]
    public float currentVolume = 0f;

    public bool isMonitoring = false;
    public bool voiceDetected = false;

    public float voiceTimer = 0f;
    public float silenceTimer = 0f;
    public float listeningWarmupTimer = 0f;


    public SpeechEndpointState EndpointState => endpoint.State;
    public bool HasSpeechCandidate => candidate;
    public double SpeechCandidateAt => candidateAt;
    readonly SpeechEndpointDetector endpoint=new SpeechEndpointDetector();
    double traceSilenceBoundary=-1, candidateAt=-1;
    bool candidate;
    long questOnsetSample=-1; float questOnsetQuiet,questEvidenceQuiet;
    void ResetQuestOnset(){questOnsetSample=-1;questOnsetQuiet=questEvidenceQuiet=0;}
    void OnEnable(){if(recorder)recorder.PcmChunk+=OnPcm;}
    void Update(){if(!recorder||!conversationState)return;recorder.StartContinuousCapture();ResolveCaptureProfile();}
    void OnPcm(float[] pcm,int count,long start,int rate)
    {
        if(!conversationState||!recorder)return;
        ResolveCaptureProfile();
        var state=conversationState.currentState;
        currentVolume=recorder.CurrentRms;
        float dt=(float)count/rate;
        if(state==ConversationStateManager.ConversationState.USER_SPEAKING){isMonitoring=false;DetectEnd(dt,rate);return;}
        bool barge=state==ConversationStateManager.ConversationState.AI_SPEAKING||state==ConversationStateManager.ConversationState.PROCESSING;
        bool eligible=state==ConversationStateManager.ConversationState.LISTENING||state==ConversationStateManager.ConversationState.WAITING_FOR_MORE||(barge&&enableBargeIn);
        if(!eligible){ResetMonitoring();return;}
        if(!isMonitoring){isMonitoring=true;listeningWarmupTimer=voiceTimer=0;voiceDetected=false;candidate=false;ResetQuestOnset();LatencyTrace.ListeningStarted();}
        float warm=barge?bargeInWarmupDuration:state==ConversationStateManager.ConversationState.WAITING_FOR_MORE?supplementWarmupDuration:listeningWarmupDuration;
        listeningWarmupTimer+=dt;if(listeningWarmupTimer<warm)return;
        if(UsingQuestPcvrProfile&&!barge)
        {
            if(currentVolume>=questPcvrProfile.onsetEvidenceThreshold)
            {
                if(questOnsetSample<0)questOnsetSample=start;
                questOnsetQuiet=0;
            }
            else if((questOnsetQuiet+=dt)>=questPcvrProfile.onsetEvidenceGapSeconds)ResetQuestOnset();
        }
        float threshold=barge?EffectiveBargeThreshold:EffectiveNormalThreshold;
        if(currentVolume<threshold)
        {
            if(UsingQuestPcvrProfile&&!barge&&candidate)
            {
                questEvidenceQuiet+=dt;
                if(questEvidenceQuiet<=questPcvrProfile.normalEvidenceGapSeconds)return;
            }
            voiceTimer=0;candidate=false;questEvidenceQuiet=0;return;
        }
        questEvidenceQuiet=0;
        if(!candidate){candidate=true;candidateAt=LatencyTrace.Now;LatencyTrace.CaptureMark(recorder.CaptureId,"speech_start_candidate",new Newtonsoft.Json.Linq.JObject{["start_sample_index"]=start,["sample_rate"]=rate});}
        voiceTimer+=dt;
        if(voiceTimer>=(barge?EffectiveBargeDuration:EffectiveNormalDuration))OnSpeechStarted(barge);
    }
    void OnSpeechStarted(bool isBargeIn)
    {
        if(voiceDetected)return;
        var oldState=conversationState.currentState;voiceDetected=true;
        var manager=TurnLifecycleManager.Instance;
        bool supplement=transcriptionClient&&transcriptionClient.TryResumeSupplement(candidateAt);
        if(isBargeIn&&!supplement){LatencyTrace.Mark(manager.Current?.Trace,"user_barge_in_detected");manager.CancelPreviousTurn("barge_in");}
        if(!supplement)recorder.recordingTrace=manager.Begin("microphone").Trace;
        else manager.Transition(manager.ForTrace(recorder.recordingTrace),TurnLifecycleState.Listening);
        if(candidateAt>=0)LatencyTrace.MarkAt(recorder.recordingTrace,"speech_start_candidate",candidateAt);
        LatencyTrace.Mark(recorder.recordingTrace,"speech_start_confirmed");LatencyTrace.Mark(recorder.recordingTrace,"vad_speech_started");
        traceSilenceBoundary=-1;endpoint.BeginSpeech();silenceTimer=0;
        recorder.StartRecording(isBargeIn&&!supplement&&UsingQuestPcvrProfile,UsingQuestPcvrProfile&&!isBargeIn?questOnsetSample:-1);ResetQuestOnset();isMonitoring=false;
    }
    void DetectEnd(float dt,int rate)
    {
        if(!recorder.isRecording){endpoint.Reset();return;}
        var context=TurnLifecycleManager.Instance.ForTrace(recorder.recordingTrace);
        endpoint.Observe(currentVolume,UsingQuestPcvrProfile?questPcvrProfile.endpointSilenceThreshold:silenceThreshold,dt,softEndpointSilence,silenceDuration,context!=null&&!context.IsTerminal);
        silenceTimer=(float)endpoint.SilenceSeconds;
        if(endpoint.SoftCandidate)LatencyTrace.Mark(recorder.recordingTrace,"soft_endpoint_candidate",new Newtonsoft.Json.Linq.JObject{["silence_ms"]=endpoint.SilenceSeconds*1000,["evidence"]=endpoint.Evidence});
        if(endpoint.SoftCancelled)LatencyTrace.Mark(recorder.recordingTrace,"soft_endpoint_cancelled",new Newtonsoft.Json.Linq.JObject{["reason"]="speech_resumed"});
        if(endpoint.Candidate){traceSilenceBoundary=LatencyTrace.Now;LatencyTrace.Mark(recorder.recordingTrace,"speech_end_candidate",new Newtonsoft.Json.Linq.JObject{["end_sample_index"]=recorder.SampleIndex,["sample_rate"]=rate});}
        if(endpoint.State==SpeechEndpointState.SpeechActive)traceSilenceBoundary=-1;
        if(!endpoint.Confirmed&&!recorder.SegmentFull)return;
        if(traceSilenceBoundary>=0)LatencyTrace.MarkAt(recorder.recordingTrace,"speech_end_estimated",traceSilenceBoundary,new Newtonsoft.Json.Linq.JObject{["basis"]="first_quiet_PCM_block_of_confirmed_silence",["sample_window_ms"]=1000.0*256/rate});
        LatencyTrace.Mark(recorder.recordingTrace,"speech_end_confirmed",new Newtonsoft.Json.Linq.JObject{["endpoint_silence_ms"]=endpoint.SilenceSeconds*1000,["reason"]=recorder.SegmentFull?"segment_capacity":"silence"});
        LatencyTrace.Mark(recorder.recordingTrace,"vad_speech_ended");recorder.StopRecording();
        if(recorder.recordedSampleFrames>0&&context!=null&&!context.IsTerminal&&transcriptionClient)transcriptionClient.TranscribeRecording();
        voiceDetected=false;isMonitoring=false;voiceTimer=0;silenceTimer=0;candidate=false;
    }
    void ResetMonitoring(){ResetQuestOnset();isMonitoring=false;voiceDetected=false;candidate=false;voiceTimer=silenceTimer=listeningWarmupTimer=0;endpoint.Reset();}
    void OnDisable(){if(recorder){recorder.PcmChunk-=OnPcm;recorder.StopContinuousCapture();}ResetMonitoring();}
}

