using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Owns speech admission and scheduling; TTSPlayer retains audio and envelope events.</summary>
[DisallowMultipleComponent]
public sealed class SpeechArbiter : MonoBehaviour
{
    [Range(1,5)] public int maxQueuedSpeechItems = 4;
    public SpeechState State { get; private set; } = SpeechState.Idle;
    public SpeechRequest ActiveRequest => active?.Request;
    public int QueueDepth => queue.Count;
    public event Action<SpeechRequest> SpeechPreparing, SpeechStarted, SpeechInterrupted, SpeechCompleted;
    public event Action<SpeechRequest,string> SpeechFailed;
    public event Action<SpeechResult> Result;
    sealed class Item { public SpeechRequest Request; public TurnContext Turn; }
    readonly List<Item> queue = new List<Item>();
    readonly HashSet<string> admitted = new HashSet<string>();
    readonly HashSet<string> unsafeHistory = new HashSet<string>();
    readonly HashSet<string> openSequences = new HashSet<string>();
    readonly HashSet<string> lastCompleted = new HashSet<string>();
    Item active;
    TTSPlayer player;
    TurnLifecycleManager lifecycle;
    bool bound, changing;

    public static SpeechArbiter For(TTSPlayer player)
    {
        if (!player) return null;
        var arbiter = player.GetComponent<SpeechArbiter>();
        if (!arbiter) arbiter = player.gameObject.AddComponent<SpeechArbiter>();
        arbiter.Bind(player);
        return arbiter;
    }
    void OnEnable() { Bind(GetComponent<TTSPlayer>()); }
    void Bind(TTSPlayer target)
    {
        if (bound || !target) return;
        player = target; lifecycle = TurnLifecycleManager.Instance;
        player.OnSpeechPreparing += Preparing;
        player.OnSpeechAudioReady += Ready;
        player.OnSpeechPlaybackStarted += Started;
        player.OnSpeechPlaybackFinished += Completed;
        player.OnSpeechInterrupted += Interrupted;
        player.OnSpeechFailed += Failed;
        lifecycle.OnTurnCancelled += Cancelled;
        bound = true;
    }
    void OnDisable()
    {
        if (!bound) return;
        if (active != null && !active.Turn.IsTerminal) lifecycle.Cancel(active.Turn,"speech_arbiter_disabled");
        player.OnSpeechPreparing -= Preparing; player.OnSpeechAudioReady -= Ready;
        player.OnSpeechPlaybackStarted -= Started; player.OnSpeechPlaybackFinished -= Completed;
        player.OnSpeechInterrupted -= Interrupted; player.OnSpeechFailed -= Failed;
        lifecycle.OnTurnCancelled -= Cancelled; bound = false;
        queue.Clear(); active = null;
    }
    void Mark(Item item,string evt,string reason = null)
    {
        var r = item.Request;
        LatencyTrace.Mark(item.Turn.Trace,evt,new JObject { ["speech_request_id"] = r.requestId,
            ["priority"] = r.priority.ToString(), ["policy"] = r.policy.ToString(),
            ["queue_depth"] = queue.Count, ["source"] = r.source.ToString(), ["reason"] = reason });
    }
    bool Matches(SpeechPlaybackEnvelope envelope) => active != null && player.ActiveSpeechRequestId == active.Request.requestId;
    public bool OpenTurnSpeechSequence(TurnContext c)
    {
        if(!lifecycle.Guard(c,"speech_sequence_open"))return false;
        openSequences.Add(c.TraceId);return true;
    }
    public void CloseTurnSpeechSequence(TurnContext c)
    {
        if(!lifecycle.Guard(c,"speech_sequence_close")||!openSequences.Remove(c.TraceId))return;
        lastCompleted.Add(c.TraceId);
        LatencyTrace.Mark(c.Trace,"speech_sequence_closed");
        StartCoroutine(AfterCompleted(new Item{Turn=c}));
    }
    public void AbandonTurnSpeechSequence(TurnContext c)
    {openSequences.Remove(c.TraceId);lastCompleted.Remove(c.TraceId);}
    public bool Submit(SpeechRequest request)
    {
        Bind(GetComponent<TTSPlayer>());
        if (!bound || !isActiveAndEnabled || request == null) return false;
        var r = request.Snapshot();
        if (string.IsNullOrEmpty(r.requestId)) r.requestId = Guid.NewGuid().ToString("N");
        var c = lifecycle.Current;
        if (c == null || c.TraceId != r.traceId || c.TurnId != r.turnId || !lifecycle.Guard(c,"speech_submit"))
        { Result?.Invoke(new SpeechResult(r,SpeechOutcome.Cancelled,"inactive_or_mismatched_turn")); return false; }
        var item = new Item { Request = r, Turn = c };
        Mark(item,"speech_request_submitted");
        if (string.IsNullOrWhiteSpace(r.text) || string.IsNullOrWhiteSpace(r.language))
        { Mark(item,"speech_request_failed","invalid_request"); Result?.Invoke(new SpeechResult(r,SpeechOutcome.Failed,"invalid_request")); return false; }
        if (admitted.Contains(r.requestId)) { Reject(item,"duplicate_request_id"); return false; }
        // IDs are used only within this active Turn; bounded retention avoids a session-long registry.
        if (active == null && queue.Count == 0) admitted.Clear();
        admitted.Add(r.requestId);
        bool userSpeaking=player.conversationState&&player.conversationState.IsUserSpeaking();
        if (active != null || changing || queue.Count > 0 || userSpeaking)
        {
            if (r.policy == SpeechPolicy.Drop) { Reject(item,"busy"); return false; }
            if (r.policy == SpeechPolicy.Interrupt)
            {
                if(userSpeaking){Reject(item,"user_has_floor");return false;}
                if (active != null && r.priority < active.Request.priority) { Reject(item,"lower_interrupt_priority"); return false; }
                changing = true;
                if (active != null) { player.StopOwnedSpeech(this,active.Request.requestId); }
                // An interrupt supersedes already queued lower/equal priority speech, preventing delayed stale speech.
                for (int i=queue.Count-1;i>=0;i--) if(queue[i].Request.priority <= r.priority)
                { var removed=queue[i];queue.RemoveAt(i);Reject(removed,"superseded_by_interrupt"); }
                changing = false;
                if (!lifecycle.Guard(c,"speech_interrupt_start")) return false;
                StartSpeech(item); return true;
            }
            if (queue.Count >= Mathf.Clamp(maxQueuedSpeechItems,1,5))
            { Reject(item,"queue_capacity"); return false; }
            queue.Add(item); Mark(item,"speech_request_queued"); return true;
        }
        StartSpeech(item); return true;
    }
    void Reject(Item item,string reason)
    { Mark(item,"speech_request_dropped",reason); Result?.Invoke(new SpeechResult(item.Request,SpeechOutcome.Dropped,reason)); }
    void StartSpeech(Item item)
    {
        if (!lifecycle.Guard(item.Turn,"speech_prepare")) return;
        active=item;State=SpeechState.Preparing;
        if(player.conversationState) player.conversationState.SetState(ConversationStateManager.ConversationState.PROCESSING);
        player.PlayOwnedSpeech(this,item.Request,item.Turn);
    }
    void Preparing(SpeechPlaybackEnvelope e)
    { if(!Matches(e))return;State=SpeechState.Preparing;Mark(active,"speech_prepare_started");SpeechPreparing?.Invoke(active.Request); }
    void Ready(SpeechPlaybackEnvelope e,AudioClip clip)
    { if(!Matches(e))return;State=SpeechState.Ready;Mark(active,"speech_ready"); }
    void Started(SpeechPlaybackEnvelope e)
    {
        if(!Matches(e))return;State=SpeechState.Speaking;Mark(active,"speech_playback_started");
        if(player.conversationState)player.conversationState.SetState(ConversationStateManager.ConversationState.AI_SPEAKING);
        SpeechStarted?.Invoke(active.Request);
    }
    void Completed(SpeechPlaybackEnvelope e)
    {
        if(!Matches(e))return;var done=active;active=null;State=SpeechState.Completed;
        if(player.conversationState&&player.conversationState.IsAISpeaking())
            player.conversationState.SetState(ConversationStateManager.ConversationState.PROCESSING);
        Mark(done,"speech_playback_completed");SpeechCompleted?.Invoke(done.Request);
        if(done.Request.isLastSpeech)lastCompleted.Add(done.Turn.TraceId);
        Result?.Invoke(new SpeechResult(done.Request,SpeechOutcome.Completed,"playback_completed"));
        // Defer next playback/Turn commit until all existing envelope subscribers have run.
        StartCoroutine(AfterCompleted(done));
    }
    System.Collections.IEnumerator AfterCompleted(Item done)
    {
        yield return null;
        if(!lifecycle.Guard(done.Turn,"speech_completion"))yield break;
        if(active!=null)yield break;
        if(queue.Count>0) { Drain(); yield break; }
        if(!openSequences.Contains(done.Turn.TraceId)&&lastCompleted.Contains(done.Turn.TraceId))
        {
            if(unsafeHistory.Contains(done.Turn.TraceId))lifecycle.Cancel(done.Turn,"speech_sequence_incomplete");
            else lifecycle.Complete(done.Turn);
            lastCompleted.Remove(done.Turn.TraceId);unsafeHistory.Remove(done.Turn.TraceId);
            RestoreListening(done.Turn);
        }
        else if(player.conversationState)player.conversationState.SetState(ConversationStateManager.ConversationState.PROCESSING);
    }
    void Interrupted(SpeechPlaybackEnvelope e)
    {
        if(!Matches(e))return;var stopped=active;active=null;State=SpeechState.Interrupted;
        if(player.conversationState&&player.conversationState.IsAISpeaking())
            player.conversationState.SetState(ConversationStateManager.ConversationState.PROCESSING);
        if(stopped.Request.source==SpeechSource.Conversation)unsafeHistory.Add(stopped.Turn.TraceId);
        Mark(stopped,"speech_playback_interrupted");SpeechInterrupted?.Invoke(stopped.Request);
        Result?.Invoke(new SpeechResult(stopped.Request,stopped.Turn.IsCancelled?SpeechOutcome.Cancelled:SpeechOutcome.Interrupted,
            stopped.Turn.IsCancelled?stopped.Turn.CancellationReason:"speech_preempted"));
    }
    void Failed(SpeechPlaybackEnvelope e,string reason)
    {
        if(!Matches(e))return;var failed=active;active=null;State=SpeechState.Failed;
        Mark(failed,"speech_request_failed",reason);Clear(failed.Turn,"speech_failed");
        SpeechFailed?.Invoke(failed.Request,reason);Result?.Invoke(new SpeechResult(failed.Request,SpeechOutcome.Failed,reason));
        lifecycle.Fail(failed.Turn,"tts_failed");RestoreListening(failed.Turn);
        lastCompleted.Remove(failed.Turn.TraceId);unsafeHistory.Remove(failed.Turn.TraceId);
    }
    void Cancelled(TurnContext c)
    {
        changing=true;Clear(c,"turn_cancelled");
        if(active!=null&&active.Turn==c)player.StopOwnedSpeech(this,active.Request.requestId);
        changing=false;
        openSequences.Remove(c.TraceId);lastCompleted.Remove(c.TraceId);unsafeHistory.Remove(c.TraceId);
        // New turn owns conversation state. Never overwrite USER_SPEAKING here.
    }
    void Clear(TurnContext c,string reason)
    {
        for(int i=queue.Count-1;i>=0;i--)if(queue[i].Turn==c)
        { var item=queue[i];queue.RemoveAt(i);Mark(item,"speech_queue_cleared",reason);Result?.Invoke(new SpeechResult(item.Request,SpeechOutcome.Cancelled,reason)); }
        LatencyTrace.Mark(c.Trace,"speech_queue_cleared",new JObject{["queue_depth"]=queue.Count,["reason"]=reason});
    }
    void Drain()
    {
        if(changing||active!=null||queue.Count==0)return;
        if(player.conversationState&&player.conversationState.IsUserSpeaking())return;
        // Priority selection, FIFO within equal priority.
        int best=0;for(int i=1;i<queue.Count;i++)if(queue[i].Request.priority>queue[best].Request.priority)best=i;
        var next=queue[best];queue.RemoveAt(best);
        if(lifecycle.Guard(next.Turn,"speech_queue_start"))StartSpeech(next);
    }
    void Update()
    {
        if(active==null&&queue.Count>0&&(!player.conversationState||
            (player.conversationState.currentState!=ConversationStateManager.ConversationState.USER_SPEAKING&&
             player.conversationState.currentState!=ConversationStateManager.ConversationState.WAITING_FOR_MORE)))Drain();
    }
    void RestoreListening(TurnContext c)
    { if(lifecycle.Current==c&&player.conversationState)player.conversationState.SetState(ConversationStateManager.ConversationState.LISTENING); }
}

