using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

public enum FailureCategory { Input,State,ASR,LLM,Validation,Tool,TTS,Playback,Cancellation,History,Infrastructure,Performance }
public enum FailureSeverity { Info,Warning,Recoverable,TurnFatal,SystemFatal }
public enum ReliabilityAction { Retry,Fallback,Degrade,AbortTurn,Ignore,Escalate }
public enum ComponentHealth { Healthy,Degraded,Unavailable }
[Serializable] public sealed class FailureRecord
{
    public string failure_id=Guid.NewGuid().ToString("N"),trace_id,component,code;
    public long turn_id,occurred_at=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public FailureCategory category;public FailureSeverity severity=FailureSeverity.Recoverable;
    public string recoverability="policy",message_safe="Component operation failed.",fallback_target;
    public int retry_count;public bool fallback_attempted;
}
public sealed class ReliabilityDecision
{
    public ReliabilityAction Action;public string Reason,Target;public int MaxAttempts;
    public ReliabilityDecision(ReliabilityAction action,string reason,string target=null,int attempts=0){Action=action;Reason=reason;Target=target;MaxAttempts=attempts;}
}
/// <summary>Pure decision table. Does not execute audio, controllers, requests or history.</summary>
public static class ReliabilityPolicy
{
    public static ReliabilityDecision Decide(FailureRecord f,bool cancelled=false,bool output=false,bool toolRequested=false,int depth=0)
    {
        if(cancelled)return new ReliabilityDecision(ReliabilityAction.Ignore,"cancelled");
        if(f.code=="already_in_state"||f.code=="user_speaking"||f.code=="busy"||f.code=="cooldown"||f.code=="policy_rejected")return new ReliabilityDecision(ReliabilityAction.Ignore,"normal_tool_result");
        if(f.code=="invalid_state")return new ReliabilityDecision(ReliabilityAction.Degrade,"invalid_optional_state","minimal_context");
        if(f.code=="tool_result_timeout")return new ReliabilityDecision(ReliabilityAction.Degrade,"execution_unknown","tool_result_failed");
        if(f.code=="manifest_mismatch")return new ReliabilityDecision(ReliabilityAction.Escalate,"executor_contract_mismatch","disable_native_tools");
        if(depth>=2)return new ReliabilityDecision(ReliabilityAction.AbortTurn,"fallback_budget_exhausted");
        if(f.component=="llm_stream"&&!output&&!toolRequested)return new ReliabilityDecision(ReliabilityAction.Fallback,"before_output","complete_agent",1);
        if(f.component=="tts_stream"&&!output)return new ReliabilityDecision(ReliabilityAction.Fallback,"before_playback","complete_tts",1);
        return new ReliabilityDecision(ReliabilityAction.AbortTurn,"no_safe_replay");
    }
}
/// <summary>Fail-open, metadata-only instrumentation. No recovery side effects.</summary>
public static class ReliabilityTrace
{
    static readonly Dictionary<string,long> counts=new Dictionary<string,long>();
    static readonly Dictionary<string,ComponentHealth> health=new Dictionary<string,ComponentHealth>();
    sealed class FailureLinks { public readonly Dictionary<string,JObject> ByTarget=new Dictionary<string,JObject>(); }
    static ConditionalWeakTable<TurnContext,FailureLinks> links=new ConditionalWeakTable<TurnContext,FailureLinks>();
    static readonly Stopwatch clock=Stopwatch.StartNew();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){links=new ConditionalWeakTable<TurnContext,FailureLinks>();counts.Clear();health.Clear();foreach(string name in new[]{"LLM","TTS","Tool","Backend","ASR"})health[name]=ComponentHealth.Healthy;clock.Restart();}
    public static ReliabilityDecision Failure(TurnContext c,string component,FailureCategory category,string code,bool output=false,bool toolRequested=false,int depth=0)
    {
        var f=new FailureRecord{trace_id=c?.TraceId,turn_id=c?.TurnId??0,component=component,category=category,code=code};
        var watch=Stopwatch.StartNew();var d=ReliabilityPolicy.Decide(f,c==null||c.IsTerminal,output,toolRequested,depth);watch.Stop();
        var data=new JObject{["failure_id"]=f.failure_id,["component"]=component,["category"]=category.ToString(),["code"]=code,["decision"]=d.Action.ToString(),["reason"]=d.Reason,["retry_count"]=0,["fallback_target"]=d.Target,["fallback_depth"]=depth,["policy_ms"]=watch.Elapsed.TotalMilliseconds};
        if(code=="manifest_mismatch")f.severity=FailureSeverity.SystemFatal;
        data["severity"]=f.severity.ToString();data["recoverability"]=f.recoverability;data["occurred_at"]=f.occurred_at;data["message_safe"]=f.message_safe;data["fallback_attempted"]=d.Action==ReliabilityAction.Fallback;
        if(c!=null&&d.Target!=null)links.GetOrCreateValue(c).ByTarget[d.Target]=(JObject)data.DeepClone();
        if(d.Reason!="normal_tool_result")Event(c,"failure_detected",data);
        Event(c,"reliability_decision",data);
        if(d.Action==ReliabilityAction.AbortTurn)Event(c,"turn_aborted",data);
        if(d.Action==ReliabilityAction.Degrade)Event(c,"turn_degraded",data);
        if(d.Reason!="normal_tool_result"&&d.Reason!="cancelled")SetHealth(c,component.Contains("llm")||component=="complete_agent"?"LLM":component.Contains("tts")||component=="TTS"?"TTS":component,d.Action==ReliabilityAction.Escalate?ComponentHealth.Unavailable:ComponentHealth.Degraded);return d;
    }
    public static void SetHealth(TurnContext c,string component,ComponentHealth value)
    {
        if(health.TryGetValue(component,out var previous)&&previous==value)return;
        health[component]=value;Event(c,"component_health_changed",new JObject{["health_component"]=component,["health"]=value.ToString()});
    }
    public static void Event(TurnContext c,string name,JObject data=null)
    {
        if(c!=null&&data!=null&&!string.IsNullOrEmpty((string)data["fallback_target"])&&links.TryGetValue(c,out var known)&&known.ByTarget.TryGetValue((string)data["fallback_target"],out var original)){
            var merged=(JObject)original.DeepClone();foreach(var item in data)merged[item.Key]=item.Value.DeepClone();data=merged;
        }
        if(!counts.ContainsKey(name))counts[name]=0;counts[name]++;
        if(name=="failure_detected"&&data!=null){foreach(string prefix in new[]{"component","category"}){string key="failures_by_"+prefix+":"+(string)data[prefix];if(!counts.ContainsKey(key))counts[key]=0;counts[key]++;}}
        try{
            var row=data==null?new JObject():(JObject)data.DeepClone();row["event"]=name;row["trace_id"]=c?.TraceId;row["turn_id"]=c?.TurnId;
            row["utc_ms"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();row["mono_ms"]=clock.Elapsed.TotalMilliseconds;row["process_component"]="unity";
            string folder=Path.Combine(Application.persistentDataPath,"ReliabilityTrace");Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"reliability.jsonl"),row.ToString(Newtonsoft.Json.Formatting.None)+"\n");
        }catch(Exception){ }
    }
    public static JObject Metrics(){var result=new JObject();foreach(var item in counts)result[item.Key]=item.Value;foreach(string type in new[]{"retry","fallback"}){counts.TryGetValue(type+"_started",out var n);counts.TryGetValue(type+"_succeeded",out var s);result[type+"_success_rate"]=n>0?new JValue((double)s/n):JValue.CreateNull();}return result;}
    public static string RecoveryText(string language)=>language=="zh"?"抱歉，这次没能回答。":"ごめん、今うまく返せなかった。";
}
