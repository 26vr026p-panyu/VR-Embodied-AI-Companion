using UnityEngine;
using UMT;

namespace CompanionAvatar
{
    /// <summary>Additive visual attention, before UMT skinning; never writes root position or BlendShapes.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(9970)]
    public sealed class CompanionGazeController : MonoBehaviour
    {
        public CompanionFollowController follow;
        public Animator animator;
        public Transform gazeTarget, chest, neck, head, leftEye, rightEye;
        public bool enableGaze = true;
        public bool allowBodyTurn = true;
        [Range(0,1)] public float gazeWeight = 1;
        [Min(.05f)] public float headSmoothTime = .25f;
        [Range(10,120)] public float bodyTurnThreshold = 65;
        [Min(0)] public float bodyTurnDelay = 1.2f;
        [Min(1)] public float bodyTurnSpeed = 28;
        public float RawTargetYaw { get; private set; }
        public float HeadYaw => headYaw;
        public bool BodyTurning { get; private set; }
        Quaternion neckBind,leftEyeBind,rightEyeBind;
        float headYaw,neckYaw,chestYaw,eyeYaw,headPitch,neckPitch,chestPitch,eyePitch,turnTimer,weight;
        int nodLayer;
        void Awake()
        {
            if(animator==null)animator=GetComponent<Animator>();
            if(follow==null)follow=GetComponent<CompanionFollowController>();
            if(neck!=null)neckBind=neck.localRotation;if(leftEye!=null)leftEyeBind=leftEye.localRotation;if(rightEye!=null)rightEyeBind=rightEye.localRotation;
            nodLayer=animator!=null?animator.GetLayerIndex("Head Nod"):-1;
        }
        void Update()
        {
            // These three bones have no curves in the current clips. Restore before UMT to avoid accumulation.
            if(neck!=null)neck.localRotation=neckBind;
            if(leftEye!=null)leftEye.localRotation=leftEyeBind;if(rightEye!=null)rightEye.localRotation=rightEyeBind;
        }
        void LateUpdate()
        {
            if(head==null||animator==null||!animator.isActiveAndEnabled)return;
            float dt=Mathf.Min(Time.deltaTime,.1f);
            bool valid=enableGaze&&gazeTarget!=null;
            float nod=nodLayer>=0?animator.GetLayerWeight(nodLayer):0;
            float movingAttention=follow!=null?Mathf.Lerp(1,.35f,follow.DepartureMovementBlend):1;
            weight=Smooth(weight,valid?gazeWeight*(1-nod)*movingAttention:0,.18f,dt);
            Vector3 local=valid?transform.InverseTransformDirection(gazeTarget.position-head.position):Vector3.forward;
            RawTargetYaw=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;
            float pitch=-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;
            bool stationary=follow!=null&&!follow.FormationActive&&follow.ActualMoveSpeed<.08f;
            if(valid&&allowBodyTurn&&stationary)
            {
                if(Mathf.Abs(RawTargetYaw)>bodyTurnThreshold)turnTimer+=dt;else if(!BodyTurning)turnTimer=0;
                if(turnTimer>=bodyTurnDelay)BodyTurning=true;
                if(Mathf.Abs(RawTargetYaw)<20){BodyTurning=false;turnTimer=0;}
                if(BodyTurning)follow.RequestStationaryFacing(gazeTarget.position-transform.position,bodyTurnSpeed);
            }
            else{BodyTurning=false;turnTimer=0;}
            chestYaw=Smooth(chestYaw,Mathf.Clamp(RawTargetYaw*.15f,-12,12)*weight,.65f,dt);
            neckYaw=Smooth(neckYaw,Mathf.Clamp(RawTargetYaw*.25f,-20,20)*weight,.4f,dt);
            headYaw=Smooth(headYaw,Mathf.Clamp(RawTargetYaw*.6f,-45,45)*weight,headSmoothTime,dt);
            eyeYaw=Smooth(eyeYaw,Mathf.Clamp(RawTargetYaw-chestYaw-neckYaw-headYaw,-12,12)*weight,.08f,dt);
            chestPitch=Smooth(chestPitch,Mathf.Clamp(pitch*.15f,-4,4)*weight,.65f,dt);
            neckPitch=Smooth(neckPitch,Mathf.Clamp(pitch*.25f,-8,8)*weight,.4f,dt);
            headPitch=Smooth(headPitch,Mathf.Clamp(pitch*.6f,-18,18)*weight,headSmoothTime,dt);
            eyePitch=Smooth(eyePitch,Mathf.Clamp(pitch-chestPitch-neckPitch-headPitch,-8,8)*weight,.08f,dt);
            float breathing=stationary?.35f*Mathf.Sin(Time.time*1.6f):0;
            Add(chest,chestYaw,chestPitch+breathing);Add(neck,neckYaw,neckPitch);Add(head,headYaw,headPitch);
            Add(leftEye,eyeYaw,eyePitch);Add(rightEye,eyeYaw,eyePitch);
        }
        void Add(Transform bone,float yaw,float pitch)
        {
            if(bone==null||bone.parent==null)return;
            Vector3 up=bone.parent.InverseTransformDirection(transform.up),right=bone.parent.InverseTransformDirection(transform.right);
            bone.localRotation=Quaternion.AngleAxis(yaw,up)*Quaternion.AngleAxis(pitch,right)*bone.localRotation;
            var mmd=bone.GetComponent<MMDBoneTransform>();if(mmd!=null)mmd.solverResetPending=true;
        }
        static float Smooth(float current,float target,float time,float dt)=>Mathf.Lerp(current,target,1-Mathf.Exp(-dt/Mathf.Max(.02f,time)));
        void OnDisable()
        {
            if(neck!=null)neck.localRotation=neckBind;if(leftEye!=null)leftEye.localRotation=leftEyeBind;if(rightEye!=null)rightEye.localRotation=rightEyeBind;
            BodyTurning=false;turnTimer=weight=0;
        }
    }
}
