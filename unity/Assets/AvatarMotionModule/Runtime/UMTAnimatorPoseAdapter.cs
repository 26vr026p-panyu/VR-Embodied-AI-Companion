using UnityEngine;
using UMT;

namespace CompanionAvatar
{
    /// <summary>Tell UMT which bones were authored by Animator, including constant poses.</summary>
    [DefaultExecutionOrder(9990), DisallowMultipleComponent]
    public sealed class UMTAnimatorPoseAdapter : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("Only directly keyed FK bones. Do not include secondary physics/constraint bones.")]
        public MMDBoneTransform[] animatedBones;
        void LateUpdate()
        {
            if(animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null || animatedBones == null) return;
            // UMT's order-10000 solver otherwise treats a constant pose equal to last
            // frame's solved pose as feedback and resets it to bind pose every other frame.
            // This public flag clears that cache, without changing bind transforms or physics.
            foreach(var bone in animatedBones) if(bone != null) bone.solverResetPending = true;
        }
    }
}
