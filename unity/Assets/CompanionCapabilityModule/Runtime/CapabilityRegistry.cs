using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CompanionCapabilities
{
    public sealed class CapabilityDescriptor
    {
        readonly string json;
        public string Name {get;}
        internal CapabilityDescriptor(JObject value){Name=(string)value["name"];json=value.ToString();}
        public JObject ToJson()=>JObject.Parse(json);
    }
    /// <summary>Read-only capability directory. No execution, timers or LLM ownership.</summary>
    public sealed class CapabilityRegistry
    {
        public static readonly string[] ExecutorNames={"gesture.nod","gesture.wave","expression.set","spatial.lead","spatial.side","spatial.trail","spatial.nearby"};
        readonly Dictionary<string,CapabilityDescriptor> tools=new Dictionary<string,CapabilityDescriptor>();
        readonly string manifest;
        public string ManifestHash {get;}
        public int RegistryVersion=>1;
        public double LastAvailabilityMs {get;private set;}
        public double LastValidationMs {get;private set;}
        public CapabilityRegistry():this(Resources.Load<TextAsset>("CompanionCapabilities/capability_manifest")?.bytes){}
        public CapabilityRegistry(byte[] bytes)
        {
            if(bytes==null)throw new InvalidOperationException("missing_manifest");
            using(var sha=SHA256.Create())ManifestHash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            var root=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            if((int?)root["registry_version"]!=1||!(root["tools"] is JArray list))throw new InvalidOperationException("invalid_manifest");
            foreach(JObject item in list)
            {
                string name=(string)item["name"];
                if(!ExecutorNames.Contains(name)||tools.ContainsKey(name)||(int?)item["version"]!=1||(string)item["priority_class"]!="normal"||(string)item["executor"]!="unity.orchestrator")throw new InvalidOperationException("executor_contract_mismatch");
                var schema=item["argument_schema"] as JObject;
                if(schema==null||(string)schema["type"]!="object"||(bool?)schema["additionalProperties"]!=false||!(schema["properties"] is JObject))throw new InvalidOperationException("invalid_schema");
                tools.Add(name,new CapabilityDescriptor(item));
            }
            if(tools.Count!=ExecutorNames.Length)throw new InvalidOperationException("executor_contract_mismatch");
            manifest=root.ToString();LatencyTrace.CaptureMark("capabilities","tool_registry_loaded",new JObject{["registry_version"]=1,["manifest_hash"]=ManifestHash});
        }
        public JObject Manifest(){var result=JObject.Parse(manifest);result["manifest_hash"]=ManifestHash;return result;}
        public bool Contains(string name)=>name!=null&&tools.ContainsKey(name);
        public JObject ValidateArguments(string name,JObject arguments)
        {
            var watch=Stopwatch.StartNew();
            try
            {
                if(!Contains(name))throw new ArgumentException("unknown_tool");
                if(arguments==null)throw new ArgumentException("invalid_arguments");
                var schema=(JObject)tools[name].ToJson()["argument_schema"];var properties=(JObject)schema["properties"];
                if(arguments.Properties().Any(p=>properties[p.Name]==null)||(schema["required"] as JArray??new JArray()).Any(key=>arguments[(string)key]==null))throw new ArgumentException("invalid_arguments");
                var result=new JObject();
                foreach(var item in properties.Properties())
                {
                    var spec=(JObject)item.Value;JToken value=arguments[item.Name]??spec["default"];
                    if(value==null)continue;
                    if((string)spec["type"]=="string")
                    {if(value.Type!=JTokenType.String||!((JArray)spec["enum"]).Any(v=>(string)v==(string)value))throw new ArgumentException("invalid_arguments");}
                    else
                    {if(value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Float)throw new ArgumentException("invalid_arguments");double n=(double)value;if(double.IsNaN(n)||double.IsInfinity(n)||n<(double)spec["minimum"]||n>(double)spec["maximum"])throw new ArgumentException("invalid_arguments");}
                    result[item.Name]=value.DeepClone();
                }
                if((string)result["expression"]=="neutral"){result["intensity"]=0;result["hold_seconds"]=0;}
                return result;
            }
            finally{LastValidationMs=watch.Elapsed.TotalMilliseconds;}
        }
        public JObject Availability(AgentStateSnapshot snapshot)
        {
            if(snapshot==null)throw new ArgumentNullException(nameof(snapshot));
            var watch=Stopwatch.StartNew();var s=snapshot.ToJsonObject();var result=new JObject();
            bool terminal=(bool?)s["turn"]?["cancelled"]==true||new[]{"completed","failed","cancelled"}.Contains((string)s["turn"]?["lifecycle"]);
            foreach(string name in tools.Keys)
            {
                string reason=null,basis="snapshot";bool goal=false;
                if(terminal)reason="invalid_turn";
                else if(name.StartsWith("gesture."))
                {if((bool?)s["user"]?["is_speaking"]==true)reason="user_speaking";else if(s["avatar"]?["gesture"]!=null&&(string)s["avatar"]["gesture"]!="none")reason="busy";}
                else if(name.StartsWith("spatial."))
                {string mode=name.Split('.')[1];goal=(string)s["avatar"]?["spatial_mode"]==mode;if((string)s["avatar"]?["spatial_intent"]==mode){reason="already_in_state";basis="preferred_intent";}}
                result[name]=new JObject{["available"]=reason==null,["reason"]=reason,["basis"]=basis,["effective_goal_reached"]=goal};
            }
            LastAvailabilityMs=watch.Elapsed.TotalMilliseconds;
            LatencyTrace.CaptureMark("capabilities","tool_availability_evaluated",new JObject{["state_version"]=snapshot.StateVersion,["availability_evaluation_ms"]=LastAvailabilityMs});
            return new JObject{["registry_version"]=1,["manifest_hash"]=ManifestHash,["state_version"]=snapshot.StateVersion,["tools"]=result,["availability_evaluation_ms"]=LastAvailabilityMs};
        }
        public JArray GetAvailableTools(AgentStateSnapshot snapshot)
        {var a=(JObject)Availability(snapshot)["tools"];return new JArray(tools.Where(t=>(bool)a[t.Key]["available"]).Select(t=>t.Value.ToJson()));}
    }
}
