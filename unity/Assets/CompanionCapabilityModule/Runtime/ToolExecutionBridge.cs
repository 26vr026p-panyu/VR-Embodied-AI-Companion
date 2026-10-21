using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using CompanionAvatar;
using Newtonsoft.Json.Linq;

namespace CompanionCapabilities
{
    /// <summary>Opt-in deterministic/developer bridge. Only submits Normal Agent behavior requests.
    /// No queue: dispatch is synchronous; accepted running effects retain existing cancellation policy.</summary>
    public sealed class ToolExecutionBridge
    {
        readonly AgentBehaviorAdapter adapter;
        readonly AgentStateStore store;
        readonly CapabilityRegistry registry;
        readonly IToolSafetyPolicy safety;
        sealed class Ledger {public readonly Dictionary<string,ToolResult> consumed=new Dictionary<string,ToolResult>();public readonly HashSet<string> inFlight=new HashSet<string>();}
        static readonly ConditionalWeakTable<AgentBehaviorAdapter,Ledger> ledgers=new ConditionalWeakTable<AgentBehaviorAdapter,Ledger>();
        readonly Dictionary<string,ToolResult> consumed;
        readonly HashSet<string> inFlight;
        public bool RejectStateVersionMismatch {get;set;} // V1 default false: StateStore versions count observations.
        public double LastDispatchMs {get;private set;}
        public int DispatchCount {get;private set;}
        public CapabilityRegistry Registry=>registry;
        public ToolExecutionBridge(AgentBehaviorAdapter adapter,AgentStateStore store,IToolSafetyPolicy safety=null)
        {if(adapter==null)throw new ArgumentNullException(nameof(adapter));this.adapter=adapter;this.store=store;this.safety=safety??new AllowExistingBehaviorPolicy();registry=new CapabilityRegistry();var ledger=ledgers.GetValue(adapter,_=>new Ledger());consumed=ledger.consumed;inFlight=ledger.inFlight;}
        public ToolResult Execute(ToolCall call)
        {
            long started=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();var watch=Stopwatch.StartNew();
            var life=TurnLifecycleManager.Instance;var turn=life?.Current;long version=store?.LastSnapshot?.StateVersion??0;
            void Mark(string name,ToolResult result=null,JObject extra=null)
            {
                var meta=new JObject{["call_id"]=call?.CallId,["tool_name"]=call?.ToolName,["turn_id"]=call?.TurnId??0,["requested_state_version"]=call?.StateVersion??0,["execution_state_version"]=version};
                if(result!=null){meta["result_status"]=result.Status.ToString();meta["rejection_reason"]=result.Reason;}
                meta["completion_scope"]="dispatch_only";
                if(extra!=null)foreach(var p in extra.Properties())meta[p.Name]=p.Value.DeepClone();
                if(turn!=null&&turn.TraceId==call?.TraceId)LatencyTrace.Mark(turn.Trace,name,meta);else LatencyTrace.CaptureMark("capabilities",name,meta);
            }
            ToolResult End(ToolResultStatus status,string reason,JObject detail=null)
            {
                var result=new ToolResult(call,status,reason,version,started,detail);
                if(call!=null&&status!=ToolResultStatus.Duplicate){consumed[call.CallId]=result;inFlight.Remove(call.CallId);}
                LastDispatchMs=watch.Elapsed.TotalMilliseconds;
                Mark(status==ToolResultStatus.Failed?"tool_execution_failed":status==ToolResultStatus.Accepted?"tool_execution_completed":"tool_execution_rejected",result,new JObject{["execution_dispatch_ms"]=LastDispatchMs});
                return result;
            }
            Mark("tool_call_received");
            if(call==null)return End(ToolResultStatus.Rejected,"invalid_arguments");
            if(consumed.TryGetValue(call.CallId,out var prior))return End(ToolResultStatus.Duplicate,"duplicate_call",new JObject{["original_result"]=prior.ToJson()});
            if(!inFlight.Add(call.CallId))return End(ToolResultStatus.Duplicate,"duplicate_call",new JObject{["original_result_status"]="dispatch_in_progress"});
            if(!registry.Contains(call.ToolName))return End(ToolResultStatus.Rejected,"unknown_tool");
            JObject args;
            try{args=registry.ValidateArguments(call.ToolName,call.Arguments);}catch(ArgumentException){return End(ToolResultStatus.Rejected,"invalid_arguments");}
            // GuardAgent alone allows legacy unknown IDs: Tool entry does NOT use that permissive path.
            if(turn==null||call.TurnId!=turn.TurnId||call.TraceId!=turn.TraceId||!life.Guard(turn,"tool_call")||turn.AgentTurnId<=0||adapter?.agent==null||turn.AgentTurnId!=adapter.agent.CurrentTurnId)
                return End(ToolResultStatus.Rejected,"invalid_turn");
            if(store==null||adapter.orchestrator==null||store.agent!=adapter.agent)return End(ToolResultStatus.Rejected,"unavailable");
            AgentStateSnapshot snapshot;
            try{snapshot=store.CreateSnapshot(turn,turn.LanguageCode);version=snapshot.StateVersion;}
            catch(Exception){return End(ToolResultStatus.Failed,"execution_failed");}
            if(RejectStateVersionMismatch&&call.StateVersion!=version)return End(ToolResultStatus.Rejected,"stale_state");
            var availability=(JObject)registry.Availability(snapshot)["tools"][call.ToolName];
            if((bool)availability["available"]==false)return End(ToolResultStatus.Rejected,(string)availability["reason"],availability);
            try{if(!safety.Allows(call,snapshot))return End(ToolResultStatus.Rejected,"policy_rejected");}
            catch(Exception){return End(ToolResultStatus.Failed,"execution_failed");}
            // Safety hook cannot grant higher priority or bypass final authority.
            if(!life.Guard(turn,"tool_dispatch"))return End(ToolResultStatus.Rejected,"invalid_turn");
            var request=new CompanionBehaviorRequest{requestId="tool:"+call.CallId,source=BehaviorSource.Agent,turnId=turn.AgentTurnId,priority=BehaviorPriority.Normal,ttl=5,expressionReleasePolicy=ExpressionReleasePolicy.ReturnToNeutral};
            switch(call.ToolName)
            {
                case "gesture.nod":request.gesture=BehaviorGesture.Nod;break;
                case "gesture.wave":request.gesture=BehaviorGesture.Wave;break;
                case "expression.set":
                    string semantic=(string)args["expression"];
                    request.expression=semantic=="neutral"?"Neutral":MapExpression(semantic);
                    if(request.expression==null)return End(ToolResultStatus.Rejected,"unavailable");
                    request.intensity=(float)args["intensity"];request.expressionHoldDuration=(float)args["hold_seconds"];break;
                case "spatial.lead":request.spatialIntent=BehaviorSpatialIntent.Lead;break;
                case "spatial.side":request.spatialIntent=BehaviorSpatialIntent.Side;break;
                case "spatial.trail":request.spatialIntent=BehaviorSpatialIntent.Trail;break;
                case "spatial.nearby":request.spatialIntent=BehaviorSpatialIntent.Nearby;break;
                default:return End(ToolResultStatus.Rejected,"unknown_tool");
            }
            Mark("tool_call_validated",extra:new JObject{["argument_validation_ms"]=registry.LastValidationMs,["availability_evaluation_ms"]=registry.LastAvailabilityMs});
            Mark("tool_execution_started");
            try
            {
                var decision=adapter.orchestrator.Submit(request);DispatchCount++;
                var detail=new JObject{["request"]=decision.request,["gesture"]=decision.gesture,["expression"]=decision.expression,["spatial"]=decision.spatial,["effect_completed"]=false};
                if(decision.AnyAccepted)return End(ToolResultStatus.Accepted,null,detail);
                string rejection=decision.request!="Valid"?decision.request:call.ToolName.StartsWith("gesture.")?decision.gesture:call.ToolName.StartsWith("expression.")?decision.expression:decision.spatial;
                return End(ToolResultStatus.Rejected,Reason(rejection),detail);
            }
            catch(Exception){return End(ToolResultStatus.Failed,"execution_failed");}
        }
        string MapExpression(string semantic)
        {
            var face=adapter.orchestrator.expression;var mesh=face==null||face.faceRenderer==null?null:face.faceRenderer.sharedMesh;
            if(mesh==null)return null;
            foreach(var binding in adapter.expressionMapping??Array.Empty<SemanticExpressionBinding>())
            {
                if(binding==null||binding.semantic!=semantic)continue;
                string shape=binding.avatarShape;
                return string.IsNullOrEmpty(shape)||new[]{"aa","ih","ou","ee","oh","blink"}.Contains(shape)||mesh.GetBlendShapeIndex(shape)<0?null:shape;
            }
            return null;
        }
        static string Reason(string authority)
        {
            switch(authority)
            {
                case "UserSpeaking":return "user_speaking";case "Busy":return "busy";case "Cooldown":return "cooldown";
                case "Unchanged":return "already_in_state";case "StaleTurn":case "Expired":return "invalid_turn";
                case "Duplicate":return "duplicate_call";case "Invalid":return "invalid_arguments";
                case "Disabled":case "Unavailable":return "unavailable";default:return "policy_rejected";
            }
        }
    }
}

