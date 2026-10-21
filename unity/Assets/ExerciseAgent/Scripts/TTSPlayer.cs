using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class TTSPlayer:MonoBehaviour
{
    public event System.Action<SpeechPlaybackEnvelope> OnSpeechPreparing;
    public event System.Action<SpeechPlaybackEnvelope,AudioClip> OnSpeechAudioReady;
    public event System.Action<SpeechPlaybackEnvelope> OnSpeechPlaybackStarted,OnSpeechPlaybackFinished,OnSpeechInterrupted;
    public event System.Action<SpeechPlaybackEnvelope,string> OnSpeechFailed;
    [Header("References")] public AudioSource audioSource;
    [Header("Server")] public string serverUrl=BackendEndpoints.LocalTts;
    [Header("Conversation")] public ConversationStateManager conversationState;
    [Header("Status")] public bool isSpeaking;
    [Header("Streaming TTS")] public bool enableStreamingTts;
    [Range(100,300)] public int initialBufferMs=300;
    [Tooltip("Fixed reserve for Unity streaming clip prefetch; separate from network jitter hold.")] [Range(400,1200)] public int streamingReaderReserveMs=800;
    public string streamingServerUrl="";
    public AudioPcmRingBuffer ActiveStreamingPcm=>activeStream?.Pcm;
    StreamingTtsSession activeStream;
    bool completeFallbackInProgress;
    [Header("Debug")] public string testText="今日は音声テストです。";
    public string testLanguage="ja";
    public LatencyTrace.Turn ActiveLatencyTrace=>context?.Trace;
    public string ActiveSpeechRequestId { get; private set; }
    public SpeechArbiter Arbiter => SpeechArbiter.For(this);
    void Awake(){var owner=Arbiter;}
    TurnLifecycleManager lifecycle;
    TurnContext context;
    SpeechPlaybackEnvelope playback;
    long nextPlaybackId;
    AudioClip currentClip;
    Coroutine speakCoroutine;
    UnityWebRequest activeRequest;
    void OnEnable(){lifecycle=TurnLifecycleManager.Instance;lifecycle.OnTurnCancelled+=Cancelled;}
    void OnDisable()
    {
        if(lifecycle!=null){if(context!=null&&!context.IsTerminal)lifecycle.Cancel(context,"tts_disabled");lifecycle.OnTurnCancelled-=Cancelled;}
        StopLocal(context);
    }
    void OnDestroy(){ClearClip();}
    public void TestSpeak()=>Speak(testText,testLanguage);
    public void Speak(string text,string language)=>Speak(text,language,0,null);
    public void Speak(string text,string language,long turnId)=>Speak(text,language,turnId,null);
    public void Speak(string text,string language,long turnId,LatencyTrace.Turn trace)
    {
        if(!BackendEndpoints.RequireConfigured())return;
        var c=trace!=null?lifecycle.Adopt(trace):lifecycle.ForAgent(turnId)??lifecycle.Begin("tts_only");
        if(!lifecycle.Guard(c,"tts_start"))return;
        if(playback!=null&&context==c)return; // A duplicate callback cannot start a second playback.
        Arbiter.Submit(SpeechRequest.ForTurn(c,text,language));
    }
    internal void PlayOwnedSpeech(SpeechArbiter owner,SpeechRequest request,TurnContext c)
    {
        if(owner!=Arbiter||!lifecycle.Guard(c,"tts_owned_start"))return;
        completeFallbackInProgress=false;
        context=c;ActiveSpeechRequestId=request.requestId;
        string text=request.text,language=request.language;
        playback=new SpeechPlaybackEnvelope(c.AgentTurnId,++nextPlaybackId,text,language);
        if(string.IsNullOrWhiteSpace(text)||audioSource==null){Failure(c,"Empty text or AudioSource missing");return;}
        lifecycle.Transition(c,TurnLifecycleState.ProcessingTTS);
        OnSpeechPreparing?.Invoke(playback);
        if(!SpeechGuard(c,playback,"tts_prepare_callbacks"))return;
        speakCoroutine=StartCoroutine(SpeakCoroutine(text,language,c,playback,request.style));
    }
    internal void StopOwnedSpeech(SpeechArbiter owner,string requestId)
    {if(owner==Arbiter&&ActiveSpeechRequestId==requestId)StopLocal(context);}
    bool SpeechGuard(TurnContext c,SpeechPlaybackEnvelope envelope,string stage)
    {
        if(!lifecycle.Guard(c,stage))return false;
        if(context==c&&playback==envelope)return true;
        LatencyTrace.Mark(c.Trace,"late_result_discarded",new JObject{["stage"]=stage,["reason"]="speech_replaced"});return false;
    }
    IEnumerator SpeakCoroutine(string text,string language,TurnContext c,SpeechPlaybackEnvelope envelope,string style)
    {
        if(enableStreamingTts)
        {
            bool useComplete=false;
            yield return StreamSpeech(text,language,c,envelope,style,value=>useComplete=value);
            if(!useComplete||!SpeechGuard(c,envelope,"stream_complete_fallback"))yield break;
            completeFallbackInProgress=true;
        }
        string url=BackendEndpoints.Resolve(serverUrl,"/tts")+"?text="+UnityWebRequest.EscapeURL(text)+"&language="+UnityWebRequest.EscapeURL(language)+"&style="+UnityWebRequest.EscapeURL(style??"neutral");
        using(var request=UnityWebRequestMultimedia.GetAudioClip(url,AudioType.MPEG))
        {
            activeRequest=request;lifecycle.Track(c,request,"tts_request");
            LatencyTrace.Mark(c.Trace,"tts_request_started",new JObject{["text_length"]=text.Length,["language"]=language});
            yield return request.SendWebRequest();
            lifecycle.Untrack(c,request);if(activeRequest==request)activeRequest=null;
            if(!SpeechGuard(c,envelope,"tts_response"))yield break;
            LatencyTrace.Mark(c.Trace,"tts_response_received",new JObject{["status_code"]=request.responseCode,["response_length"]=request.downloadedBytes});
            if(request.responseCode==409){lifecycle.Cancel(c,"backend_cancelled",false);yield break;}
            if(request.result!=UnityWebRequest.Result.Success){Failure(c,request.error);yield break;}
            var decoded=DownloadHandlerAudioClip.GetContent(request);
            if(!SpeechGuard(c,envelope,"audio_decode_complete")){if(decoded!=null)Destroy(decoded);yield break;}
            if(decoded==null){Failure(c,"AudioClip is null");yield break;}
            currentClip=decoded;
        }
        ReliabilityTrace.SetHealth(c,"TTS",ComponentHealth.Healthy);
        ReliabilityTrace.SetHealth(c,"Backend",ComponentHealth.Healthy);
        if(completeFallbackInProgress){ReliabilityTrace.Event(c,"fallback_succeeded",new JObject{["fallback_target"]="complete_tts"});completeFallbackInProgress=false;}
        LatencyTrace.Mark(c.Trace,"audio_decode_finished",new JObject{["audio_duration"]=currentClip.length,["basis"]="GetContent_returned_decode_may_overlap_download"});
        OnSpeechAudioReady?.Invoke(envelope,currentClip);
        if(!SpeechGuard(c,envelope,"playback_start")){ClearClip();yield break;}
        audioSource.clip=currentClip;isSpeaking=true;
        lifecycle.Transition(c,TurnLifecycleState.Speaking);
        audioSource.Play();LatencyTrace.Mark(c.Trace,"playback_started",new JObject{["basis"]="AudioSource_Play_called_not_acoustic_output"});
        OnSpeechPlaybackStarted?.Invoke(envelope);
        if(!SpeechGuard(c,envelope,"playback_started_callbacks"))yield break;
        yield return new WaitUntil(()=>audioSource==null||!audioSource.isPlaying);
        if(!SpeechGuard(c,envelope,"playback_finish"))yield break;
        if(audioSource==null){ReliabilityTrace.Failure(c,"Playback",FailureCategory.Playback,"source_lost",true);Failure(c,"playback_source_lost");yield break;}
        isSpeaking=false;ClearClip();speakCoroutine=null;
        LatencyTrace.Mark(c.Trace,"playback_finished");
        // Arbiter completes the Turn only after the last scheduled speech. Existing subscribers stay intact.
        OnSpeechPlaybackFinished?.Invoke(envelope);
        if(playback==envelope){playback=null;ActiveSpeechRequestId=null;}
    }
    void StreamMark(TurnContext c,string name,JObject data=null)
    {data=data??new JObject();data["speech_request_id"]=ActiveSpeechRequestId;LatencyTrace.Mark(c.Trace,name,data);}
    IEnumerator StreamSpeech(string text,string language,TurnContext c,SpeechPlaybackEnvelope envelope,string style,System.Action<bool> fallback)
    {
        string url=BackendEndpoints.Websocket(streamingServerUrl,BackendEndpoints.Active.ttsWebsocketPath,serverUrl);
        var r=SpeechRequest.ForTurn(c,text,language);r.requestId=ActiveSpeechRequestId;r.style=style;
        var stream=new StreamingTtsSession(r,url);activeStream=stream;
        StreamMark(c,"tts_request_started",new JObject{["format"]="pcm16"});StreamMark(c,"tts_stream_request_started");
        bool started=false;double firstAt=-1,deadline=Time.realtimeSinceStartupAsDouble+240;int observedUnderruns=0;
        while(true)
        {
            if(!SpeechGuard(c,envelope,"tts_stream_poll")){stream.Abort();if(activeStream==stream)activeStream=null;yield break;}
            while(stream.Notices.TryDequeue(out var notice))
            {
                if((string)notice["type"]=="first_chunk"){firstAt=Time.realtimeSinceStartupAsDouble;StreamMark(c,"tts_first_chunk_received",notice);}
                else if((string)notice["type"]=="end")StreamMark(c,"tts_stream_ended",notice);
            }
            if(stream.Failure!=null||Time.realtimeSinceStartupAsDouble>deadline)
            {
                stream.Abort();if(activeStream==stream)activeStream=null;
                StreamMark(c,"tts_stream_failed",new JObject{["partial_audio_played"]=started,["reason"]=stream.Failure??"timeout"});
                var recovery=ReliabilityTrace.Failure(c,"tts_stream",FailureCategory.TTS,"tts_stream_failed",started);
                if(recovery.Action==ReliabilityAction.Fallback){StreamMark(c,"tts_stream_complete_fallback");ReliabilityTrace.Event(c,"fallback_started",new JObject{["fallback_target"]="complete_tts",["fallback_depth"]=1});fallback(true);}
                else Failure(c,"stream_failed_after_playback");
                yield break;
            }
            // Fixed startup reserve: both sample depth and a short wall-time hold after first chunk.
            if(!started&&firstAt>=0&&Time.realtimeSinceStartupAsDouble-firstAt>=initialBufferMs/1000.0&&
                (stream.Pcm.Buffered>=32000*System.Math.Max(streamingReaderReserveMs,initialBufferMs)/1000||stream.Ended&&stream.Pcm.Buffered>0))
            {
                StreamMark(c,"tts_playable_buffer_ready",new JObject{["buffered_samples"]=stream.Pcm.Buffered});
                stream.Pcm.Arm(); // Unity pre-fills streaming clips during creation; consume real PCM from sample zero.
                currentClip=AudioClip.Create("AI streaming PCM",32000*300,1,32000,true,stream.Pcm.Read);
                StreamMark(c,"tts_clip_prefill_finished",new JObject{["buffered_samples"]=stream.Pcm.Buffered});
                OnSpeechAudioReady?.Invoke(envelope,currentClip);
                if(!SpeechGuard(c,envelope,"stream_playback_callbacks")){stream.Abort();yield break;}
                audioSource.clip=currentClip;isSpeaking=true;started=true;
                lifecycle.Transition(c,TurnLifecycleState.Speaking);audioSource.Play();
                StreamMark(c,"playback_started",new JObject{["basis"]="AudioSource_Play_not_acoustic_output"});StreamMark(c,"tts_stream_playback_started",new JObject{["buffered_samples"]=stream.Pcm.Buffered,["initial_buffer_ms"]=initialBufferMs,["reader_reserve_ms"]=streamingReaderReserveMs});
                OnSpeechPlaybackStarted?.Invoke(envelope);
                if(!SpeechGuard(c,envelope,"stream_started_callbacks")){stream.Abort();yield break;}
            }
            if(started&&audioSource==null){stream.Abort();ReliabilityTrace.Failure(c,"Playback",FailureCategory.Playback,"source_lost",true);Failure(c,"playback_source_lost");yield break;}
            int underruns=stream.Pcm.Underruns;
            if(underruns>observedUnderruns){StreamMark(c,"tts_buffer_underrun",new JObject{["count"]=underruns});observedUnderruns=underruns;}
            if(started&&stream.Ended&&stream.Pcm.Drained&&audioSource.timeSamples>=stream.Pcm.LastDataEnd)
            {
                StreamMark(c,"tts_buffer_drained",new JObject{["underrun_count"]=underruns,["min_buffer_samples"]=stream.Pcm.MinDepth,["max_buffer_samples"]=stream.Pcm.MaxDepth,["total_frames"]=stream.Pcm.Received});
                audioSource.Stop();isSpeaking=false;speakCoroutine=null;ClearClip();
                StreamMark(c,"playback_finished");OnSpeechPlaybackFinished?.Invoke(envelope);
                stream.Abort();if(activeStream==stream)activeStream=null;
                if(playback==envelope){playback=null;ActiveSpeechRequestId=null;}
                yield break;
            }
            if(stream.Ended&&stream.Pcm.Received==0){stream.Abort();activeStream=null;var recovery=ReliabilityTrace.Failure(c,"tts_stream",FailureCategory.TTS,"empty_stream");if(recovery.Action==ReliabilityAction.Fallback){ReliabilityTrace.Event(c,"fallback_started",new JObject{["fallback_target"]="complete_tts",["fallback_depth"]=1});fallback(true);}yield break;}
            yield return null;
        }
    }
    public void Interrupt()
    {
        if(context!=null&&!context.IsTerminal)lifecycle.Cancel(context,"tts_interrupt");
        else StopLocal(context);
    }
    void Cancelled(TurnContext c){if(c==context)StopLocal(c);}
    void StopLocal(TurnContext c)
    {
        if(c!=context)return;
        var stopped=playback;
        bool wasPlaying=isSpeaking;
        if(activeStream!=null){activeStream.Abort();activeStream=null;LatencyTrace.Mark(c.Trace,"tts_stream_cancelled");}
        if(speakCoroutine!=null){StopCoroutine(speakCoroutine);speakCoroutine=null;}
        if(activeRequest!=null)
        {
            var request=activeRequest;activeRequest=null;
            try{request.Abort();lifecycle.Untrack(c,request);request.Dispose();}catch{ }
        }
        if(audioSource!=null&&audioSource.isPlaying)audioSource.Stop();
        isSpeaking=false;ClearClip();
        if(stopped!=null)
        {
            LatencyTrace.Mark(c.Trace,"playback_interrupted",new JObject{["was_playing"]=wasPlaying});
            OnSpeechInterrupted?.Invoke(stopped);
            if(playback==stopped){playback=null;ActiveSpeechRequestId=null;}
        }
        // Conversation state is owned by the new/current turn, never restored by an interrupted turn.
    }
    void Failure(TurnContext c,string reason)
    {
        if(!lifecycle.Guard(c,"tts_failure"))return;
        ReliabilityTrace.Failure(c,"TTS",FailureCategory.TTS,"speech_failed",isSpeaking);
        if(completeFallbackInProgress){ReliabilityTrace.Event(c,"fallback_failed",new JObject{["fallback_target"]="complete_tts"});completeFallbackInProgress=false;}
        if(activeStream!=null){activeStream.Abort();activeStream=null;}
        if(audioSource!=null)audioSource.Stop();
        var failed=playback;isSpeaking=false;speakCoroutine=null;ClearClip();
        LatencyTrace.Mark(c.Trace,"tts_failed");
        if(failed!=null)OnSpeechFailed?.Invoke(failed,reason);
        if(playback==failed){playback=null;ActiveSpeechRequestId=null;}
    }
    void ClearClip(){if(audioSource!=null)audioSource.clip=null;if(currentClip!=null){Destroy(currentClip);currentClip=null;}}
}





