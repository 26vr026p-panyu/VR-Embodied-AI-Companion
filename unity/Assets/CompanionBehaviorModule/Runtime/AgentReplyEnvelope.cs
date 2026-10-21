/// <summary>Unity-local lifecycle metadata. Never serialized into the backend request.</summary>
public sealed class AgentReplyEnvelope
{
    public long TurnId { get; }
    public string Speech { get; }
    public string Action { get; }
    public AgentBehaviorProposal Behavior { get; }
    public bool NativeToolMode { get; }
    public AgentReplyEnvelope(long turnId, string speech, string action, AgentBehaviorProposal behavior = null, bool nativeToolMode = false)
    { NativeToolMode = nativeToolMode; TurnId = turnId; Speech = speech; Action = action; Behavior = AgentBehaviorContract.Validate(behavior); }
}

public sealed class SpeechPlaybackEnvelope
{
    public long TurnId { get; }
    public long PlaybackId { get; }
    public string Text { get; }
    public string Language { get; }
    public SpeechPlaybackEnvelope(long turnId, long playbackId, string text, string language)
    { TurnId = turnId; PlaybackId = playbackId; Text = text; Language = language; }
}
