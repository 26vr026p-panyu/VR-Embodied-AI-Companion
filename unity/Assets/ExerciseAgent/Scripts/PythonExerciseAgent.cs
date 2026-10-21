using System.Collections;
using System.Text;
using CompanionAvatar;
using CompanionCapabilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable] public class AgentRequest
{
    public float elapsedTime,stepRate,targetStepRate,rpe,visualGain;
    public string userMessage,language,sessionId;
    public string mode="exercise";
}
[System.Serializable] public class AgentResponse
{
    public string action,speech;
    public AgentBehaviorProposal behavior;
}
public class PythonExerciseAgent:MonoBehaviour
{
    public long CurrentTurnId{get;private set;}
    public event System.Action<AgentReplyEnvelope> OnAgentReplyParsed;
    [Header("References")] public ExerciseStateManager exerciseState;
    public TTSPlayer ttsPlayer;
    public ConversationStateManager conversationState;
    [Header("Server")] public string serverUrl=BackendEndpoints.LocalAgent;
    [Header("Streaming LLM (opt-in until regression)")] public bool enableStreamingLlm;
    [Header("Native tools (opt-in until regression)")] public bool enableNativeToolCalling=false;
    ToolExecutionBridge nativeToolBridge;
    bool completeFallbackInProgress;
    [Header("Agent Output")] public string currentAction="NONE";
    [TextArea] public string currentSpeech="";
    [Header("Conversation")] public string sessionId="user_001";
    public CompanionMode mode=CompanionMode.Exercise;
    [Header("Status")] public bool isRequesting;
    public TurnContext ActiveTurn{get;private set;}
    public AgentStateSnapshot LastStateSnapshot { get; private set; }
    TurnLifecycleManager lifecycle;
    void OnEnable(){lifecycle=TurnLifecycleManager.Instance;lifecycle.OnTurnCancelled+=Cancelled;}
    void OnDisable(){if(lifecycle!=null){lifecycle.OnTurnCancelled-=Cancelled;if(ActiveTurn!=null&&!ActiveTurn.IsTerminal)lifecycle.Cancel(ActiveTurn,"agent_disabled");}isRequesting=false;}
    void Cancelled(TurnContext c){if(ActiveTurn==c){if(enableStreamingLlm)LatencyTrace.Mark(c.Trace,"llm_stream_cancelled");isRequesting=false;currentSpeech="";currentAction="NONE";}}
    public void AskAgent()=>AskAgentWithMessage("","");
    public void AskAgentWithMessage(string text,string language)=>AskAgentWithMessage(text,language,null);
    public void AskAgentWithMessage(string text,string language,LatencyTrace.Turn trace)
    {
        if(!BackendEndpoints.RequireConfigured())return;
        var c=trace==null?lifecycle.Begin("text"):lifecycle.Adopt(trace);
        if(!lifecycle.Guard(c,"llm_request_start"))return;
        if(isRequesting&&ActiveTurn==c){Debug.LogWarning("Agent request is already in progress.");return;}
        ActiveTurn=c;
        if(mode==CompanionMode.Exercise&&exerciseState==null){Failure(c,"exercise_reference_missing");return;}
        StartCoroutine(SendStateToAgent(text,language,c));
    }
    IEnumerator SendStateToAgent(string text,string language,TurnContext c)
    {
        if(!lifecycle.Guard(c,"llm_start"))yield break;
        completeFallbackInProgress=false;
        isRequesting=true;long agentId=++CurrentTurnId;lifecycle.BindAgent(c,agentId);
        lifecycle.Transition(c,TurnLifecycleState.ProcessingLLM);
        if(conversationState!=null)conversationState.SetState(ConversationStateManager.ConversationState.PROCESSING);
        var data=new AgentRequest{elapsedTime=exerciseState==null?0:exerciseState.elapsedTime,stepRate=exerciseState==null?0:exerciseState.stepRate,
            targetStepRate=exerciseState==null?0:exerciseState.targetStepRate,rpe=exerciseState==null?0:exerciseState.rpe,
            visualGain=exerciseState==null?0:exerciseState.visualGain,userMessage=text,language=language,sessionId=sessionId,
            mode=mode==CompanionMode.General?"general":"exercise"};
        string json=mode==CompanionMode.General?new JObject{["mode"]="general",["userMessage"]=text,["language"]=language,["sessionId"]=sessionId}.ToString(Newtonsoft.Json.Formatting.None):JsonUtility.ToJson(data);
        // Capture once; the same immutable value is reused by streaming and complete fallback.
        LastStateSnapshot=AgentStateStore.For(this).CreateSnapshot(c,language);
        var payload=JObject.Parse(json); payload["stateSnapshot"]=LastStateSnapshot.ToJsonObject();
        json=payload.ToString(Newtonsoft.Json.Formatting.None);
        if(enableStreamingLlm&&ttsPlayer!=null)
        {
            bool handled=false;
            yield return SendStream(json,language,c,agentId,value=>handled=value);
            if(handled||!lifecycle.Guard(c,"stream_fallback"))yield break;
            completeFallbackInProgress=true;
            LatencyTrace.Mark(c.Trace,"llm_stream_fallback");
        }
        using(var request=new UnityWebRequest(BackendEndpoints.Resolve(serverUrl,"/agent"),"POST"))
        {
            request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));request.downloadHandler=new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type","application/json");lifecycle.Track(c,request,"llm_request");
            LatencyTrace.Mark(c.Trace,"llm_request_started",new JObject{["text_length"]=text==null?0:text.Length,["language"]=language});
            yield return request.SendWebRequest();lifecycle.Untrack(c,request);
            if(!lifecycle.Guard(c,"llm_response"))yield break;
            LatencyTrace.Mark(c.Trace,"llm_response_received",new JObject{["status_code"]=request.responseCode,["response_length"]=request.downloadedBytes});
            if(request.responseCode==409){lifecycle.Cancel(c,"backend_cancelled",false);yield break;}
            if(request.result!=UnityWebRequest.Result.Success){Debug.LogWarning("Agent Request Error: "+request.error);Failure(c,"agent_request_failed");yield break;}
            AgentResponse response=null;
            try{response=AgentBehaviorContract.Parse(request.downloadHandler.text);}
            catch(System.Exception e){Debug.LogWarning("Failed to parse Agent response: "+e.GetType().Name);}
            if(!lifecycle.Guard(c,"agent_reply_parsed"))yield break;
            if(response==null||string.IsNullOrWhiteSpace(response.speech)){Failure(c,"agent_parse_failed");yield break;}
            ReliabilityTrace.SetHealth(c,"LLM",ComponentHealth.Healthy);
            ReliabilityTrace.SetHealth(c,"Backend",ComponentHealth.Healthy);
            if(completeFallbackInProgress){ReliabilityTrace.Event(c,"fallback_succeeded",new JObject{["fallback_target"]="complete_agent"});completeFallbackInProgress=false;}
            currentAction=response.action;currentSpeech=response.speech;isRequesting=false;
            LatencyTrace.Mark(c.Trace,"agent_reply_parsed");
            OnAgentReplyParsed?.Invoke(new AgentReplyEnvelope(agentId,response.speech,response.action,response.behavior,c.BodyToolsDisabledForReliability));
            // An event subscriber may have synchronously cancelled/replaced this turn.
            if(!lifecycle.Guard(c,"agent_to_tts"))yield break;
            if(ttsPlayer==null){Failure(c,"tts_reference_missing");yield break;}
            if(!ttsPlayer.Arbiter.Submit(SpeechRequest.ForTurn(c,response.speech,language)))
                Failure(c,"speech_request_rejected");
        }
    }
    IEnumerator SendStream(string json,string language,TurnContext c,long agentId,System.Action<bool> completed)
    {
        var arbiter=ttsPlayer.Arbiter;
        if(!arbiter.OpenTurnSpeechSequence(c)){completed(true);yield break;}
        bool accepted=false,validated=false,closed=false,toolRequested=false; int sequence=0;
        string nativeRequestId=null;int nativeRequests=0;
        string failure=null;int fallbackDepth=0;var spoken=new StringBuilder();
        using(var request=new UnityWebRequest(BackendEndpoints.Resolve(serverUrl,"/agent").TrimEnd('/')+"/stream","POST"))
        {
            var handler=new AgentStreamDownloadHandler();request.downloadHandler=handler;
            request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.SetRequestHeader("Content-Type","application/json");
            if(enableNativeToolCalling)request.SetRequestHeader("X-Native-Tool-Calling","true");
            request.timeout=90;
            lifecycle.Track(c,request,"llm_stream_request");
            LatencyTrace.Mark(c.Trace,"llm_stream_request_started");
            var operation=request.SendWebRequest();
            while(true)
            {
                if(!lifecycle.Guard(c,"llm_stream_event"))
                {request.Abort();lifecycle.Untrack(c,request);arbiter.AbandonTurnSpeechSequence(c);completed(true);yield break;}
                while(handler.Events.TryDequeue(out var raw))
                {
                    try
                    {
                        var e=JObject.Parse(raw);
                        if((string)e["trace_id"]!=c.TraceId||(long?)e["turn_id"]!=c.TurnId)throw new System.Exception("stream_identity");
                        string type=(string)e["type"];
                        if(type=="llm_first_token")LatencyTrace.Mark(c.Trace,"llm_first_token_received");
                        else if(type=="llm_speech_field_started")LatencyTrace.Mark(c.Trace,"llm_speech_field_started");
                        else if(type=="llm_stream_completed")LatencyTrace.Mark(c.Trace,"llm_stream_completed");
                        else if(type=="native_tool_mode_started")
                        {
                            if(!enableNativeToolCalling||nativeRequestId!=null||accepted)throw new System.Exception("native_start_protocol");
                            c.BodyToolsDisabledForReliability=(bool?)e["degraded_context"]??false;
                            var registry=new CapabilityRegistry();
                            nativeRequestId=(string)e["agent_request_id"];
                            if(string.IsNullOrWhiteSpace(nativeRequestId)||(string)e["manifest_hash"]!=registry.ManifestHash)throw new System.Exception("native_start_identity");
                            LatencyTrace.Mark(c.Trace,"llm_tool_mode_started",new JObject{["agent_request_id"]=nativeRequestId});
                        }
                        else if(type=="tool_call_requested")
                        {
                            if(!enableNativeToolCalling||nativeRequestId==null||++nativeRequests>6||accepted||validated||closed||(string)e["identity"]?["agent_request_id"]!=nativeRequestId)throw new System.Exception("unexpected_tool_request");
                            toolRequested=true;
                            StartCoroutine(ReturnNativeToolResult(e,c,language,nativeRequestId));
                        }
                        else if(type=="speech_segment_ready")
                        {
                            if(closed||validated||(int?)e["sequence_index"]!=sequence||string.IsNullOrWhiteSpace((string)e["text"]))throw new System.Exception("invalid_segment");
                            string segment=(string)e["text"];
                            if((string)e["language"]!=language)throw new System.Exception("stream_language");
                            LatencyTrace.Mark(c.Trace,"llm_sentence_ready",new JObject{["sequence_index"]=sequence,["text_length"]=segment.Length});
                            var speech=SpeechRequest.ForTurn(c,segment,language,isLastSpeech:false);
                            speech.requestId=(string)e["segment_id"];
                            if(string.IsNullOrWhiteSpace(speech.requestId)||!arbiter.Submit(speech))throw new System.Exception("segment_rejected");
                            accepted=true;sequence++;spoken.Append(segment);
                            LatencyTrace.Mark(c.Trace,"speech_segment_submitted",new JObject{["sequence_index"]=sequence-1,["queue_depth"]=arbiter.QueueDepth});
                        }
                        else if(type=="final_validated")
                        {
                            if(validated)throw new System.Exception("duplicate_final");
                            var response=AgentBehaviorContract.Parse(e["reply"].ToString());
                            if(CanonicalSpeech(spoken.ToString())!=CanonicalSpeech(response.speech))
                            {LatencyTrace.Mark(c.Trace,"streamed_speech_mismatch");throw new System.Exception("streamed_speech_mismatch");}
                            validated=true;currentSpeech=response.speech;currentAction=response.action;isRequesting=false;
                            LatencyTrace.Mark(c.Trace,"llm_final_validated",new JObject{["parser_ms"]=e["parser_ms"],["buffered_characters"]=e["buffered_characters"]});
                            LatencyTrace.Mark(c.Trace,"agent_reply_parsed");
                            OnAgentReplyParsed?.Invoke(new AgentReplyEnvelope(agentId,response.speech,response.action,response.behavior,(bool?)e["native_tool_mode"]??false));
                        }
                        else if(type=="sequence_closed")
                        {
                            if(!validated||closed||sequence==0)throw new System.Exception("invalid_sequence_close");
                            closed=true;
                        }
                        else if(type=="error"){fallbackDepth=(int?)e["fallback_depth"]??0;throw new System.Exception((string)e["reason"]??"stream_failed");}
                        else throw new System.Exception("unknown_stream_event");
                    }
                    catch(System.Exception ex){failure=ex.Message;break;}
                    if(!lifecycle.Guard(c,"stream_event_callbacks")){failure="turn_cancelled";break;}
                }
                if(failure!=null||closed||operation.isDone)break;
                yield return null;
            }
            if(!operation.isDone)request.Abort();
            lifecycle.Untrack(c,request);
            if(closed&&failure==null&&lifecycle.Guard(c,"stream_sequence_close"))
            {arbiter.CloseTurnSpeechSequence(c);completed(true);yield break;}
            arbiter.AbandonTurnSpeechSequence(c);
            if(lifecycle.Guard(c,"stream_failure"))
            {
                LatencyTrace.Mark(c.Trace,"llm_stream_failed",new JObject{["reason"]=failure??handler.Error??request.error??"stream_incomplete",["partial_response_spoken"]=c.Trace.Times.ContainsKey("playback_started"),["speech_segment_admitted"]=accepted});
                var recovery=ReliabilityTrace.Failure(c,"llm_stream",FailureCategory.LLM,"llm_stream_failed",accepted,toolRequested,fallbackDepth);
                if(recovery.Action!=ReliabilityAction.Fallback)
                {isRequesting=false;lifecycle.Cancel(c,"llm_sequence_incomplete");if(conversationState&&!conversationState.IsUserSpeaking())conversationState.SetState(ConversationStateManager.ConversationState.WAITING_FOR_MORE);}
            }
            if(!accepted&&!toolRequested&&!c.IsTerminal)ReliabilityTrace.Event(c,"fallback_started",new JObject{["fallback_target"]="complete_agent",["fallback_depth"]=fallbackDepth+1});
            completed(accepted||toolRequested||c.IsTerminal);
        }
    }
    IEnumerator ReturnNativeToolResult(JObject e,TurnContext c,string language,string requestId)
    {
        if(!lifecycle.Guard(c,"native_tool_dispatch"))yield break;
        JObject body=null;
        try
        {
            var identity=e["identity"] as JObject;
            var call=ToolCall.Parse(e["call"] as JObject);
            if(identity==null||(string)identity["agent_request_id"]!=requestId||(string)identity["trace_id"]!=c.TraceId||(long?)identity["turn_id"]!=c.TurnId||
                (string)identity["call_id"]!=call.CallId||(string)identity["tool_name"]!=call.ToolName||
                call.TraceId!=c.TraceId||call.TurnId!=c.TurnId||string.IsNullOrWhiteSpace((string)identity["agent_request_id"])||string.IsNullOrWhiteSpace((string)identity["api_tool_call_id"]))
                throw new System.Exception("native_tool_identity");
            var store=AgentStateStore.For(this);
            if(nativeToolBridge==null)
            {
                AgentBehaviorAdapter adapter=null;
                foreach(var candidate in FindObjectsByType<AgentBehaviorAdapter>(FindObjectsSortMode.None))if(candidate.agent==this){adapter=candidate;break;}
                if(adapter==null)throw new System.Exception("native_tool_adapter_missing");
                nativeToolBridge=new ToolExecutionBridge(adapter,store);
            }
            if((string)identity["manifest_hash"]!=(string)nativeToolBridge.Registry.Manifest()["manifest_hash"])throw new System.Exception("native_manifest_mismatch");
            if(!lifecycle.Guard(c,"native_tool_fresh_dispatch"))throw new System.Exception("turn_cancelled");
            var result=nativeToolBridge.Execute(call);
            if(result.Status==ToolResultStatus.Duplicate)ReliabilityTrace.Event(c,"duplicate_prevented");
            if(result.Status==ToolResultStatus.Failed)ReliabilityTrace.Failure(c,"Tool",FailureCategory.Tool,"executor_failed",toolRequested:true);
            if(!lifecycle.Guard(c,"native_tool_result_snapshot"))throw new System.Exception("turn_cancelled");
            body=(JObject)identity.DeepClone();body["result"]=result.ToJson();
            body["stateSnapshot"]=store.CreateSnapshot(c,language).ToJsonObject();
        }
        catch(System.Exception ex)
        {LatencyTrace.Mark(c.Trace,"tool_result_discarded",new JObject{["reason"]=ex.Message});
            var code=ex.Message=="native_manifest_mismatch"?"manifest_mismatch":"executor_contract_failed";
            var decision=ReliabilityTrace.Failure(c,"Tool",FailureCategory.Tool,code,toolRequested:true);
            if(decision.Action==ReliabilityAction.Escalate)enableNativeToolCalling=false; // runtime only; never saves production Scene
        }
        if(body==null||!lifecycle.Guard(c,"native_tool_result_post"))yield break;
        using(var request=new UnityWebRequest(BackendEndpoints.Resolve(serverUrl,"/agent").TrimEnd('/')+"/tool-result","POST"))
        {
            request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
            request.downloadHandler=new DownloadHandlerBuffer();request.SetRequestHeader("Content-Type","application/json");request.timeout=5;
            lifecycle.Track(c,request,"native_tool_result");yield return request.SendWebRequest();lifecycle.Untrack(c,request);
            if(!lifecycle.Guard(c,"native_tool_result_ack"))yield break;
            if(request.result!=UnityWebRequest.Result.Success)ReliabilityTrace.Failure(c,"Tool",FailureCategory.Tool,"tool_result_timeout",toolRequested:true);
            else ReliabilityTrace.SetHealth(c,"Tool",ComponentHealth.Healthy);
            LatencyTrace.Mark(c.Trace,request.result==UnityWebRequest.Result.Success?"tool_result_received":"tool_result_discarded",new JObject{["status_code"]=request.responseCode});
        }
    }
    static string CanonicalSpeech(string text)
    {var b=new StringBuilder();foreach(char c in text??"")if(!char.IsWhiteSpace(c))b.Append(c);return b.ToString();}
    void Failure(TurnContext c,string reason)
    {
        if(!lifecycle.Guard(c,"agent_failure"))return;
        ReliabilityTrace.Failure(c,"complete_agent",reason=="agent_parse_failed"?FailureCategory.Validation:FailureCategory.LLM,"agent_failed");
        if(completeFallbackInProgress){ReliabilityTrace.Event(c,"fallback_failed",new JObject{["fallback_target"]="complete_agent"});completeFallbackInProgress=false;}
        currentSpeech=ReliabilityTrace.RecoveryText(c.LanguageCode);currentAction="NONE";
        isRequesting=false;
        if(conversationState!=null)conversationState.SetState(ConversationStateManager.ConversationState.WAITING_FOR_MORE);
        lifecycle.Fail(c,reason);
    }
}

