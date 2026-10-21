using UnityEngine;

namespace CompanionAvatar
{
    /// <summary>Controls visibility only; never disables voice, AI or motion components.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PresentationModeController : MonoBehaviour
    {
        [Tooltip("On: Presentation / normal research UI. Off: restore development UI.")]
        public bool presentationMode = true;
        public Canvas[] developmentCanvases;
        public Renderer[] developmentRenderers;
        public GameObject presentationHud;

        bool appliedMode;
        bool applied;

        void OnEnable() { Apply(); }
        void Update()
        {
            if (!applied || appliedMode != presentationMode) Apply();
        }

        void Apply()
        {
            // Only explicit development visuals: keep scripts and Button events active.
            if (developmentCanvases != null)
                foreach (var canvas in developmentCanvases)
                    if (canvas) canvas.enabled = !presentationMode;
            if (developmentRenderers != null)
                foreach (var renderer in developmentRenderers)
                    if (renderer) renderer.enabled = !presentationMode;
            if (presentationHud) presentationHud.SetActive(presentationMode);
            appliedMode = presentationMode;
            applied = true;
        }

        // Leaving Play/reloading must not resurrect the cached pre-Play debug UI.
        void OnDisable() { applied = false; }
    }
}
