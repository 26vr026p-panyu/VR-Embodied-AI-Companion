using System;
using UnityEngine;

namespace CompanionAvatar
{
    [Serializable] public sealed class SemanticExpressionBinding
    {
        public string semantic;
        public string avatarShape;
        public SemanticExpressionBinding(string semantic,string shape){this.semantic=semantic;avatarShape=shape;}
    }

    /// <summary>The sole AI behavior bridge. Never calls Motion/Face/Follow execution APIs directly.</summary>
    [DisallowMultipleComponent]
    public sealed class AgentBehaviorAdapter:MonoBehaviour
    {
        public PythonExerciseAgent agent;
        public TTSPlayer tts;
        public ConversationStateManager conversation;
        public CompanionBehaviorOrchestrator orchestrator;
        [Min(.1f)] public float behaviorTtl=20;
        public SemanticExpressionBinding[] expressionMapping={
            new SemanticExpressionBinding("happy","happy"),new SemanticExpressionBinding("concerned","sad"),
            new SemanticExpressionBinding("surprised","surprised"),new SemanticExpressionBinding("relaxed","relaxed"),
            new SemanticExpressionBinding("shy","hazukashiih")
        };
        public int SubmittedCount {get;private set;}
        public long PendingTurnId=>pending==null?0:pendingTurn;
        public event Action<long,string,BehaviorDecision> OnBehaviorSubmitted;
        AgentBehaviorProposal pending;
        long pendingTurn,lastReplyTurn,invalidThroughTurn;
        double receivedAt;
        float pendingTtl;

        void OnEnable()
        {
            TurnLifecycleManager.Instance.OnTurnCancelled+=TurnCancelled;
            if(agent!=null)agent.OnAgentReplyParsed+=Reply;
            if(conversation!=null)conversation.OnStateChanged+=State;
            if(tts!=null){tts.OnSpeechPlaybackStarted+=Started;tts.OnSpeechInterrupted+=Ended;tts.OnSpeechPlaybackFinished+=Ended;tts.OnSpeechFailed+=Failed;}
        }
        void OnDisable()
        {
            var manager=TurnLifecycleManager.Instance;if(manager!=null)manager.OnTurnCancelled-=TurnCancelled;
            if(agent!=null)agent.OnAgentReplyParsed-=Reply;
            if(conversation!=null)conversation.OnStateChanged-=State;
            if(tts!=null){tts.OnSpeechPlaybackStarted-=Started;tts.OnSpeechInterrupted-=Ended;tts.OnSpeechPlaybackFinished-=Ended;tts.OnSpeechFailed-=Failed;}
            pending=null;
        }
        void Update()
        {
            if(pending!=null&&(!TurnValid(pendingTurn)||Time.realtimeSinceStartupAsDouble-receivedAt>=pendingTtl))pending=null;
        }
        void TurnCancelled(TurnContext c)
        {
            if(c.AgentTurnId>0)invalidThroughTurn=Math.Max(invalidThroughTurn,c.AgentTurnId);
            if(pending!=null&&pendingTurn==c.AgentTurnId)pending=null;
        }
        bool TurnValid(long turn)=>TurnLifecycleManager.Instance.GuardAgent(turn,"behavior_turn_gate")&&agent!=null&&turn>0&&turn==agent.CurrentTurnId&&turn>invalidThroughTurn&&
            (conversation==null||!conversation.IsUserSpeaking());
        void State(ConversationStateManager.ConversationState previous,ConversationStateManager.ConversationState current)
        {
            if(current!=ConversationStateManager.ConversationState.USER_SPEAKING)return;
            invalidThroughTurn=Math.Max(invalidThroughTurn,agent==null?0:agent.CurrentTurnId);pending=null;
        }
        void Reply(AgentReplyEnvelope reply)
        {
            if(!TurnValid(reply.TurnId)||reply.TurnId<=lastReplyTurn)return;
            lastReplyTurn=reply.TurnId;pending=null;
            if(reply.NativeToolMode)
            {
                var legacy=AgentBehaviorContract.Validate(reply.Behavior);
                if(legacy.gesture!="none"||legacy.spatial_intent!="none"||(legacy.expression!="none"&&legacy.expression!="neutral"))
                    LatencyTrace.Mark(TurnLifecycleManager.Instance.Current?.Trace,"legacy_behavior_suppressed_in_tool_mode");
                return;
            }
            var b=AgentBehaviorContract.Validate(reply.Behavior);
            double now=Time.realtimeSinceStartupAsDouble;
            float ttl=float.IsNaN(behaviorTtl)||float.IsInfinity(behaviorTtl)?20:Mathf.Max(.1f,behaviorTtl);
            if(b.spatial_intent!="none")
            {
                var r=Request(reply.TurnId,now,ttl);
                r.spatialIntent=b.spatial_intent=="lead"?BehaviorSpatialIntent.Lead:b.spatial_intent=="side"?BehaviorSpatialIntent.Side:
                    b.spatial_intent=="trail"?BehaviorSpatialIntent.Trail:BehaviorSpatialIntent.Nearby;
                Submit(r,"reply-spatial");
            }
            if(b.gesture=="none"&&b.expression=="none")return;
            pending=b;pendingTurn=reply.TurnId;receivedAt=now;pendingTtl=ttl;
        }
        void Started(SpeechPlaybackEnvelope speech)
        {
            if(pending==null||speech.TurnId!=pendingTurn)return;
            var b=pending;pending=null; // one-shot even if a duplicate event arrives
            if(!TurnValid(speech.TurnId)||Time.realtimeSinceStartupAsDouble-receivedAt>=pendingTtl)return;
            var r=Request(speech.TurnId,receivedAt,pendingTtl);
            r.gesture=b.gesture=="nod"?BehaviorGesture.Nod:b.gesture=="wave"?BehaviorGesture.Wave:BehaviorGesture.None;
            if(b.expression=="neutral")r.expression="Neutral";
            else if(b.expression!="none"&&b.expression_intensity>0)r.expression=MapExpression(b.expression);
            r.intensity=b.expression_intensity;r.expressionHoldDuration=b.expression_hold;
            if(r.gesture!=BehaviorGesture.None||r.expression!=null)Submit(r,"playback-body");
        }
        string MapExpression(string semantic)
        {
            var face=orchestrator==null?null:orchestrator.expression;
            var mesh=face==null||face.faceRenderer==null?null:face.faceRenderer.sharedMesh;
            if(mesh==null)return null;
            foreach(var binding in expressionMapping??Array.Empty<SemanticExpressionBinding>())
            {
                if(binding==null||binding.semantic!=semantic)continue;
                string shape=binding.avatarShape;
                // Mapping cannot be used as a side door into mouth/blink controls.
                if(string.IsNullOrEmpty(shape)||AgentBehaviorContract.Allowed(shape,"aa","ih","ou","ee","oh","blink")||mesh.GetBlendShapeIndex(shape)<0)
                {Debug.LogWarning("[BehaviorAdapter] Unsupported expression mapping: "+semantic,this);return null;}
                return shape;
            }
            Debug.LogWarning("[BehaviorAdapter] Missing expression mapping: "+semantic,this);return null;
        }
        CompanionBehaviorRequest Request(long turn,double created,float ttl)=>new CompanionBehaviorRequest {
            turnId=turn,source=BehaviorSource.Agent,priority=BehaviorPriority.Normal,createdAt=created,ttl=ttl,
            expressionReleasePolicy=ExpressionReleasePolicy.ReturnToNeutral
        };
        void Submit(CompanionBehaviorRequest request,string stage)
        {
            if(orchestrator==null||!TurnLifecycleManager.Instance.GuardAgent(request.turnId,"behavior_submission"))return;
            var decision=orchestrator.Submit(request);SubmittedCount++;
            OnBehaviorSubmitted?.Invoke(request.turnId,stage,decision);
        }
        void Ended(SpeechPlaybackEnvelope speech){if(pending!=null&&speech.TurnId==pendingTurn)pending=null;}
        void Failed(SpeechPlaybackEnvelope speech,string reason){Ended(speech);}
    }
}
