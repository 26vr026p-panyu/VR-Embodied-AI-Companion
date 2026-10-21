using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable] public class TranscriptionResponse
{
    public string text, language;
    public float language_probability;
}
public class VoiceTranscriptionClient : MonoBehaviour
{
    [Header("References")] public MicrophoneRecorder recorder;
    public PythonExerciseAgent agent;
    public ConversationStateManager conversationState;
    [Header("Server")] public string serverUrl=BackendEndpoints.LocalTranscribe;
    [Header("Supplement")] public float supplementWaitSeconds=.5f;
    [Tooltip("Window begins at audio finalization; ASR time consumes it.")] public bool conditionalSupplementWindow=true;
    [Header("Result"),TextArea] public string transcript="";
    public string detectedLanguage="";
    public float languageProbability;
    [Header("Pending Conversation"),TextArea] public string pendingTranscript="";
    public string pendingLanguage="";
    [Header("Status")] public bool isTranscribing, isWaitingForSupplement;
    Coroutine supplementWaitCoroutine;
    TurnContext context;
    TurnLifecycleManager lifecycle;
    readonly ConditionalSupplementWindow supplementWindow=new ConditionalSupplementWindow();
    sealed class AsrWork { public byte[] Wav; public StreamingAsrSession Stream; }
    readonly Queue<AsrWork> pendingAudio=new Queue<AsrWork>();
    [Header("Incremental ASR V1")]
    [Tooltip("Enable after CPU performance review. Complete WAV always remains fallback.")] public bool enableStreamingAsr=false;
    [Range(100,250)] public int streamingChunkMilliseconds=200;
    public string streamingUrl=BackendEndpoints.LocalAsrWs;
    [Tooltip("Explicit language is frozen for each Turn. Auto is experimental.")]
    public AsrLanguageMode languageMode=AsrLanguageMode.Ja;
    [Tooltip("Legacy compatibility field; explicit Language Mode takes precedence.")]
    public string streamingLanguageHint="";
    [Header("Development ASR (Inspector only)")][TextArea] public string partialTranscript="",stablePrefix="";
    readonly List<StreamingAsrSession> streams=new List<StreamingAsrSession>();
    StreamingAsrSession recordingStream;
    void SubscribePcm(){if(recorder){recorder.RecordingPcmStarted+=StreamStarted;recorder.RecordingPcmChunk+=StreamPcm;}}
    void StreamStarted(TurnContext c,float[] pcm,int count,long first,int rate)
    {
        c.BindLanguageMode(languageMode);
        if(!enableStreamingAsr)return;
        try {partialTranscript=stablePrefix="";recordingStream=new StreamingAsrSession(c,BackendEndpoints.Websocket(streamingUrl,BackendEndpoints.Active.asrWebsocketPath,serverUrl),recorder.CaptureId,rate,first,Mathf.Clamp(streamingChunkMilliseconds,100,250),c.LanguageCode);streams.Add(recordingStream);recordingStream.Chunker.Append(pcm,count,first);}
        catch(System.Exception e){LatencyTrace.Mark(c.Trace,"streaming_asr_failed",new JObject{["reason"]=e.GetType().Name,["fallback_complete_wav"]=true});recordingStream=null;}
    }
    void StreamPcm(TurnContext c,float[] pcm,int count,long first,int rate)
    {if(recordingStream==null||recordingStream.Context!=c||recordingStream.Ended)return;try{recordingStream.Chunker.Append(pcm,count,first);}catch(System.Exception){recordingStream.Abort("pcm_clock_gap");}}
    void Update()
    {
        foreach(var stream in streams.ToArray())
        {
            while(stream.Notices.TryDequeue(out var item))
            {
                string type=(string)item["type"];
                if(!lifecycle.Guard(stream.Context,"asr_stream_"+type)){if(type=="partial"||type=="stable")LatencyTrace.Mark(stream.Context.Trace,"late_asr_partial_discarded");continue;}
                if(type=="partial"){partialTranscript=(string)item["text"]??"";LatencyTrace.Mark(stream.Context.Trace,"asr_partial",new JObject{["text_length"]=partialTranscript.Length,["revision"]=item["revision"]});}
                if(type=="stable"){stablePrefix=(string)item["text"]??"";LatencyTrace.Mark(stream.Context.Trace,"asr_stable_prefix",new JObject{["text_length"]=stablePrefix.Length,["revision"]=item["revision"]});}
                if(type=="opened"){LatencyTrace.Mark(stream.Context.Trace,"asr_stream_opened");LatencyTrace.Mark(stream.Context.Trace,"asr_request_started");}
                if(type=="chunks_sent")LatencyTrace.Mark(stream.Context.Trace,"asr_chunk_sent",new JObject{["chunk_count"]=item["chunk_count"]});
            }
            if(stream.Context.IsTerminal&&stream.Final.IsCompleted)streams.Remove(stream);
        }
    }
    VoiceActivityDetector speechDetector;
    const double ConfirmationAllowanceMs=250;
    public bool TryResumeSupplement(double candidateAt)
    {
        if(context==null||context.IsTerminal||context.Trace.AgentTurn!=0||context!=lifecycle.Current)return false;
        bool allowed=conditionalSupplementWindow?supplementWindow.CanResume(candidateAt,LatencyTrace.Now,ConfirmationAllowanceMs):isWaitingForSupplement;
        if(!allowed)return false;
        CancelPendingAgentSend();supplementWindow.Close();
        LatencyTrace.Mark(context.Trace,"supplement_resume_accepted");
        return true;
    }
    void OnEnable(){lifecycle=TurnLifecycleManager.Instance;lifecycle.OnTurnCancelled+=Cancelled;SubscribePcm();}
    void OnDisable(){if(recorder){recorder.RecordingPcmStarted-=StreamStarted;recorder.RecordingPcmChunk-=StreamPcm;}foreach(var stream in streams)stream.Abort("play_exit_or_disabled");streams.Clear();recordingStream=null;if(lifecycle!=null){lifecycle.OnTurnCancelled-=Cancelled;if(context!=null&&!context.IsTerminal)lifecycle.Cancel(context,"transcription_disabled");}CancelPendingAgentSend();}
    void Cancelled(TurnContext c)
    {
        foreach(var stream in streams)if(stream.Context==c){stream.Abort("turn_cancelled");LatencyTrace.Mark(c.Trace,"asr_stream_cancelled");}
        if(recordingStream!=null&&recordingStream.Context==c)recordingStream=null;
        if(context!=c)return;
        CancelPendingAgentSend();supplementWindow.Close();pendingAudio.Clear();isTranscribing=false;pendingTranscript="";pendingLanguage="";transcript="";
    }
    public void TranscribeRecording()
    {
        if(recorder==null||recorder.recordedClip==null||recorder.recordedSampleFrames<=0){Debug.LogWarning("No recorded audio available.");return;}
        var trace=recorder.recordingTrace??LatencyTrace.Begin("recorded_audio");
        var c=lifecycle.Adopt(trace);if(!lifecycle.Guard(c,"asr_request_start"))return;
        if(context!=c){pendingAudio.Clear();pendingTranscript="";pendingLanguage="";}
        c.BindLanguageMode(languageMode);context=c;CancelPendingAgentSend();
        LatencyTrace.Mark(c.Trace,"wav_encode_started");
        byte[] wav=AudioClipWavEncoder.Encode(recorder.recordedClip,recorder.recordedSampleFrames);
        LatencyTrace.Mark(c.Trace,"wav_encode_finished",new JObject{["response_length"]=wav==null?0:wav.Length});
        if(wav==null||wav.Length==0){Failure(c);return;}
        if(pendingAudio.Count>=4){lifecycle.Cancel(c,"supplement_audio_queue_full");return;}
        var stream=recordingStream;recordingStream=null;
        if(stream!=null){stream.Finish();LatencyTrace.Mark(c.Trace,"asr_endpoint_sent");}
        pendingAudio.Enqueue(new AsrWork{Wav=wav,Stream=stream});
        supplementWindow.Begin(LatencyTrace.Now,supplementWaitSeconds);
        LatencyTrace.Mark(c.Trace,"supplement_window_opened",new JObject{["window_ms"]=supplementWaitSeconds*1000,["conditional"]=conditionalSupplementWindow});
        if(isTranscribing)return;
        lifecycle.Transition(c,TurnLifecycleState.ProcessingASR);
        if(conversationState!=null)conversationState.SetState(ConversationStateManager.ConversationState.PROCESSING);
        isTranscribing=true;
        StartCoroutine(SendAudioToWhisper(c,pendingAudio.Dequeue()));
    }
    IEnumerator SendAudioToWhisper(TurnContext c,AsrWork work)
    {
        byte[] wav=work.Wav;
        if(work.Stream!=null)
        {
            float deadline=Time.realtimeSinceStartup+90;
            while(!work.Stream.Final.IsCompleted&&Time.realtimeSinceStartup<deadline){if(!lifecycle.Guard(c,"asr_stream_wait")){work.Stream.Abort("turn_terminal");yield break;}yield return null;}
            if(!work.Stream.Final.IsCompleted)work.Stream.Abort("final_timeout");
            if(!lifecycle.Guard(c,"asr_stream_final")){LatencyTrace.Mark(c.Trace,"late_asr_final_discarded");yield break;}
            string finalJson=work.Stream.Final.IsCompleted?work.Stream.Final.Result:null;
            streams.Remove(work.Stream);
            if(finalJson!=null)
            {
                TranscriptionResponse response=null;
                try { response=JsonUtility.FromJson<TranscriptionResponse>(finalJson); }
                catch(System.Exception) { /* Malformed final must retain complete-WAV fallback. */ }
                if(response!=null&&!string.IsNullOrWhiteSpace(response.text))
                {
                    LatencyTrace.Mark(c.Trace,"asr_stream_closed");AcceptFinal(c,response);yield break;
                }
                LatencyTrace.Mark(c.Trace,"asr_stream_invalid_final",new JObject{["fallback_complete_wav"]=true});
            }
            LatencyTrace.Mark(c.Trace,"streaming_asr_failed",new JObject{["fallback_complete_wav"]=true,["reason"]=work.Stream.FailureReason??"missing_final"});
        }
        if(wav==null||wav.Length==0){Failure(c);yield break;}
        var form=new WWWForm();form.AddField("language_mode",c.LanguageCode);form.AddBinaryData("file",wav,"recording.wav","audio/wav");
        using(var request=UnityWebRequest.Post(BackendEndpoints.Resolve(serverUrl,"/transcribe"),form))
        {
            request.SetRequestHeader("X-Asr-Language-Mode",c.LanguageCode);
            lifecycle.Track(c,request,"asr_request");
            LatencyTrace.Mark(c.Trace,"asr_request_started");
            yield return request.SendWebRequest();
            lifecycle.Untrack(c,request);
            if(!lifecycle.Guard(c,"asr_response"))yield break;
            if(request.responseCode==409){lifecycle.Cancel(c,"backend_cancelled",false);yield break;}
            if(request.result!=UnityWebRequest.Result.Success){Debug.LogWarning("Transcription Error: "+request.error);Failure(c);yield break;}
            TranscriptionResponse response=null;
            try{response=JsonUtility.FromJson<TranscriptionResponse>(request.downloadHandler.text);}
            catch(System.Exception e){Debug.LogWarning("Failed to parse transcription response: "+e.GetType().Name);}
            if(!lifecycle.Guard(c,"asr_parsed"))yield break;
            if(response==null){Failure(c);yield break;}
            AcceptFinal(c,response);
        }
    }
    void AcceptFinal(TurnContext c,TranscriptionResponse response)
    {
        if(!lifecycle.Guard(c,"asr_final_accept"))return;
            LatencyTrace.Mark(c.Trace,"asr_final_received",new JObject{["text_length"]=response.text==null?0:response.text.Length,["language"]=response.language});
            transcript=response.text;detectedLanguage=AsrLanguagePolicy.FinalLanguage(c.LanguageMode,response.language);languageProbability=response.language_probability;
            if(!string.IsNullOrWhiteSpace(transcript))
            {
                if(!string.IsNullOrWhiteSpace(pendingTranscript))LatencyTrace.Mark(c.Trace,"supplement_merged");
                pendingTranscript=string.IsNullOrWhiteSpace(pendingTranscript)?transcript.Trim():pendingTranscript+" "+transcript.Trim();
                if(!string.IsNullOrWhiteSpace(detectedLanguage))pendingLanguage=detectedLanguage;
            }
            if(pendingAudio.Count>0){StartCoroutine(SendAudioToWhisper(c,pendingAudio.Dequeue()));return;}
            isTranscribing=false;
            if(recorder!=null&&recorder.isRecording)return;
            StartSupplementWaiting(c);
    }
    void StartSupplementWaiting(TurnContext c)
    {
        if(!lifecycle.Guard(c,"supplement_start"))return;
        CancelPendingAgentSend();isWaitingForSupplement=true;
        lifecycle.Transition(c,TurnLifecycleState.WaitingSupplement);
        if(conversationState!=null)conversationState.SetState(ConversationStateManager.ConversationState.WAITING_FOR_MORE);
        supplementWaitCoroutine=StartCoroutine(WaitForSupplement(c));
    }
    IEnumerator WaitForSupplement(TurnContext c)
    {
        LatencyTrace.Mark(c.Trace,"supplement_wait_started");
        if(!conditionalSupplementWindow)yield return new WaitForSeconds(supplementWaitSeconds);
        else
        {
            if(speechDetector==null)speechDetector=GetComponent<VoiceActivityDetector>();
            while(supplementWindow.RemainingMilliseconds(LatencyTrace.Now)>0||
                (speechDetector!=null&&speechDetector.HasSpeechCandidate&&supplementWindow.CanResume(speechDetector.SpeechCandidateAt,LatencyTrace.Now,ConfirmationAllowanceMs)))
            {
                if(!lifecycle.Guard(c,"supplement_window"))yield break;
                yield return null;
            }
        }
        if(!lifecycle.Guard(c,"supplement_continuation"))yield break;
        supplementWaitCoroutine=null;isWaitingForSupplement=false;supplementWindow.Close();
        LatencyTrace.Mark(c.Trace,"supplement_wait_finished");
        if(string.IsNullOrWhiteSpace(pendingTranscript))
        {
            if(conversationState!=null)conversationState.SetState(ConversationStateManager.ConversationState.LISTENING);
            lifecycle.Fail(c,"no_transcript");yield break;
        }
        string text=pendingTranscript.Trim(),language=pendingLanguage;
        pendingTranscript="";pendingLanguage="";
        if(!lifecycle.Guard(c,"asr_to_agent"))yield break;
        if(agent!=null)agent.AskAgentWithMessage(text,language,c.Trace);
        else Failure(c);
    }
    public void CancelPendingAgentSend()
    {
        if(supplementWaitCoroutine!=null)
        {
            LatencyTrace.Mark(context?.Trace,"supplement_wait_cancelled");
            StopCoroutine(supplementWaitCoroutine);supplementWaitCoroutine=null;
        }
        isWaitingForSupplement=false;
    }
    void Failure(TurnContext c)
    {
        if(!lifecycle.Guard(c,"asr_failure"))return;
        isTranscribing=false;pendingAudio.Clear();supplementWindow.Close();LatencyTrace.Mark(c.Trace,"asr_failed");
        if(!string.IsNullOrWhiteSpace(pendingTranscript)){StartSupplementWaiting(c);return;}
        if(conversationState!=null)conversationState.SetState(ConversationStateManager.ConversationState.LISTENING);
        ReliabilityTrace.Failure(c,"ASR",FailureCategory.ASR,"asr_failed");
        lifecycle.Fail(c,"asr_failed");
    }
}
