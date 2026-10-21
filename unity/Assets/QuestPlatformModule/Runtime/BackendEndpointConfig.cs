using System;
using UnityEngine;

/// <summary>Platform endpoints only. No keys, retries or conversation policy.</summary>
[Serializable] public sealed class BackendEndpointProfile
{
    public string scheme="http", host="", websocketScheme="ws";
    public int port=8000;
    public string asrWebsocketPath="/asr/stream", ttsWebsocketPath="/tts/stream";
}
[Serializable] public sealed class BackendEndpointConfig
{
    public BackendEndpointProfile WindowsLocal=new BackendEndpointProfile{host="127.0.0.1"};
    public BackendEndpointProfile QuestLan=new BackendEndpointProfile();
}
public static class BackendEndpoints
{
    // Serialized legacy URL fields remain for fixtures and rollback; Android never uses them.
    public const string LocalBase="http://127.0.0.1:8000";
    public const string LocalAgent=LocalBase+"/agent", LocalTts=LocalBase+"/tts", LocalTranscribe=LocalBase+"/transcribe", LocalLipSync=LocalBase+"/lipsync/plan";
    public const string LocalAsrWs="ws://127.0.0.1:8000/asr/stream";
    static BackendEndpointConfig config;
    public static BackendEndpointConfig Config
    {
        get { if(config==null){var asset=Resources.Load<TextAsset>("BackendEndpointConfig");config=asset?JsonUtility.FromJson<BackendEndpointConfig>(asset.text):new BackendEndpointConfig();}return config; }
    }
    public static bool IsQuestLan => Application.platform==RuntimePlatform.Android;
    public static string ProfileName => IsQuestLan?"QuestLan":"WindowsLocal";
    public static BackendEndpointProfile Active => IsQuestLan?Config.QuestLan:Config.WindowsLocal;
    public static string BaseUrl => IsConfigured ? Build(Active,false,"") : "";
    static bool warned;
    public static bool IsConfigured
    {
        get {try {Build(Active,false,"");Build(Active,true,Active.asrWebsocketPath);Build(Active,true,Active.ttsWebsocketPath);return true;}catch(Exception){return false;}}
    }
    public static bool RequireConfigured()
    {
        if(IsConfigured)return true;
        if(!warned){warned=true;Debug.LogWarning("[Backend] QuestLan host is not configured; voice/agent network input unavailable.");ReliabilityTrace.SetHealth(null,"Backend",ComponentHealth.Unavailable);}
        return false;
    }
    public static string Resolve(string legacyUrl,string path,bool websocket=false)
    {
        // Explicit local fixture/custom routes continue to work in Windows tests.
        if(!IsQuestLan&&!string.IsNullOrEmpty(legacyUrl)&&Uri.TryCreate(legacyUrl,UriKind.Absolute,out var old))
        {
            bool standard=old.Host=="127.0.0.1"&&old.Port==8000 && (old.Scheme=="http"||old.Scheme=="ws");
            if(!standard)return legacyUrl;
        }
        return Build(Active,websocket,path);
    }
    public static string Websocket(string legacyUrl,string path,string httpUrl)
    {
        if(!IsQuestLan&&!string.IsNullOrEmpty(legacyUrl))return Resolve(legacyUrl,path,true);
        if(!IsQuestLan&&Uri.TryCreate(httpUrl,UriKind.Absolute,out var old)&&(old.Host!="127.0.0.1"||old.Port!=8000))
            return (old.Scheme=="https"?"wss":"ws")+"://"+old.Authority+path;
        return Build(Active,true,path);
    }
    public static string Build(BackendEndpointProfile p,bool websocket,string path)
    {
        if(p==null||string.IsNullOrWhiteSpace(p.host)||p.port<1||p.port>65535)
            throw new InvalidOperationException("Backend endpoint profile is not configured.");
        if(IsQuestLan&&(p.host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||p.host=="127.0.0.1"||p.host=="::1"))
            throw new InvalidOperationException("QuestLan cannot use localhost.");
        string scheme=websocket?p.websocketScheme:p.scheme;
        if(websocket?(scheme!="ws"&&scheme!="wss"):(scheme!="http"&&scheme!="https"))throw new InvalidOperationException("Unsupported backend scheme.");
        var b=new UriBuilder(scheme,p.host,p.port,path);return b.Uri.AbsoluteUri.TrimEnd('/');
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){config=null;warned=false;}
}
