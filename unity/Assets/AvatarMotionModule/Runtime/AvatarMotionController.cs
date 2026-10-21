using UnityEngine;

namespace CompanionAvatar
{
    public enum AvatarMotion { Idle, Walk, Run, Nod, Wave }

    /// <summary>Visual animation only. A separate follow module owns world position and rotation.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class AvatarMotionController : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [Min(0)] public float walkSpeed = 1.4f;
        [Min(0)] public float runSpeed = 3.5f;
        [Min(0)] public float speedDamping = .18f;
        [Min(.01f)] public float gestureFadeDuration = .18f;
        public float TargetMoveSpeed { get; private set; }
        public Animator Animator => animator;
        static readonly int Speed = Animator.StringToHash("MoveSpeed");
        static readonly int Nod = Animator.StringToHash("Nod");
        static readonly int Wave = Animator.StringToHash("Wave");
        int waveLayer, nodLayer;

        void Awake()
        {
            if(animator == null) animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            waveLayer = animator.GetLayerIndex("Upper Body Wave");
            nodLayer = animator.GetLayerIndex("Head Nod");
            if(waveLayer >= 0) animator.SetLayerWeight(waveLayer,0);
            if(nodLayer >= 0) animator.SetLayerWeight(nodLayer,0);
        }
        void Update()
        {
            if(animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
            {
                animator.SetFloat(Speed, TargetMoveSpeed, speedDamping, Time.deltaTime);
                FadeGesture(waveLayer,"Wave");
                FadeGesture(nodLayer,"Nod");
            }
        }
        void FadeGesture(int layer,string state)
        {
            if(layer < 0) return;
            bool active = animator.GetCurrentAnimatorStateInfo(layer).IsName(state);
            if(animator.IsInTransition(layer)) active |= animator.GetNextAnimatorStateInfo(layer).IsName(state);
            // An empty state with Write Defaults off can retain its last keyed pose.
            // Explicitly fading this masked layer to zero restores the live base pose.
            animator.SetLayerWeight(layer,Mathf.MoveTowards(animator.GetLayerWeight(layer),active?1:0,Time.deltaTime/Mathf.Max(.01f,gestureFadeDuration)));
        }
        public void SetMoveSpeed(float speed)
        {
            TargetMoveSpeed = float.IsNaN(speed) || float.IsInfinity(speed) ? 0 : Mathf.Max(0, speed);
        }
        public bool PlayMotion(string motion)
        {
            if(!System.Enum.TryParse(motion, true, out AvatarMotion value) || !System.Enum.IsDefined(typeof(AvatarMotion),value)) return false;
            return PlayMotion(value);
        }
        public bool PlayMotion(AvatarMotion motion)
        {
            switch(motion)
            {
                case AvatarMotion.Idle: SetMoveSpeed(0); return true;
                case AvatarMotion.Walk: SetMoveSpeed(walkSpeed); return true;
                case AvatarMotion.Run: SetMoveSpeed(runSpeed); return true;
                case AvatarMotion.Nod: return Trigger(Nod);
                case AvatarMotion.Wave: return Trigger(Wave);
                default: return false;
            }
        }
        bool Trigger(int parameter)
        {
            if(animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return false;
            animator.ResetTrigger(parameter);
            animator.SetTrigger(parameter);
            return true;
        }
        // Consume animation motion without moving the root, including replacement clips.
        void OnAnimatorMove() { }
    }
}
