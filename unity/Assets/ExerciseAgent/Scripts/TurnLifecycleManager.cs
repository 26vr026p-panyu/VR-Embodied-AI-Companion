using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public sealed class TurnLifecycleManager : MonoBehaviour
{
    static TurnLifecycleManager instance;
    static bool quitting;
    public static TurnLifecycleManager Instance
    {
        get
        {
            if(instance==null&&!quitting)
            {
                instance=FindAnyObjectByType<TurnLifecycleManager>();
                if(instance==null)instance=new GameObject("Turn Lifecycle (runtime only)").AddComponent<TurnLifecycleManager>();
            }
            return instance;
        }
    }
    public TurnContext Current { get; private set; }
    public event Action<TurnContext> OnTurnCancelled;
    readonly Dictionary<string,TurnContext> turns=new Dictionary<string,TurnContext>();
    readonly Dictionary<long,TurnContext> agentTurns=new Dictionary<long,TurnContext>();
    static string Key(string id,long n)=>id+":"+n;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){instance=null;quitting=false;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install(){var _=Instance;}
    void Awake(){if(instance!=null&&instance!=this){Destroy(gameObject);return;}instance=this;DontDestroyOnLoad(gameObject);}
    public TurnContext ForTrace(LatencyTrace.Turn t){if(t==null)return null;turns.TryGetValue(Key(t.Id,t.Number),out var c);return c;}
    public TurnContext ForAgent(long n){agentTurns.TryGetValue(n,out var c);return c;}
    public TurnContext Begin(string kind)=>Adopt(LatencyTrace.Begin(kind));
    public TurnContext Adopt(LatencyTrace.Turn trace)
    {
        if(trace==null)return Begin("text");
        var known=ForTrace(trace);if(known!=null)return known;
        CancelPreviousTurn("superseded");
        var c=new TurnContext{Trace=trace};turns.Add(Key(c.TraceId,c.TurnId),c);Current=c;
        LatencyTrace.Mark(trace,"turn_lifecycle_created");return c;
    }
    public bool IsThisTurnStillCurrentAndActive(long turnId,string traceId)
    {
        return Current!=null&&Current.TurnId==turnId&&Current.TraceId==traceId&&!Current.IsTerminal;
    }
    public bool Guard(TurnContext c,string stage)
    {
        if(c!=null&&IsThisTurnStillCurrentAndActive(c.TurnId,c.TraceId))return true;
        if(c!=null)ReliabilityTrace.Event(c,"stale_result_discarded");
        if(c!=null)LatencyTrace.Mark(c.Trace,"late_result_discarded",new JObject{["stage"]=stage,["reason"]=c.IsCancelled?"turn_cancelled":c.IsTerminal?"turn_terminal":"superseded"});
        return false;
    }
    public bool GuardAgent(long id,string stage)
    {
        var c=ForAgent(id);
        // Existing Debug/local turn-zero and legacy fixtures remain compatible.
        return c==null || Guard(c,stage);
    }
    public bool Transition(TurnContext c,TurnLifecycleState state)
    {
        if(!Guard(c,"state_transition"))return false;
        if(state==TurnLifecycleState.Cancelled||state==TurnLifecycleState.Completed||state==TurnLifecycleState.Failed)return false;
        c.State=state;LatencyTrace.Mark(c.Trace,"turn_state_changed",new JObject{["state"]=state.ToString()});return true;
    }
    public void BindAgent(TurnContext c,long id){if(!Guard(c,"bind_agent"))return;LatencyTrace.Bind(c.Trace,id);agentTurns[id]=c;}
    public void Track(TurnContext c,UnityWebRequest request,string stage)
    {
        if(!Guard(c,stage))return;
        c.Requests[request]=stage;LatencyTrace.Headers(request,c.Trace);
        try { c.BackendBaseUrl=new Uri(request.url).GetLeftPart(UriPartial.Authority); }catch{ }
    }
    public void Untrack(TurnContext c,UnityWebRequest request){c?.Requests.Remove(request);}
    public void CancelPreviousTurn(string reason="new_user_turn"){if(Current!=null&&!Current.IsTerminal)Cancel(Current,reason);}
    public bool Cancel(TurnContext c,string reason,bool notifyBackend=true)
    {
        if(c==null||c.IsTerminal)return false;
        LatencyTrace.Mark(c.Trace,"turn_cancel_requested",new JObject{["reason"]=reason});
        c.State=TurnLifecycleState.Cancelled;c.CancellationReason=reason;
        ReliabilityTrace.Event(c,"cancellation_precedence");
        LatencyTrace.Mark(c.Trace,"turn_cancelled_local",new JObject{["reason"]=reason});
        foreach(var item in new List<KeyValuePair<UnityWebRequest,string>>(c.Requests))
        {
            try{item.Key.Abort();LatencyTrace.Mark(c.Trace,"request_aborted",new JObject{["stage"]=item.Value});}catch{ }
        }
        c.Requests.Clear();
        // Each consumer only cleans up resources that belong to this context.
        if(OnTurnCancelled!=null)foreach(Action<TurnContext> action in OnTurnCancelled.GetInvocationList())
            try{action(c);}catch(Exception e){Debug.LogWarning("[TurnLifecycle] Cancellation cleanup: "+e.GetType().Name);}
        LatencyTrace.End(c.Trace,"cancelled");
        if(notifyBackend&&isActiveAndEnabled)StartCoroutine(Notify(c,"cancel",reason));
        return true;
    }
    public bool Complete(TurnContext c)
    {
        if(!Guard(c,"completion"))return false;
        c.State=TurnLifecycleState.Completed;LatencyTrace.End(c.Trace,"completed");
        StartCoroutine(Notify(c,"complete",null));return true;
    }
    public void Fail(TurnContext c,string reason)
    {
        if(!Guard(c,"failure"))return;
        ReliabilityTrace.Event(c,"turn_aborted");
        c.State=TurnLifecycleState.Failed;
        LatencyTrace.End(c.Trace,reason);
        // Discard any uncommitted server reply on failure, too.
        if(isActiveAndEnabled)StartCoroutine(Notify(c,"cancel",reason));
    }
    IEnumerator Notify(TurnContext c,string command,string reason)
    {
        var body=new JObject{["trace_id"]=c.TraceId,["turn_id"]=c.TurnId};if(reason!=null)body["reason"]=reason;
        using(var request=new UnityWebRequest(c.BackendBaseUrl+"/turn/"+command,"POST"))
        {
            request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
            request.downloadHandler=new DownloadHandlerBuffer();request.SetRequestHeader("Content-Type","application/json");request.timeout=5;
            LatencyTrace.Headers(request,c.Trace);
            LatencyTrace.Mark(c.Trace,command=="cancel"?"turn_cancel_sent":"turn_completion_sent");
            yield return request.SendWebRequest();
            if(request.result==UnityWebRequest.Result.Success)
                LatencyTrace.Mark(c.Trace,command=="cancel"?"turn_cancel_acknowledged":"turn_completion_acknowledged",new JObject{["status_code"]=request.responseCode});
            else LatencyTrace.Mark(c.Trace,"turn_notification_failed",new JObject{["operation"]=command,["status_code"]=request.responseCode});
        }
    }
    void OnApplicationQuit(){CancelPreviousTurn("application_quit");quitting=true;}
    void OnDestroy(){if(instance==this){CancelPreviousTurn("manager_destroyed");instance=null;}}
}
