using System.Collections.Generic;
using UnityEngine.Networking;

public enum TurnLifecycleState
{
    Created, Listening, ProcessingASR, WaitingSupplement, ProcessingLLM,
    ProcessingTTS, Speaking, Completed, Cancelled, Failed
}

/// <summary>Explicit identity and terminal state; owns only request lifetimes, never avatar motion.</summary>
public sealed class TurnContext
{
    public LatencyTrace.Turn Trace { get; internal set; }
    public string TraceId => Trace.Id;
    public long TurnId => Trace.Number;
    public long AgentTurnId => Trace.AgentTurn;
    public TurnLifecycleState State { get; internal set; } = TurnLifecycleState.Created;
    public bool BodyToolsDisabledForReliability { get; internal set; }
    public bool IsCancelled => State == TurnLifecycleState.Cancelled;
    public bool IsTerminal => State == TurnLifecycleState.Completed || State == TurnLifecycleState.Cancelled || State == TurnLifecycleState.Failed;
    public string CancellationReason { get; internal set; }
    public AsrLanguageMode LanguageMode { get; private set; } = AsrLanguageMode.Ja;
    public string LanguageCode => AsrLanguagePolicy.Code(LanguageMode);
    bool languageBound;
    internal void BindLanguageMode(AsrLanguageMode mode)
    {
        // Supplemental segments inherit original identity even if Inspector changes.
        if(languageBound)return;
        LanguageMode=mode;languageBound=true;
        LatencyTrace.Mark(Trace,"asr_language_selected",new Newtonsoft.Json.Linq.JObject{["language_mode"]=LanguageCode});
    }
    internal string BackendBaseUrl = BackendEndpoints.BaseUrl;
    internal readonly Dictionary<UnityWebRequest,string> Requests = new Dictionary<UnityWebRequest,string>();
}
