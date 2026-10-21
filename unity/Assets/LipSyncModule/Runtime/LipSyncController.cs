using System;
using System.Collections;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>AI TTS-only mouth channel. Never writes mesh, bones, expression, or playback.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class LipSyncController : MonoBehaviour
{
    public TTSPlayer tts;
    public FaceExpressionController face;
    public string planUrl=BackendEndpoints.LocalLipSync;
    [Range(.0001f,.03f)] public float silenceFloor=.004f;
    [Range(.01f,.2f)] public float relativeSilence=.06f;
    [Range(.25f,.95f)] public float maximumIntensity=.8f;
    [Header("Streaming PCM input")] public bool enableStreamingLipSync=true;
    float streamingPeak;
    public bool IsActive {get;private set;}
    public string CurrentPhoneme {get;private set;}="";
    public string CurrentViseme {get;private set;}="closed";
    public float CurrentIntensity {get;private set;}
    public int CurrentSample {get;private set;}
    public double LastUpdateMicroseconds {get;private set;}
    public double LastPrecomputeMilliseconds {get;private set;}
    public int TimelineFrameCount=>timeline?.intensity.Length??0;
    public bool HasPlan=>plan!=null;
    public ILipSyncTimelineProvider TimelineProvider {get;set;}
    TurnLifecycleManager lifecycle;TurnContext context;SpeechPlaybackEnvelope playback;
    LipSyncPlan plan;LipSyncTimeline timeline;AudioClip clip;
    Coroutine planRoutine;UnityWebRequest activeRequest;int generation;
    static readonly ProfilerMarker Marker=new ProfilerMarker("Companion.LipSync.SamplePlayback");
    void Awake(){if(face==null)face=GetComponent<FaceExpressionController>();TimelineProvider=new ApproximateMoraTimelineProvider();}
    void OnEnable()
    {
        // Managed provider is not serialized across Editor domain reloads.
        if(TimelineProvider==null)TimelineProvider=new ApproximateMoraTimelineProvider();
        lifecycle=TurnLifecycleManager.Instance;
        if(tts!=null){tts.OnSpeechPreparing+=Prepare;tts.OnSpeechAudioReady+=AudioReady;tts.OnSpeechPlaybackStarted+=Started;tts.OnSpeechPlaybackFinished+=Finished;tts.OnSpeechInterrupted+=Finished;tts.OnSpeechFailed+=Failed;}
        lifecycle.OnTurnCancelled+=Cancelled;
    }
    void OnDisable()
    {
        if(tts!=null){tts.OnSpeechPreparing-=Prepare;tts.OnSpeechAudioReady-=AudioReady;tts.OnSpeechPlaybackStarted-=Started;tts.OnSpeechPlaybackFinished-=Finished;tts.OnSpeechInterrupted-=Finished;tts.OnSpeechFailed-=Failed;}
        if(lifecycle!=null)lifecycle.OnTurnCancelled-=Cancelled;StopLip();
    }
    void Prepare(SpeechPlaybackEnvelope envelope)
    {
        StopLip();playback=envelope;context=lifecycle.ForTrace(tts.ActiveLatencyTrace);
        planRoutine=StartCoroutine(RequestPlan(envelope,generation));
    }
    IEnumerator RequestPlan(SpeechPlaybackEnvelope envelope,int token)
    {
        using(var request=new UnityWebRequest(BackendEndpoints.Resolve(planUrl,"/lipsync/plan"),"POST"))
        {
            request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new PlanInput{text=envelope.Text,language=envelope.Language})));
            request.downloadHandler=new DownloadHandlerBuffer();request.SetRequestHeader("Content-Type","application/json");request.timeout=20;
            activeRequest=request;if(context!=null)lifecycle.Track(context,request,"lipsync_plan");
            yield return request.SendWebRequest();
            if(context!=null)lifecycle.Untrack(context,request);if(activeRequest==request)activeRequest=null;
            if(token!=generation||playback?.PlaybackId!=envelope.PlaybackId||context!=null&&!lifecycle.Guard(context,"lipsync_plan_received"))yield break;
            if(request.result==UnityWebRequest.Result.Success){try{plan=JsonUtility.FromJson<LipSyncPlan>(request.downloadHandler.text);}catch(ArgumentException){plan=null;}}
            if(plan?.units==null||plan.units.Length==0){plan=null;Debug.LogWarning("[LipSync] Plan unavailable; TTS remains independent.");yield break;}
            Build();
        }
        planRoutine=null;
    }
    [Serializable] sealed class PlanInput{public string text,language;}
    void AudioReady(SpeechPlaybackEnvelope envelope,AudioClip audio){if(playback?.PlaybackId!=envelope.PlaybackId)return;clip=audio;Build();}
    void Build()
    {
        if(tts!=null&&tts.ActiveStreamingPcm!=null)return;
        if(clip==null||plan==null)return;long start=System.Diagnostics.Stopwatch.GetTimestamp();
        if(TimelineProvider==null)TimelineProvider=new ApproximateMoraTimelineProvider();
        timeline=TimelineProvider.Build(clip,plan,silenceFloor,relativeSilence,maximumIntensity);
        LastPrecomputeMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
    }
    void Started(SpeechPlaybackEnvelope envelope){if(playback?.PlaybackId==envelope.PlaybackId)IsActive=true;}
    void Finished(SpeechPlaybackEnvelope envelope){if(playback?.PlaybackId==envelope.PlaybackId)StopLip();}
    void Failed(SpeechPlaybackEnvelope envelope,string reason)=>Finished(envelope);
    void Cancelled(TurnContext turn){if(turn==context)StopLip();}
    void StopLip()
    {
        generation++;IsActive=false;
        if(planRoutine!=null){StopCoroutine(planRoutine);planRoutine=null;}
        if(activeRequest!=null){try{activeRequest.Abort();if(context!=null)lifecycle.Untrack(context,activeRequest);activeRequest.Dispose();}catch{}activeRequest=null;}
        streamingPeak=0;timeline=null;plan=null;clip=null;playback=null;context=null;CurrentPhoneme="";CurrentViseme="closed";CurrentIntensity=0;CurrentSample=0;
        if(face!=null&&face.IsReady)face.CloseMouth();
    }
    void StreamingMouth(AudioSource source)
    {
        var pcm=tts.ActiveStreamingPcm;
        float level=pcm.Envelope(CurrentSample);streamingPeak=Mathf.Max(streamingPeak,level);
        if(!enableStreamingLipSync||!source.isPlaying||plan?.units==null||level<silenceFloor)
        {face.CloseMouth();CurrentIntensity=0;CurrentViseme="closed";return;}
        float estimate=0;foreach(var u in plan.units)estimate+=u.kind=="vowel"?.14f:u.phoneme=="pau"?.12f:.025f;
        float seconds=pcm.IsEnded?pcm.Received/32000f:Mathf.Max(.3f,estimate);
        int index=Mathf.Clamp((int)(CurrentSample/32000f/Mathf.Max(.1f,seconds)*plan.units.Length),0,plan.units.Length-1);
        var unit=plan.units[index];string shape=unit.viseme;
        if(shape=="anticipate"||shape=="neutral")
        {shape="closed";for(int j=index+1;j<plan.units.Length&&j<=index+3;j++)if(plan.units[j].kind=="vowel"){shape=plan.units[j].viseme;break;}}
        CurrentPhoneme=unit.phoneme;CurrentViseme=shape;
        CurrentIntensity=shape=="closed"?0:maximumIntensity*Mathf.Sqrt(Mathf.Clamp01((level-silenceFloor)/Mathf.Max(silenceFloor,streamingPeak*.6f-silenceFloor)));
        if(CurrentIntensity==0)face.CloseMouth();else face.SetMouth(shape,CurrentIntensity*100);
    }
    void Update()
    {
        if(!IsActive)return;long start=System.Diagnostics.Stopwatch.GetTimestamp();
        using(Marker.Auto())
        {
            if(context!=null&&!lifecycle.IsThisTurnStillCurrentAndActive(context.TurnId,context.TraceId)){StopLip();return;}
            var source=tts?.audioSource;
            if(face==null||!face.IsReady||source==null||clip==null)return;
            CurrentSample=source.timeSamples;
            if(tts.ActiveStreamingPcm!=null)
            {StreamingMouth(source);return;}
            if(!source.isPlaying||timeline==null){face.CloseMouth();CurrentIntensity=0;CurrentViseme="closed";return;}
            int frame=Mathf.Clamp(CurrentSample/timeline.samplesPerFrame,0,timeline.intensity.Length-1),index=timeline.unitIndex[frame];
            CurrentIntensity=timeline.intensity[frame];
            if(index<0||timeline.shapes[index]=="closed"){CurrentPhoneme=index<0?"sil":timeline.units[index].phoneme;CurrentViseme="closed";face.CloseMouth();}
            else{CurrentPhoneme=timeline.units[index].phoneme;CurrentViseme=timeline.shapes[index];face.SetMouth(CurrentViseme,CurrentIntensity*100f);}
        }
        LastUpdateMicroseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000000.0/System.Diagnostics.Stopwatch.Frequency;
    }
}
