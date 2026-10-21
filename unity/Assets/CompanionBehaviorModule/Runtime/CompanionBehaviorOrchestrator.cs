using System;
using System.Collections.Generic;
using UnityEngine;

namespace CompanionAvatar
{
    /// <summary>Authorizes discrete intents. Does not own locomotion, root pose, gaze bones or facial weights.</summary>
    [DisallowMultipleComponent]
    public sealed class CompanionBehaviorOrchestrator : MonoBehaviour
    {
        public AvatarMotionController motion;
        public FaceExpressionController expression;
        public CompanionFollowController follow;
        public ConversationStateManager conversation;
        public PythonExerciseAgent agent;
        public TTSPlayer tts;
        [Min(0)] public float nodCooldown = 2.5f;
        [Min(0)] public float waveCooldown = 8f;
        [Min(0)] public float spatialIntentCooldown = 4f;
        public bool logDecisions;
        public int GestureDispatchCount { get; private set; }
        public int ExpressionDispatchCount { get; private set; }
        public int SpatialDispatchCount { get; private set; }
        public BehaviorGesture ActiveGesture { get; private set; }
        public bool IsSpeechPlaying { get; private set; }
        public AgentReplyEnvelope LastReply { get; private set; }
        public long LatestTurnId => agent == null ? 0 : agent.CurrentTurnId;
        public event Action<CompanionBehaviorRequest, BehaviorDecision> OnDecision;
        readonly Dictionary<string, double> consumed = new Dictionary<string, double>();
        readonly List<string> expired = new List<string>();
        double nextNod, nextWave, nextSpatial, gestureStarted, expressionReleaseAt;
        long invalidThroughTurn;
        bool expressionOwned;
        BehaviorPriority expressionPriority;
        ExpressionReleasePolicy releasePolicy;

        void OnEnable()
        {
            if (conversation != null) conversation.OnStateChanged += StateChanged;
            if (agent != null) agent.OnAgentReplyParsed += ReplyParsed;
            if (tts != null)
            {
                tts.OnSpeechPlaybackStarted += SpeechStarted;
                tts.OnSpeechPlaybackFinished += SpeechEnded;
                tts.OnSpeechInterrupted += SpeechEnded;
                tts.OnSpeechFailed += SpeechFailed;
                IsSpeechPlaying = tts.isSpeaking;
            }
        }
        void OnDisable()
        {
            if (conversation != null) conversation.OnStateChanged -= StateChanged;
            if (agent != null) agent.OnAgentReplyParsed -= ReplyParsed;
            if (tts != null)
            {
                tts.OnSpeechPlaybackStarted -= SpeechStarted;
                tts.OnSpeechPlaybackFinished -= SpeechEnded;
                tts.OnSpeechInterrupted -= SpeechEnded;
                tts.OnSpeechFailed -= SpeechFailed;
            }
            if (expressionOwned && releasePolicy == ExpressionReleasePolicy.ReturnToNeutral && expression != null)
                expression.SetNeutral();
            expressionOwned = false;
            IsSpeechPlaying = false;
        }
        void StateChanged(ConversationStateManager.ConversationState previous, ConversationStateManager.ConversationState current)
        {
            if (current == ConversationStateManager.ConversationState.USER_SPEAKING)
                invalidThroughTurn = Math.Max(invalidThroughTurn, LatestTurnId);
            // No gesture queue and no forced interruption of an already playing gesture.
        }
        void ReplyParsed(AgentReplyEnvelope reply)
        {
            if (TurnLifecycleManager.Instance.GuardAgent(reply.TurnId,"orchestrator_reply") && reply.TurnId == LatestTurnId && reply.TurnId > invalidThroughTurn) LastReply = reply;
            // Legacy CHAT/KEEP/ENCOURAGE/REST deliberately dispatch NO avatar requests.
        }
        void SpeechStarted(SpeechPlaybackEnvelope _) { IsSpeechPlaying = true; }
        void SpeechEnded(SpeechPlaybackEnvelope _) { IsSpeechPlaying = false; }
        void SpeechFailed(SpeechPlaybackEnvelope _, string reason) { IsSpeechPlaying = false; }
        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (ActiveGesture != BehaviorGesture.None && now - gestureStarted > .3 && !AnimatorGestureBusy())
                ActiveGesture = BehaviorGesture.None;
            if (expressionOwned && releasePolicy == ExpressionReleasePolicy.ReturnToNeutral && now >= expressionReleaseAt)
            {
                if (expression != null) expression.SetNeutral();
                expressionOwned = false;
            }
            expired.Clear();
            foreach (var item in consumed) if (item.Value <= now) expired.Add(item.Key);
            foreach (string key in expired) consumed.Remove(key);
        }
        bool AnimatorGestureBusy()
        {
            var a = motion == null ? null : motion.Animator;
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) return false;
            foreach (string name in new[] { "Upper Body Wave", "Head Nod" })
            {
                int layer = a.GetLayerIndex(name);
                if (layer < 0) continue;
                if (a.GetLayerWeight(layer) > .001f) return true;
                string state = name == "Head Nod" ? "Nod" : "Wave";
                if (a.GetCurrentAnimatorStateInfo(layer).IsName(state) ||
                    (a.IsInTransition(layer) && a.GetNextAnimatorStateInfo(layer).IsName(state))) return true;
            }
            return false;
        }
        public BehaviorSpatialIntent CurrentSpatialIntent => follow == null ? BehaviorSpatialIntent.None :
            follow.ForceSocial ? BehaviorSpatialIntent.Nearby :
            follow.FollowMode == CompanionFollowMode.Lead ? BehaviorSpatialIntent.Lead :
            follow.FollowMode == CompanionFollowMode.Trail ? BehaviorSpatialIntent.Trail : BehaviorSpatialIntent.Side;

        public BehaviorDecision Submit(CompanionBehaviorRequest r)
        {
            var d = new BehaviorDecision();
            double now = Time.realtimeSinceStartupAsDouble;
            if (!isActiveAndEnabled) d.request = "Disabled";
            else if (r == null) d.request = "Invalid";
            else if (r.source == BehaviorSource.Agent && !TurnLifecycleManager.Instance.GuardAgent(r.turnId,"orchestrator_submission")) d.request = "StaleTurn";
            else if (double.IsNaN(r.createdAt) || double.IsInfinity(r.createdAt) || float.IsNaN(r.ttl) || float.IsInfinity(r.ttl) || r.createdAt > now || r.ttl <= 0 || now - r.createdAt >= r.ttl) d.request = "Expired";
            else if (r.turnId < 0 || (r.turnId > 0 && (r.turnId != LatestTurnId || r.turnId <= invalidThroughTurn))) d.request = "StaleTurn";
            else if (!Enum.IsDefined(typeof(BehaviorGesture), r.gesture) || !Enum.IsDefined(typeof(BehaviorSpatialIntent), r.spatialIntent) || !Enum.IsDefined(typeof(BehaviorPriority), r.priority)) d.request = "Invalid";
            else if (!string.IsNullOrEmpty(r.requestId) && consumed.ContainsKey(r.requestId)) d.request = "Duplicate";
            if (d.request != "Valid") return Publish(r, d);

            if (r.gesture != BehaviorGesture.None)
            {
                if (conversation != null && conversation.IsUserSpeaking()) d.gesture = "UserSpeaking";
                else if (ActiveGesture != BehaviorGesture.None || AnimatorGestureBusy()) d.gesture = "Busy";
                else if (now < (r.gesture == BehaviorGesture.Nod ? nextNod : nextWave)) d.gesture = "Cooldown";
                else if (motion == null || !motion.PlayMotion(r.gesture == BehaviorGesture.Nod ? AvatarMotion.Nod : AvatarMotion.Wave)) d.gesture = "Unavailable";
                else
                {
                    ActiveGesture = r.gesture; gestureStarted = now; GestureDispatchCount++;
                    if (r.gesture == BehaviorGesture.Nod) nextNod = now + nodCooldown; else nextWave = now + waveCooldown;
                    d.gesture = "Accepted";
                }
            }
            if (!string.IsNullOrWhiteSpace(r.expression) && !string.Equals(r.expression, "None", StringComparison.OrdinalIgnoreCase))
            {
                bool neutral = string.Equals(r.expression, "Neutral", StringComparison.OrdinalIgnoreCase);
                bool valid = expression != null && expression.isActiveAndEnabled && expression.faceRenderer != null && expression.faceRenderer.sharedMesh != null;
                int shape = valid ? expression.faceRenderer.sharedMesh.GetBlendShapeIndex(r.expression) : -1;
                if (!valid) d.expression = "Unavailable";
                else if ((!neutral && (shape < 0 || r.expression == "blink" || r.expression == "aa" || r.expression == "ih" || r.expression == "ou" || r.expression == "ee" || r.expression == "oh")) || float.IsNaN(r.intensity) || float.IsInfinity(r.intensity) || float.IsNaN(r.expressionHoldDuration) || float.IsInfinity(r.expressionHoldDuration) || r.expressionHoldDuration < 0 || !Enum.IsDefined(typeof(ExpressionReleasePolicy), r.expressionReleasePolicy))
                { d.expression = "Invalid"; Debug.LogWarning("[Behavior] Invalid expression request: " + r.expression, this); }
                else if (expressionOwned && r.priority < expressionPriority) d.expression = "LowerPriority";
                else
                {
                    if (neutral) expression.SetNeutral(); else expression.SetExpression(r.expression, Mathf.Clamp01(r.intensity) * 100);
                    expressionOwned = !neutral; expressionPriority = r.priority; releasePolicy = r.expressionReleasePolicy;
                    // Include the controller's transition before the requested hold window.
                    expressionReleaseAt = now + Mathf.Max(0, expression.transitionDuration) + r.expressionHoldDuration;
                    ExpressionDispatchCount++; d.expression = "Accepted";
                }
            }
            if (r.spatialIntent != BehaviorSpatialIntent.None)
            {
                if (follow == null || !follow.isActiveAndEnabled) d.spatial = "Unavailable";
                else if (r.spatialIntent == CurrentSpatialIntent) d.spatial = "Unchanged";
                else if (now < nextSpatial && !(r.bypassSpatialCooldown && r.priority >= BehaviorPriority.Important)) d.spatial = "Cooldown";
                else
                {
                    var intent = r.spatialIntent == BehaviorSpatialIntent.Lead ? CompanionFollowIntent.Lead :
                        r.spatialIntent == BehaviorSpatialIntent.Trail ? CompanionFollowIntent.Trail :
                        r.spatialIntent == BehaviorSpatialIntent.Nearby ? CompanionFollowIntent.Nearby : CompanionFollowIntent.Side;
                    follow.SetFollowIntent(intent); nextSpatial = now + spatialIntentCooldown;
                    SpatialDispatchCount++; d.spatial = "Accepted";
                }
            }
            if (d.AnyAccepted && !string.IsNullOrEmpty(r.requestId)) consumed[r.requestId] = r.createdAt + r.ttl;
            return Publish(r, d);
        }
        BehaviorDecision Publish(CompanionBehaviorRequest r, BehaviorDecision d)
        {
            if (logDecisions) Debug.Log("[Behavior] " + d, this);
            OnDecision?.Invoke(r, d); return d;
        }
    }
}
