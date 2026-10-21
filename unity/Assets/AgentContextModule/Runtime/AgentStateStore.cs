using System;
using System.Diagnostics;
using CompanionAvatar;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Immutable serialized value; callers receive a fresh JSON tree, never live mutable state.</summary>
public sealed class AgentStateSnapshot
{
    readonly string json;
    public string SnapshotId { get; }
    public long TurnId { get; }
    public long CreatedAtMs { get; }
    public long StateVersion { get; }
    internal AgentStateSnapshot(JObject value)
    {
        SnapshotId=(string)value["snapshot_id"]; TurnId=(long)value["turn_id"];
        CreatedAtMs=(long)value["created_at_ms"]; StateVersion=(long)value["state_version"];
        json=value.ToString(Newtonsoft.Json.Formatting.None);
    }
    public string ToJson()=>json;
    public JObject ToJsonObject()=>JObject.Parse(json);
}

/// <summary>Request-time observation adapter. Does not command the authoritative modules.</summary>
[DisallowMultipleComponent]
public sealed class AgentStateStore : MonoBehaviour
{
    public PythonExerciseAgent agent;
    public CompanionBehaviorOrchestrator behavior;
    public VoiceTranscriptionClient transcription;
    public bool developmentDebug;
    public AgentStateSnapshot LastSnapshot { get; private set; }
    public double LastCreationMs { get; private set; }
    public double LastSerializationMs { get; private set; }
    long version;
    public static AgentStateStore For(PythonExerciseAgent owner)
    {
        var store=owner.GetComponent<AgentStateStore>();
        if(store==null)store=owner.gameObject.AddComponent<AgentStateStore>();
        store.agent=owner; return store;
    }
    public AgentStateSnapshot CreateSnapshot(TurnContext turn,string language)
    {
        var timer=Stopwatch.StartNew();
        if(behavior==null)
            foreach(var candidate in UnityEngine.Object.FindObjectsByType<CompanionBehaviorOrchestrator>(FindObjectsSortMode.None))
                if(candidate.agent==agent){behavior=candidate;break;}
        if(transcription==null)
            foreach(var candidate in UnityEngine.Object.FindObjectsByType<VoiceTranscriptionClient>(FindObjectsSortMode.None))
                if(candidate.agent==agent){transcription=candidate;break;}
        var conversation=agent.conversationState;
        var tts=agent.ttsPlayer;
        var arbiter=tts==null?null:tts.GetComponent<SpeechArbiter>();
        var avatar=new JObject();
        if(behavior!=null)
        {
            avatar["gesture"]=behavior.ActiveGesture.ToString().ToLowerInvariant();
            if(behavior.expression!=null&&behavior.expression.IsReady)avatar["expression"]=behavior.expression.CurrentSemanticExpression;
            if(behavior.follow!=null)
            { avatar["spatial_mode"]=behavior.follow.EffectiveMode.ToString().ToLowerInvariant(); avatar["spatial_intent"]=behavior.follow.FollowMode.ToString().ToLowerInvariant(); }
            if(behavior.motion!=null)
            {
                float speed=behavior.follow==null?behavior.motion.TargetMoveSpeed:behavior.follow.VisualMoveSpeed;
                avatar["locomotion"]=speed<.05f?"idle":speed>=(behavior.motion.walkSpeed+behavior.motion.runSpeed)*.5f?"running":"walking";
            }
        }
        var speech=new JObject();
        if(arbiter!=null)
        {
            speech["state"]=arbiter.State.ToString().ToLowerInvariant();
            speech["is_speaking"]=arbiter.State==SpeechState.Speaking;
            speech["queue_depth"]=arbiter.QueueDepth;
            if(arbiter.ActiveRequest!=null)
            { speech["request_id"]=arbiter.ActiveRequest.requestId; speech["source"]=arbiter.ActiveRequest.source.ToString().ToLowerInvariant(); }
        }
        var user=new JObject(); if(conversation!=null)user["is_speaking"]=conversation.IsUserSpeaking();
        var environment=new JObject{["scene"]=SceneManager.GetActiveScene().name};
        var follow=behavior==null?null:behavior.follow;
        if(follow!=null)
        {
            user["activity"]=follow.PlayerMoving?"moving":"stationary";
            if(follow.playerPositionReference!=null)
            {
                var delta=follow.transform.position-follow.playerPositionReference.position; delta.y=0;
                environment["distance_m"]=delta.magnitude;
                if(follow.locomotionHeading!=null)
                {
                    var local=follow.locomotionHeading.InverseTransformDirection(delta);
                    environment["relative_side"]=Mathf.Abs(local.x)<.2f?"aligned":local.x<0?"left":"right";
                    environment["relative_forward"]=Mathf.Abs(local.z)<.2f?"aligned":local.z<0?"behind":"ahead";
                }
            }
        }
        var value=new JObject{
            ["version"]=1,["snapshot_id"]=Guid.NewGuid().ToString("N"),["created_at_ms"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["state_version"]=++version,["turn_id"]=turn.TurnId,["trace_id"]=turn.TraceId,
            ["conversation"]=new JObject{["mode"]=agent.mode==CompanionMode.General?"general":"exercise",["language"]=language,["session_id"]=agent.sessionId},
            ["turn"]=new JObject{["lifecycle"]=turn.State.ToString().ToLowerInvariant(),["cancelled"]=turn.IsCancelled},
            ["speech"]=speech,["avatar"]=avatar,["user"]=user,["environment"]=environment,
            ["system"]=new JObject{["language_mode"]=turn.LanguageCode}};
        LastCreationMs=timer.Elapsed.TotalMilliseconds; timer.Restart();
        LastSnapshot=new AgentStateSnapshot(value); LastSerializationMs=timer.Elapsed.TotalMilliseconds;
        LatencyTrace.Mark(turn.Trace,"state_snapshot_created",new JObject{["snapshot_version"]=1,["state_version"]=version,["snapshot_creation_ms"]=LastCreationMs,["serialization_ms"]=LastSerializationMs,["snapshot_chars"]=LastSnapshot.ToJson().Length});
        return LastSnapshot;
    }
    void OnGUI()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var presentation=UnityEngine.Object.FindAnyObjectByType<PresentationModeController>();
        if(developmentDebug&&LastSnapshot!=null&&(presentation==null||!presentation.presentationMode))GUI.Label(new Rect(12,110,650,250),LastSnapshot.ToJson());
#endif
    }
}
