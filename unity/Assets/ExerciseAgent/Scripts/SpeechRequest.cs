using System;

public enum SpeechPolicy { Queue, Drop, Interrupt }
public enum SpeechPriority { Ambient, Normal, Important, Safety }
public enum SpeechSource { Conversation, Proactive, Exercise, System, Tool }
public enum SpeechState { Idle, Preparing, Ready, Speaking, Interrupted, Completed, Failed }
public enum SpeechOutcome { Completed, Interrupted, Dropped, Failed, Cancelled }

/// <summary>Unity speech scheduling metadata, independent of backend SpeechRequest.</summary>
[Serializable] public sealed class SpeechRequest
{
    public string requestId, traceId, text, language = "ja", style = "neutral";
    public long turnId;
    public SpeechPriority priority = SpeechPriority.Normal;
    public SpeechPolicy policy = SpeechPolicy.Queue;
    public SpeechSource source = SpeechSource.Conversation;
    public bool isLastSpeech = true;
    public static SpeechRequest ForTurn(TurnContext turn, string text, string language,
        SpeechPolicy policy = SpeechPolicy.Queue, SpeechPriority priority = SpeechPriority.Normal,
        SpeechSource source = SpeechSource.Conversation, bool isLastSpeech = true)
    {
        return new SpeechRequest { requestId = Guid.NewGuid().ToString("N"), traceId = turn.TraceId,
            turnId = turn.TurnId, text = text, language = language, policy = policy,
            priority = priority, source = source, isLastSpeech = isLastSpeech };
    }
    internal SpeechRequest Snapshot() => (SpeechRequest)MemberwiseClone();
}

public sealed class SpeechResult
{
    public SpeechRequest Request { get; }
    public SpeechOutcome Outcome { get; }
    public string Reason { get; }
    public SpeechResult(SpeechRequest request, SpeechOutcome outcome, string reason)
    { Request = request; Outcome = outcome; Reason = reason; }
}
