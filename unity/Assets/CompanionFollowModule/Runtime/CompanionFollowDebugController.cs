using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
#endif
namespace CompanionAvatar
{
    [DisallowMultipleComponent, RequireComponent(typeof(CompanionFollowController))]
    public sealed class CompanionFollowDebugController : MonoBehaviour
    {
        public bool enableKeyboardDebug = true;
        public CompanionFollowController follow;
#if UNITY_EDITOR
        void Awake() { if (follow == null) follow = GetComponent<CompanionFollowController>(); }
        void Update()
        {
            if (!enableKeyboardDebug || follow == null || !follow.isActiveAndEnabled || !Application.isFocused) return;
            var window = UnityEditor.EditorWindow.focusedWindow;
            if (window == null || window.GetType().Name != "GameView") return;
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected != null && (selected.GetComponent<TMPro.TMP_InputField>() != null || selected.GetComponent<UnityEngine.UI.InputField>() != null)) return;
            if(!AgentInputActions.DevelopmentAllowed) return;
            if (AgentInputActions.Pressed("FollowSide")) follow.SetFollowMode(CompanionFollowMode.Side);
            if (AgentInputActions.Pressed("FollowLead")) follow.SetFollowMode(CompanionFollowMode.Lead);
            if (AgentInputActions.Pressed("FollowTrail")) follow.SetFollowMode(CompanionFollowMode.Trail);
            if (AgentInputActions.Pressed("NearbyStay")) follow.SetFollowMode(CompanionFollowMode.Stay);
            if (AgentInputActions.Pressed("NearbyApproach")) follow.SetFollowMode(CompanionFollowMode.Approach);
            if (AgentInputActions.Pressed("NearbyRoam")) follow.SetFollowMode(CompanionFollowMode.Roam);
        }
#endif
    }
}
