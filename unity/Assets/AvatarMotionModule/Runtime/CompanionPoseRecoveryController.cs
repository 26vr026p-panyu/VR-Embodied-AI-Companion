using UnityEngine;
using UMT;

namespace CompanionAvatar
{
    /// <summary>Reseeds MMD secondary physics after a discontinuous root pose change only.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(9950)]
    public sealed class CompanionPoseRecoveryController : MonoBehaviour
    {
        public MMDTransformManager mmd;
        [Min(.5f)] public float resetDistance = 1f;
        [Range(45,180)] public float resetAngle = 90f;
        public int ResetCount { get; private set; }
        Vector3 previousPosition;
        Quaternion previousRotation;
        void OnEnable()
        {
            if(mmd==null)mmd=GetComponent<MMDTransformManager>();
            previousPosition=transform.position;
            previousRotation=transform.rotation;
        }
        void LateUpdate()
        {
            bool discontinuity=Vector3.Distance(previousPosition,transform.position)>resetDistance
                || Quaternion.Angle(previousRotation,transform.rotation)>resetAngle;
            previousPosition=transform.position;
            previousRotation=transform.rotation;
            if(discontinuity&&mmd!=null&&mmd.isActiveAndEnabled)
            {
                // Public UMT API clears stale Bullet velocities and reseeds garment/hair bodies.
                // No renderer/bone rebinding, no per-frame physics reset, no root movement.
                mmd.ResetPhysics();
                ResetCount++;
            }
        }
    }
}
