using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CompanionCapabilities
{
    /// <summary>Immutable semantic command. turn_id is the logical trace turn, not Agent's reply counter.</summary>
    public sealed class ToolCall
    {
        public string CallId {get;} public string ToolName {get;} public string TraceId {get;}
        public long TurnId {get;} public long StateVersion {get;}
        readonly string arguments;
        public JObject Arguments=>JObject.Parse(arguments);
        ToolCall(JObject value)
        {CallId=(string)value["call_id"];ToolName=(string)value["tool_name"];TraceId=(string)value["trace_id"];TurnId=(long)value["turn_id"];StateVersion=(long)value["state_version"];arguments=value["arguments"].ToString();}
        public static ToolCall Parse(JObject value)
        {
            var fields=new[]{"call_id","tool_name","arguments","turn_id","trace_id","state_version"};
            if(value==null||value.Count!=fields.Length||value.Properties().Any(p=>!fields.Contains(p.Name)))throw new ArgumentException("invalid_arguments");
            foreach(string field in new[]{"call_id","tool_name","trace_id"})
            {var token=value[field];if(token?.Type!=JTokenType.String||string.IsNullOrWhiteSpace((string)token)||((string)token).Length>96||((string)token).Any(char.IsControl))throw new ArgumentException("invalid_arguments");}
            foreach(string field in new[]{"turn_id","state_version"})
            {if(value[field]?.Type!=JTokenType.Integer)throw new ArgumentException("invalid_arguments");try{if((long)value[field]<=0)throw new ArgumentException("invalid_arguments");}catch(OverflowException){throw new ArgumentException("invalid_arguments");}}
            if(!(value["arguments"] is JObject)||Encoding.UTF8.GetByteCount(value["arguments"].ToString())>4096)throw new ArgumentException("invalid_arguments");
            return new ToolCall(value);
        }
        public static ToolCall Create(string id,string tool,JObject arguments,TurnContext turn,long stateVersion)=>Parse(new JObject{["call_id"]=id,["tool_name"]=tool,["arguments"]=arguments,["turn_id"]=turn.TurnId,["trace_id"]=turn.TraceId,["state_version"]=stateVersion});
    }
    public enum ToolResultStatus { Accepted,Rejected,Completed,Failed,Cancelled,Duplicate }
    public sealed class ToolResult
    {
        readonly string json;
        public ToolResultStatus Status {get;}
        public string Reason {get;}
        internal ToolResult(ToolCall call,ToolResultStatus status,string reason,long version,long startedAt,JObject detail=null)
        {
            Status=status;Reason=reason;
            var data=new JObject{["call_id"]=call?.CallId,["tool_name"]=call?.ToolName,["status"]=status.ToString(),["reason"]=reason,["requested_state_version"]=call?.StateVersion??0,["execution_state_version"]=version,["started_at"]=startedAt,["completed_at"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),["completion_scope"]="dispatch_only"};
            if(detail!=null) data["detail"]=detail.DeepClone();json=data.ToString();
        }
        public JObject ToJson()=>JObject.Parse(json);
    }
    public interface IToolSafetyPolicy
    {bool Allows(ToolCall call,AgentStateSnapshot snapshot);}
    public sealed class AllowExistingBehaviorPolicy:IToolSafetyPolicy
    {public bool Allows(ToolCall call,AgentStateSnapshot snapshot)=>true;}
}
