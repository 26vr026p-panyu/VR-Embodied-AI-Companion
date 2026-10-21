using UnityEngine;
using CompanionAvatar;
/// <summary>Existing diagnostic intents on Quest; no new feature and no voice start/stop button.</summary>
public sealed class QuestDevelopmentInputAdapter : MonoBehaviour
{
    CompanionBehaviorOrchestrator orchestrator;
    CompanionFollowController follow;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
    {
        // Editor already has its existing keyboard adapters. No persistent Scene object added.
#if !UNITY_EDITOR
        if(Application.platform==RuntimePlatform.Android||Application.platform==RuntimePlatform.WindowsPlayer){var root=Object.FindAnyObjectByType<CompanionBehaviorOrchestrator>();if(root&&!root.GetComponent<QuestDevelopmentInputAdapter>())root.gameObject.AddComponent<QuestDevelopmentInputAdapter>();}
#endif
    }
    void Awake(){orchestrator=GetComponent<CompanionBehaviorOrchestrator>();follow=GetComponent<CompanionFollowController>();}
    void Update()
    {
        if(!AgentInputActions.DevelopmentAllowed)return;
        if(AgentInputActions.Pressed("GestureNod"))Submit(BehaviorGesture.Nod,BehaviorSpatialIntent.None);
        if(AgentInputActions.Pressed("GestureWave"))Submit(BehaviorGesture.Wave,BehaviorSpatialIntent.None);
        if(AgentInputActions.Pressed("FollowSide"))Submit(BehaviorGesture.None,BehaviorSpatialIntent.Side);
        if(AgentInputActions.Pressed("FollowLead"))Submit(BehaviorGesture.None,BehaviorSpatialIntent.Lead);
        if(AgentInputActions.Pressed("FollowTrail"))Submit(BehaviorGesture.None,BehaviorSpatialIntent.Trail);
        if(follow&&AgentInputActions.Pressed("NearbyStay"))follow.SetFollowMode(CompanionFollowMode.Stay);
        if(follow&&AgentInputActions.Pressed("NearbyApproach"))follow.SetFollowMode(CompanionFollowMode.Approach);
    }
    void Submit(BehaviorGesture gesture,BehaviorSpatialIntent spatial){if(orchestrator)orchestrator.Submit(new CompanionBehaviorRequest{source=BehaviorSource.Debug,gesture=gesture,spatialIntent=spatial});}
}
