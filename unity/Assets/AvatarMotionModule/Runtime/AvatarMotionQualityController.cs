using UnityEngine;
using UMT;

namespace CompanionAvatar
{
    /// <summary>Generic-rig gait polish on the existing FK bones; no root motion, no physics forces.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(9960)]
    public sealed class AvatarMotionQualityController : MonoBehaviour
    {
        [System.Serializable] public sealed class Leg
        {
            public Transform upper,lower,foot;
            [System.NonSerialized] public float upperLength,lowerLength,footHeight,lateral;
            [System.NonSerialized] public Vector3 plant,swingStart;
            [System.NonSerialized] public bool planted;
            [System.NonSerialized] public Quaternion footBasis,plantRotation;
        }
        public CompanionFollowController follow;
        public Animator animator;
        public AvatarWalkCycleController walkCycle;
        public AvatarRunCycleController runCycle;
        public AvatarTurningAdaptation turning;
        public Transform groove,leftUpperArm,leftLowerArm,rightUpperArm,rightLowerArm;
        public Leg left=new Leg(),right=new Leg();
        public bool enableGaitPolish=true;
        [Header("Legacy fallback / pivot gait (assigned cycle profiles own normal Walk and Run)")]
        [Min(.2f)] public float walkStrideLength=.85f;
        [Min(.2f)] public float runStrideLength=1.35f;
        [Min(.01f)] public float walkFootLift=.055f;
        [Min(.01f)] public float runFootLift=.11f;
        [Min(.02f)] public float footSpacing=.09f;
        [Min(.05f)] public float blendTime=.2f;
        public float GaitWeight { get; private set; }
        public float Phase { get; private set; }
        public float LeftPlantError => left.foot!=null?Vector3.Distance(left.foot.position,left.plant):0;
        public float RightPlantError => right.foot!=null?Vector3.Distance(right.foot.position,right.plant):0;
        Vector3 lastRoot,grooveBind;
        Quaternion leftArmBind,leftElbowBind,rightArmBind,rightElbowBind;
        float cadenceSpeed,lastYaw;bool moving;int waveLayer;
        void Awake()
        {
            if(follow==null)follow=GetComponent<CompanionFollowController>();if(animator==null)animator=GetComponent<Animator>();
            if(walkCycle==null)walkCycle=GetComponent<AvatarWalkCycleController>();
            if(runCycle==null)runCycle=GetComponent<AvatarRunCycleController>();
            if(turning==null)turning=GetComponent<AvatarTurningAdaptation>();
            lastRoot=transform.position;lastYaw=transform.eulerAngles.y;grooveBind=groove!=null?groove.localPosition:Vector3.zero;
            Init(left);Init(right);
            if(leftUpperArm!=null)leftArmBind=leftUpperArm.localRotation;if(leftLowerArm!=null)leftElbowBind=leftLowerArm.localRotation;
            if(rightUpperArm!=null)rightArmBind=rightUpperArm.localRotation;if(rightLowerArm!=null)rightElbowBind=rightLowerArm.localRotation;
            waveLayer=animator!=null?animator.GetLayerIndex("Upper Body Wave"):-1;
        }
        void Init(Leg leg)
        {
            if(leg.upper==null||leg.lower==null||leg.foot==null)return;
            leg.upperLength=Vector3.Distance(leg.upper.position,leg.lower.position);leg.lowerLength=Vector3.Distance(leg.lower.position,leg.foot.position);
            Vector3 local=transform.InverseTransformPoint(leg.foot.position);leg.footHeight=local.y;leg.lateral=Mathf.Sign(local.x)*Mathf.Max(Mathf.Abs(local.x),footSpacing);
            leg.footBasis=Quaternion.Inverse(transform.rotation)*leg.foot.rotation;leg.plantRotation=leg.foot.rotation;leg.plant=leg.swingStart=leg.foot.position;
        }
        void Update(){if(groove!=null)groove.localPosition=grooveBind;}
        void LateUpdate()
        {
            if(animator==null||!animator.isActiveAndEnabled)return;
            float dt=Mathf.Min(Time.deltaTime,.1f);Vector3 delta=transform.position-lastRoot;delta.y=0;lastRoot=transform.position;
            float speed=follow!=null&&follow.isActiveAndEnabled?follow.ActualMoveSpeed:delta.magnitude/Mathf.Max(Time.deltaTime,.001f);
            float turn=Mathf.Abs(Mathf.DeltaAngle(lastYaw,transform.eulerAngles.y));lastYaw=transform.eulerAngles.y;
            // Old world-space foot plants are invalid after a teleport or discontinuous turn.
            if(delta.magnitude>1f||turn>90f){GaitWeight=0;moving=false;cadenceSpeed=0;if(walkCycle!=null)walkCycle.ResetSupport();if(runCycle!=null)runCycle.ResetSupport();return;}
            if(turning!=null&&turning.enabled)turning.Sample(delta,dt);
            bool pivot=speed<.06f&&turn>.01f&&turn<15;
            bool active=enableGaitPolish&&(speed>.06f||pivot)&&delta.magnitude<1;
            GaitWeight=Mathf.MoveTowards(GaitWeight,active?1:0,dt/Mathf.Max(.05f,blendTime));
            cadenceSpeed=Mathf.Lerp(cadenceSpeed,speed,1-Mathf.Exp(-dt/.15f));
            if(active&&!moving)
            {
                Phase=.28f;
                if(left.foot!=null){left.plant=left.swingStart=left.foot.position;left.plantRotation=left.foot.rotation;}
                if(right.foot!=null){right.plant=right.swingStart=right.foot.position;right.plantRotation=right.foot.rotation;}
                left.planted=true;right.planted=false;
            }
            moving=active;
            bool separateWalk=walkCycle!=null&&walkCycle.isActiveAndEnabled&&walkCycle.profile!=null;
            float walkWeight=separateWalk&&!pivot?1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(walkCycle.profile.runBlendStart,walkCycle.profile.runBlendEnd,cadenceSpeed)):0;
            // The previous solver is retained for turn-in-place and missing-profile fallback.
            float run=separateWalk&&!pivot?1:Mathf.InverseLerp(1.6f,3.2f,cadenceSpeed),stride=Mathf.Lerp(walkStrideLength,runStrideLength,run);
            if(pivot)stride=.4f;
            if(active)Phase=Mathf.Repeat(Phase+(pivot?turn*Mathf.Deg2Rad*.25f:delta.magnitude)/Mathf.Max(.2f,stride),1);
            if(GaitWeight<=0){if(walkCycle!=null)walkCycle.ResetSupport();if(runCycle!=null)runCycle.ResetSupport();return;}
            if(separateWalk&&runCycle!=null&&runCycle.isActiveAndEnabled&&runCycle.profile!=null&&!pivot)
            {
                runCycle.ApplyRun(delta.magnitude,speed,GaitWeight);
                walkCycle.ApplyWalk(delta.magnitude,speed,walkWeight*GaitWeight,dt);
                return;
            }
            if(groove!=null)
            {
                float lower=Mathf.Lerp(.035f,.055f,run),bounce=Mathf.Lerp(.006f,.015f,run)*Mathf.Cos(Phase*Mathf.PI*4);
                groove.localPosition=grooveBind+Vector3.down*(lower+bounce)*GaitWeight;Pending(groove);
            }
            float stance=Mathf.Lerp(.62f,.46f,run),lift=Mathf.Lerp(walkFootLift,runFootLift,run);
            Step(left,Phase,stride,stance,lift);Step(right,Mathf.Repeat(Phase+.5f,1),stride,stance,lift);
            float swing=Mathf.Sin(Phase*Mathf.PI*2)*Mathf.Lerp(12,23,run),elbow=Mathf.Lerp(18,58,run);
            Arm(leftUpperArm,leftLowerArm,leftArmBind,leftElbowBind,-swing,elbow,30,GaitWeight);
            float wave=waveLayer>=0?animator.GetLayerWeight(waveLayer):0;
            Arm(rightUpperArm,rightLowerArm,rightArmBind,rightElbowBind,swing,elbow,-30,GaitWeight*(1-wave));
            if(separateWalk)walkCycle.ApplyWalk(delta.magnitude,speed,walkWeight*GaitWeight,dt);
        }
        void Step(Leg leg,float phase,float stride,float stance,float lift)
        {
            if(leg.upper==null||leg.lower==null||leg.foot==null)return;
            bool planted=phase<stance;Vector3 target;
            Vector3 ahead=transform.TransformPoint(new Vector3(leg.lateral,leg.footHeight,stride*stance*.5f));
            if(planted)
            {
                if(!leg.planted){leg.plant=ahead;leg.plantRotation=transform.rotation*leg.footBasis;}
                if(turning!=null&&turning.enabled){turning.AdaptPlant(ref leg.plant,leg.lateral<0?-1:1,false);leg.plantRotation=turning.SupportHeading(leg.plantRotation,false);}
                target=leg.plant;
            }
            else
            {
                if(leg.planted)leg.swingStart=leg.foot.position;
                float u=(phase-stance)/(1-stance);target=Vector3.Lerp(leg.swingStart,ahead,Mathf.SmoothStep(0,1,u));target.y+=Mathf.Sin(u*Mathf.PI)*lift;
            }
            if(turning!=null&&turning.enabled)target=turning.ClampFoot(target,leg.lateral<0?-1:1,false);
            leg.planted=planted;
            Quaternion upperBase=leg.upper.localRotation,lowerBase=leg.lower.localRotation,footBase=leg.foot.localRotation;
            Vector3 h=leg.upper.position,to=target-h;float dist=Mathf.Clamp(to.magnitude,.01f,(leg.upperLength+leg.lowerLength)*.995f);Vector3 direction=to.normalized;
            float along=(leg.upperLength*leg.upperLength-leg.lowerLength*leg.lowerLength+dist*dist)/(2*dist);
            float height=Mathf.Sqrt(Mathf.Max(0,leg.upperLength*leg.upperLength-along*along));
            Vector3 bend=Vector3.ProjectOnPlane(transform.forward,direction).normalized;
            if(bend.sqrMagnitude<.01f)bend=transform.forward;
            if(turning!=null&&turning.enabled)bend=turning.SafeBend(h,direction,along,height,bend,leg.lateral<0?-1:1);
            Vector3 knee=h+direction*along+bend*height;
            leg.upper.rotation=Quaternion.FromToRotation(leg.lower.position-h,knee-h)*leg.upper.rotation;
            Vector3 reachable=h+direction*dist;
            leg.lower.rotation=Quaternion.FromToRotation(leg.foot.position-leg.lower.position,reachable-leg.lower.position)*leg.lower.rotation;
            leg.foot.rotation=planted?leg.plantRotation:Quaternion.Slerp(leg.plantRotation,transform.rotation*leg.footBasis,(phase-stance)/(1-stance));
            Quaternion upperSolved=leg.upper.localRotation,lowerSolved=leg.lower.localRotation,footSolved=leg.foot.localRotation;
            leg.upper.localRotation=Quaternion.Slerp(upperBase,upperSolved,GaitWeight);leg.lower.localRotation=Quaternion.Slerp(lowerBase,lowerSolved,GaitWeight);leg.foot.localRotation=Quaternion.Slerp(footBase,footSolved,GaitWeight);
            Pending(leg.upper);Pending(leg.lower);Pending(leg.foot);
        }
        void Arm(Transform upper,Transform lower,Quaternion upperBind,Quaternion lowerBind,float swing,float elbow,float rest,float weight)
        {
            if(upper!=null){upper.localRotation=Quaternion.Slerp(upper.localRotation,upperBind*Quaternion.Euler(swing,0,rest),weight);Pending(upper);}
            if(lower!=null){lower.localRotation=Quaternion.Slerp(lower.localRotation,lowerBind*Quaternion.Euler(-elbow,0,0),weight);Pending(lower);}
        }
        static void Pending(Transform t){var b=t.GetComponent<MMDBoneTransform>();if(b!=null)b.solverResetPending=true;}
        void OnDisable(){if(groove!=null)groove.localPosition=grooveBind;GaitWeight=0;moving=false;}
    }
}
