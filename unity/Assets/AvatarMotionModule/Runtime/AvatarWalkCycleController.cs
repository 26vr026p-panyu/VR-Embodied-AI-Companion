using UnityEngine;
using UMT;

namespace CompanionAvatar
{
    /// <summary>Authored phase walking pose plus heel/toe support pivots. Never writes the character root.</summary>
    [DisallowMultipleComponent]
    public sealed class AvatarWalkCycleController : MonoBehaviour
    {
        public WalkCycleProfile profile;
        public AvatarMotionQualityController rig;
        public Transform hips,pelvis,spine,chest,leftShoulder,rightShoulder,leftToes,rightToes;
        public float Phase { get; private set; }
        public float Weight { get; private set; }
        public float Stride { get; private set; }
        public float RightFootPitch { get; private set; }
        public float LeftFootPitch { get; private set; }
        public Vector3 RightAnkleTarget { get; private set; }
        public Vector3 LeftAnkleTarget { get; private set; }
        public string PhaseName => Phase<.07f?"Right Contact":Phase<.2f?"Right Down / Left Toe-off":Phase<.5f?"Left Passing":Phase<.57f?"Left Contact":Phase<.7f?"Left Down / Right Toe-off":"Right Passing";
        sealed class Support
        {
            public Vector3 flatAnchor;
            public Quaternion heading;
            public float previousPhase;
            public bool valid;
        }
        readonly Support rightSupport=new Support(),leftSupport=new Support();
        Transform[] restBones;Quaternion[] restRotations;
        Vector3 grooveRest;Quaternion leftArmRest,rightArmRest,leftElbowRest,rightElbowRest;
        bool initialized,wasWalking;

        void Awake(){Initialize();}
        void Initialize()
        {
            if(initialized)return;
            if(rig==null)rig=GetComponent<AvatarMotionQualityController>();
            if(rig==null||profile==null)return;
            restBones=new[]{hips,pelvis,spine,chest,leftShoulder,rightShoulder,leftToes,rightToes};
            restRotations=new Quaternion[restBones.Length];
            for(int i=0;i<restBones.Length;i++)if(restBones[i]!=null)restRotations[i]=restBones[i].localRotation;
            grooveRest=rig.groove.localPosition;
            leftArmRest=rig.leftUpperArm.localRotation;rightArmRest=rig.rightUpperArm.localRotation;
            leftElbowRest=rig.leftLowerArm.localRotation;rightElbowRest=rig.rightLowerArm.localRotation;
            initialized=true;
        }
        void Update()
        {
            Initialize();if(!initialized)return;
            // These supplemental channels must not accumulate last frame's additive pose.
            for(int i=0;i<restBones.Length;i++)if(restBones[i]!=null)restBones[i].localRotation=restRotations[i];
        }
        public void ResetSupport(){leftSupport.valid=rightSupport.valid=false;wasWalking=false;Weight=0;}

        public void ApplyWalk(float travelled,float speed,float poseWeight,float dt)
        {
            Initialize();if(!initialized||!isActiveAndEnabled)return;
            Weight=poseWeight;
            if(poseWeight<.001f){ResetSupport();return;}
            if(!wasWalking){Phase=0;rightSupport.valid=leftSupport.valid=false;}
            wasWalking=true;
            Stride=profile.strideLength*Mathf.Lerp(.65f,1.1f,Mathf.InverseLerp(.35f,1.8f,speed));
            if(rig.turning!=null&&rig.turning.enabled)Stride*=rig.turning.StrideScale(false);
            Phase=Mathf.Repeat(Phase+Mathf.Max(0,travelled)/Mathf.Max(.35f,Stride),1);
            float p=Phase,yaw=profile.pelvisYaw.Evaluate(p),roll=profile.pelvisRoll.Evaluate(p);
            Vector3 body=new Vector3(profile.pelvisSide.Evaluate(p),profile.pelvisHeight.Evaluate(p),0);
            // Translation acts on the visual skeleton; its world root remains entirely owned by Follow.
            rig.groove.localPosition=Vector3.Lerp(rig.groove.localPosition,grooveRest+body,Weight);Pending(rig.groove);
            Pose(hips,0,new Vector3(0,yaw,roll),Weight);
            Pose(pelvis,1,new Vector3(1.5f,0,0),Weight);
            Pose(spine,2,new Vector3(profile.spinePitch.Evaluate(p),-yaw*.65f,-roll*.65f),Weight);
            Pose(chest,3,new Vector3(-.5f,-yaw*.65f,-roll*.2f),Weight);
            float shoulder=profile.shoulderYaw.Evaluate(p);
            Pose(leftShoulder,4,new Vector3(0,shoulder,0),Weight);
            Pose(rightShoulder,5,new Vector3(0,shoulder,0),Weight);
            float lp=Mathf.Repeat(p+.5f,1);
            RightAnkleTarget=Leg(rig.right,rightSupport,p,1,rightToes,7);
            LeftAnkleTarget=Leg(rig.left,leftSupport,lp,-1,leftToes,6);
            RightFootPitch=profile.footPitch.Evaluate(p);LeftFootPitch=profile.footPitch.Evaluate(lp);
            Arm(rig.leftUpperArm,rig.leftLowerArm,leftArmRest,leftElbowRest,p,35,Weight);
            int wave=rig.animator.GetLayerIndex("Upper Body Wave");
            float waveWeight=wave>=0?rig.animator.GetLayerWeight(wave):0;
            Arm(rig.rightUpperArm,rig.rightLowerArm,rightArmRest,rightElbowRest,lp,-35,Weight*(1-waveWeight));
        }
        Vector3 Leg(AvatarMotionQualityController.Leg leg,Support support,float p,int side,Transform toes,int toeIndex)
        {
            float pitch=profile.footPitch.Evaluate(p);
            Quaternion roll=Quaternion.AngleAxis(pitch,Vector3.right);
            bool stance=p<profile.stanceEnd;
            if(!support.valid||p<support.previousPhase)
            {
                support.heading=transform.rotation;
                support.flatAnchor=transform.TransformPoint(new Vector3(side*profile.halfStepWidth,leg.footHeight,Stride*(.28f-(stance?p:0))));
                support.valid=true;
            }
            if(rig.turning!=null&&rig.turning.enabled){rig.turning.AdaptPlant(ref support.flatAnchor,side,false);support.heading=rig.turning.SupportHeading(support.heading,false);}
            support.previousPhase=p;
            Vector3 target;
            Quaternion footRotation;
            if(stance)
            {
                // Heel rocker -> flat sole -> forefoot rocker. The contact pivot stays at its world landing.
                Vector3 pivot=new Vector3(0,-profile.ankleToSole,pitch<0?-profile.heelLength:profile.toeLength);
                Vector3 nominal=transform.TransformPoint(new Vector3(side*profile.halfStepWidth,leg.footHeight,Stride*(.28f-p)));
                target=Vector3.Lerp(support.flatAnchor,nominal,.18f)+support.heading*(pivot-roll*pivot);
                footRotation=support.heading*roll*leg.footBasis;
            }
            else
            {
                target=transform.TransformPoint(new Vector3(side*profile.halfStepWidth,leg.footHeight+profile.swingHeight.Evaluate(p),Stride*profile.swingForward.Evaluate(p)));
                footRotation=transform.rotation*roll*leg.footBasis;
            }
            if(rig.turning!=null&&rig.turning.enabled){target=rig.turning.ClampFoot(target,side,false);if(!stance)footRotation=rig.turning.StepHeading(false)*roll*leg.footBasis;}
            SolveLeg(leg,target,footRotation,Weight);
            Pose(toes,toeIndex,new Vector3(profile.toeBend.Evaluate(p),0,0),Weight);
            return target;
        }
        void SolveLeg(AvatarMotionQualityController.Leg leg,Vector3 target,Quaternion footRotation,float weight)
        {
            Quaternion upperBase=leg.upper.localRotation,lowerBase=leg.lower.localRotation,footBase=leg.foot.localRotation;
            Vector3 hip=leg.upper.position,v=target-hip;
            float distance=Mathf.Clamp(v.magnitude,.03f,(leg.upperLength+leg.lowerLength)*.999f);
            Vector3 direction=v.normalized;
            float along=(leg.upperLength*leg.upperLength-leg.lowerLength*leg.lowerLength+distance*distance)/(2*distance);
            float bendHeight=Mathf.Sqrt(Mathf.Max(0,leg.upperLength*leg.upperLength-along*along));
            Vector3 bend=Vector3.ProjectOnPlane(rig.turning!=null&&rig.turning.enabled?rig.turning.BendForward(false):transform.forward,direction).normalized;
            if(rig.turning!=null&&rig.turning.enabled)bend=rig.turning.SafeBend(hip,direction,along,bendHeight,bend,leg==rig.left?-1:1);
            Vector3 knee=hip+direction*along+bend*bendHeight;
            leg.upper.rotation=Quaternion.FromToRotation(leg.lower.position-hip,knee-hip)*leg.upper.rotation;
            leg.lower.rotation=Quaternion.FromToRotation(leg.foot.position-leg.lower.position,hip+direction*distance-leg.lower.position)*leg.lower.rotation;
            leg.foot.rotation=footRotation;
            Quaternion upper=leg.upper.localRotation,lower=leg.lower.localRotation,foot=leg.foot.localRotation;
            leg.upper.localRotation=Quaternion.Slerp(upperBase,upper,weight);
            leg.lower.localRotation=Quaternion.Slerp(lowerBase,lower,weight);
            leg.foot.localRotation=Quaternion.Slerp(footBase,foot,weight);
            Pending(leg.upper);Pending(leg.lower);Pending(leg.foot);
        }
        void Arm(Transform upper,Transform lower,Quaternion upperRest,Quaternion lowerRest,float oppositeLegPhase,float restDrop,float weight)
        {
            Quaternion armDown=upperRest*Quaternion.Euler(0,0,restDrop);
            Vector3 upperAxis=upper.parent.InverseTransformDirection(transform.right);
            Quaternion desired=Quaternion.AngleAxis(profile.armPitch.Evaluate(oppositeLegPhase),upperAxis)*armDown;
            upper.localRotation=Quaternion.Slerp(upper.localRotation,desired,weight);
            Vector3 elbowAxis=lower.parent.InverseTransformDirection(transform.right);
            desired=Quaternion.AngleAxis(-profile.elbowFlexion.Evaluate(oppositeLegPhase),elbowAxis)*lowerRest;
            lower.localRotation=Quaternion.Slerp(lower.localRotation,desired,weight);
            Pending(upper);Pending(lower);
        }
        void Pose(Transform bone,int index,Vector3 euler,float weight)
        {
            if(bone==null)return;
            bone.localRotation=Quaternion.Slerp(bone.localRotation,restRotations[index]*Quaternion.Euler(euler),weight);Pending(bone);
        }
        static void Pending(Transform bone){var b=bone.GetComponent<MMDBoneTransform>();if(b!=null)b.solverResetPending=true;}
        void OnDisable()
        {
            ResetSupport();if(!initialized)return;
            for(int i=0;i<restBones.Length;i++)if(restBones[i]!=null){restBones[i].localRotation=restRotations[i];Pending(restBones[i]);}
        }
    }
}
