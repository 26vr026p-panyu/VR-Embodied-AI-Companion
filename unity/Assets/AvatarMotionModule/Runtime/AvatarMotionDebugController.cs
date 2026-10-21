using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
#endif

namespace CompanionAvatar
{
    [DisallowMultipleComponent, RequireComponent(typeof(AvatarMotionController))]
    public sealed class AvatarMotionDebugController : MonoBehaviour
    {
        public bool enableKeyboardDebug = true;
        public AvatarMotionController motion;
#if UNITY_EDITOR
        void Awake() { if(motion == null) motion = GetComponent<AvatarMotionController>(); }
        void Update()
        {
            if(!enableKeyboardDebug || motion == null || !motion.isActiveAndEnabled) return;
            var window = UnityEditor.EditorWindow.focusedWindow;
            if(!Application.isFocused || window == null || window.GetType().Name != "GameView") return;
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if(selected != null && (selected.GetComponent<TMPro.TMP_InputField>() != null || selected.GetComponent<UnityEngine.UI.InputField>() != null)) return;
            if(!AgentInputActions.DevelopmentAllowed) return;
            if(AgentInputActions.Pressed("MotionIdle")) motion.PlayMotion(AvatarMotion.Idle);
            if(AgentInputActions.Pressed("MotionWalk")) motion.PlayMotion(AvatarMotion.Walk);
            if(AgentInputActions.Pressed("MotionRun")) motion.PlayMotion(AvatarMotion.Run);
            if(AgentInputActions.Pressed("GestureNod")) motion.PlayMotion(AvatarMotion.Nod);
            if(AgentInputActions.Pressed("GestureWave")) motion.PlayMotion(AvatarMotion.Wave);
        }
#endif
    }
}
