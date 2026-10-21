using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable]
public sealed class AgentBehaviorProposal
{
    public string gesture = "none", expression = "none", spatial_intent = "none";
    public float expression_intensity, expression_hold;
}

public enum CompanionMode { Exercise, General }

/// <summary>Strict semantic contract; malformed behavior does not discard valid speech.</summary>
public static class AgentBehaviorContract
{
    public static bool Allowed(string value, params string[] options) => Array.IndexOf(options, value) >= 0;
    public static AgentBehaviorProposal Validate(AgentBehaviorProposal b)
    {
        if (b == null || !Allowed(b.gesture,"none","nod","wave") ||
            !Allowed(b.expression,"none","neutral","happy","concerned","surprised","relaxed","shy") ||
            !Allowed(b.spatial_intent,"none","lead","side","trail","nearby") ||
            float.IsNaN(b.expression_intensity) || float.IsInfinity(b.expression_intensity) ||
            float.IsNaN(b.expression_hold) || float.IsInfinity(b.expression_hold)) return new AgentBehaviorProposal();
        var result = new AgentBehaviorProposal {
            gesture=b.gesture, expression=b.expression, spatial_intent=b.spatial_intent,
            expression_intensity=Mathf.Clamp01(b.expression_intensity), expression_hold=Mathf.Clamp(b.expression_hold,0,10)
        };
        if(result.expression=="none"||result.expression=="neutral") result.expression_intensity=result.expression_hold=0;
        return result;
    }
    public static AgentResponse Parse(string json)
    {
        JObject root;
        using(var reader=new JsonTextReader(new StringReader(json)){MaxDepth=16,DateParseHandling=DateParseHandling.None})
        {
            root=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            if(reader.Read())throw new JsonException("Trailing response content");
        }
        if(root["speech"]?.Type!=JTokenType.String)throw new JsonException("speech must be a string");
        string speech=root.Value<string>("speech");
        if(string.IsNullOrWhiteSpace(speech))throw new JsonException("speech is empty");
        string action=root["action"]==null?"CHAT":root["action"].Type==JTokenType.String?root.Value<string>("action"):null;
        bool validAction=Allowed(action,"CHAT","KEEP","ENCOURAGE","REST");
        var result=new AgentResponse{speech=speech,action=validAction?action:"CHAT",behavior=new AgentBehaviorProposal()};
        if(!validAction||!(root["behavior"] is JObject behavior))return result;
        try
        {
            result.behavior=Validate(new AgentBehaviorProposal {
                gesture=ReadString(behavior,"gesture"),expression=ReadString(behavior,"expression"),spatial_intent=ReadString(behavior,"spatial_intent"),
                expression_intensity=ReadNumber(behavior,"expression_intensity",0,0,1),expression_hold=ReadNumber(behavior,"expression_hold",3,0,10)
            });
        }
        catch(JsonException){result.behavior=new AgentBehaviorProposal();}
        return result;
    }
    static string ReadString(JObject b,string key)
    {
        if(b[key]==null)return "none";
        if(b[key].Type!=JTokenType.String)throw new JsonException("Invalid behavior enum type");
        return b.Value<string>(key);
    }
    static float ReadNumber(JObject b,string key,double fallback,double min,double max)
    {
        if(b[key]==null)return (float)fallback;
        if(b[key].Type!=JTokenType.Integer&&b[key].Type!=JTokenType.Float)throw new JsonException("Invalid behavior numeric type");
        double n=b.Value<double>(key);
        if(double.IsNaN(n)||double.IsInfinity(n))throw new JsonException("Nonfinite behavior number");
        return (float)Math.Max(min,Math.Min(max,n));
    }
}
