using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
#endif

namespace WindsongIntegration
{
    /// <summary>Editor-only input adapter. Never changes tracking or poses in a player build.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class DesktopXRDebugController : MonoBehaviour
    {
        [Header("Editor only — turn off before testing Quest Link")]
        public bool enableDesktopDebug = true;
        [Header("Existing rig and existing camera — no replacement camera")]
        public Transform rigRoot;
        public Camera vrCamera;
        public OVRCameraRig ovrCameraRig;
        [Header("Desktop movement")]
        [Min(.1f)] public float desktopEyeHeight = 1.65f;
        [Min(0)] public float moveSpeed = 2.5f;
        [Min(0)] public float fastMoveSpeed = 6f;
        [Min(0)] public float mouseSensitivity = .12f;
        [Range(1,89)] public float pitchLimit = 80f;
        [Header("Temporarily paused in desktop mode; original enabled states restored")]
        [Tooltip("Existing HMD speed gain, headset emulator and keyboard debug controllers only. Never pause the production FaceExpressionController or AI/voice components.")]
        public Behaviour[] pauseWhileDebug;
        public bool IsDesktopActive
        {
            get
            {
#if UNITY_EDITOR
                return active;
#else
                return false;
#endif
            }
        }

#if UNITY_EDITOR
        bool started, active, cursorCaptured;
        bool[] originalEnabled;
        Behaviour[] pausedComponents;
        Vector3 savedRootPosition, savedHeadPosition;
        Quaternion savedRootRotation, savedHeadRotation;
        Transform head;
        float yaw, pitch;
        StereoTargetEyeMask savedStereo;
        UniversalAdditionalCameraData cameraData;
        bool savedAllowXR;
        CursorLockMode savedCursorLock;
        bool savedCursorVisible;

        // Starts after the original scripts, so their original initialization is preserved.
        void Start() { started = true; SyncMode(); }
        void Update()
        {
            if(!started) return;
            SyncMode();
            if(!active) return;
            if(!GameViewHasFocus()) { ReleaseCursor(); return; }
            bool typing=IsTypingInUI();
            if(AgentInputActions.Pressed("ReleaseDesktopCursor")) ReleaseCursor();
            bool look=AgentInputActions.Held("DesktopLookHeld") && !typing;
            if(look && !cursorCaptured) { Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;cursorCaptured=true; }
            if(!look) ReleaseCursor();
            Vector2 move=typing?Vector2.zero:AgentInputActions.Vector("DesktopMove");
            bool fast=AgentInputActions.Held("DesktopFast");
            ApplyInput(move,look?AgentInputActions.Vector("DesktopLook"):Vector2.zero,fast,Mathf.Min(Time.unscaledDeltaTime,.1f));
        }
        void SyncMode()
        {
            if(enableDesktopDebug && !active) EnterDesktop();
            else if(!enableDesktopDebug && active) ExitDesktop();
        }
        void EnterDesktop()
        {
            if(rigRoot==null || vrCamera==null || ovrCameraRig==null || !vrCamera.transform.IsChildOf(rigRoot))
            {
                Debug.LogError("Desktop debug requires the existing rig, its OVRCameraRig and its existing child camera.",this);
                enableDesktopDebug=false;return;
            }
            head=vrCamera.transform;
            savedRootPosition=rigRoot.position;savedRootRotation=rigRoot.rotation;
            savedHeadPosition=head.localPosition;savedHeadRotation=head.localRotation;
            savedStereo=vrCamera.stereoTargetEye;
            cameraData=vrCamera.GetComponent<UniversalAdditionalCameraData>();
            if(cameraData!=null) { savedAllowXR=cameraData.allowXRRendering;cameraData.allowXRRendering=false; }
            vrCamera.stereoTargetEye=StereoTargetEyeMask.None;
            savedCursorLock=Cursor.lockState;savedCursorVisible=Cursor.visible;
            pausedComponents=pauseWhileDebug==null?new Behaviour[0]:(Behaviour[])pauseWhileDebug.Clone();
            originalEnabled=new bool[pausedComponents.Length];
            for(int i=0;i<pausedComponents.Length;i++)
            {
                var b=pausedComponents[i]; if(b==null || b==this)continue;
                // Protect production expressions even if an old scene still lists them.
                if(b is FaceExpressionController) { pausedComponents[i]=null;continue; }
                originalEnabled[i]=b.enabled;b.enabled=false;
            }
            yaw=rigRoot.eulerAngles.y;pitch=0;active=true;
            // OVRCameraRig keeps its native hierarchy and continues tracking. Apply our
            // editor-only view AFTER each anchor update, including its before-render update.
            ovrCameraRig.UpdatedAnchors+=AfterTrackedAnchors;
            Application.onBeforeRender+=BeforeRender;
            ApplyPose();
        }
        void ApplyInput(Vector2 move,Vector2 look,bool fast,float seconds)
        {
            if(!active)return;
            yaw+=look.x*mouseSensitivity;
            pitch=Mathf.Clamp(pitch-look.y*mouseSensitivity,-pitchLimit,pitchLimit);
            rigRoot.rotation=Quaternion.Euler(0,yaw,0);
            Vector2 normalized=Vector2.ClampMagnitude(move,1);
            rigRoot.position+=(rigRoot.right*normalized.x+rigRoot.forward*normalized.y)*(fast?fastMoveSpeed:moveSpeed)*seconds;
        }
        void LateUpdate() { if(active)ApplyPose(); }
        void AfterTrackedAnchors(OVRCameraRig _) { if(active)ApplyPose(); }
        [BeforeRenderOrder(10000)]
        void BeforeRender() { if(active)ApplyPose(); }
        void ApplyPose()
        {
            if(head==null || rigRoot==null)return;
            // Runtime-only world pose accounts for the unchanged TrackingSpace transform.
            head.SetPositionAndRotation(rigRoot.TransformPoint(Vector3.up*desktopEyeHeight),rigRoot.rotation*Quaternion.Euler(pitch,0,0));
        }
        void ExitDesktop()
        {
            if(!active)return;
            active=false;
            if(ovrCameraRig!=null)ovrCameraRig.UpdatedAnchors-=AfterTrackedAnchors;
            Application.onBeforeRender-=BeforeRender;
            if(rigRoot!=null)rigRoot.SetPositionAndRotation(savedRootPosition,savedRootRotation);
            if(head!=null) { head.localPosition=savedHeadPosition;head.localRotation=savedHeadRotation; }
            if(vrCamera!=null)vrCamera.stereoTargetEye=savedStereo;
            if(cameraData!=null)cameraData.allowXRRendering=savedAllowXR;
            for(int i=0;i<pausedComponents.Length;i++)
                if(pausedComponents[i]!=null && pausedComponents[i]!=this)pausedComponents[i].enabled=originalEnabled[i];
            ReleaseCursor();
        }
        void ReleaseCursor()
        {
            if(!cursorCaptured)return;
            Cursor.lockState=savedCursorLock;Cursor.visible=savedCursorVisible;cursorCaptured=false;
        }
        static bool GameViewHasFocus()
        {
            var window=UnityEditor.EditorWindow.focusedWindow;
            return Application.isFocused && window!=null && window.GetType().Name=="GameView";
        }
        static bool IsTypingInUI()
        {
            var selected=EventSystem.current==null?null:EventSystem.current.currentSelectedGameObject;
            return selected!=null && (selected.GetComponent<TMPro.TMP_InputField>()!=null || selected.GetComponent<UnityEngine.UI.InputField>()!=null);
        }
        void OnApplicationFocus(bool focused) { if(!focused)ReleaseCursor(); }
        void OnDisable() { ExitDesktop(); }
        void OnDestroy() { ExitDesktop(); }
#endif
    }
}
