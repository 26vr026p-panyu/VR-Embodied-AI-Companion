using UnityEngine;
using UMT;
namespace CompanionAvatar
{
    /// <summary>Independent running cycle: compressed stance, push-off, flight and folded recovery leg.</summary>
    [DisallowMultipleComponent]
    public sealed class AvatarRunCycleController:MonoBehaviour
    {
        public AvatarMotionQualityController rig;
        public RunCycleProfile profile;
        public Transform hips,spine,chest,leftToes,rightToes;
        public float Phase {get;private set;}
        public float Weight {get;private set;}
        public bool InFlight => profile!=null && (Phase>profile.stanceEnd&&Phase<.5f || Phase>.5f+profile.stanceEnd);
        public float Stride {get;private set;}
        Transform[] bones;Quaternion[] rest;Vector3 grooveRest;
        Quaternion leftArm,rightArm,leftElbow,rightElbow;
        Vector3 leftPlant,rightPlant;float previousLeft,previousRight;bool initialized,active;
        void Awake(){Initialize();}
        void Initialize()
        {
            if(initialized||rig==null||profile==null)return;
            bones=new[]{hips,spine,chest,leftToes,rightToes};rest=new Quaternion[bones.Length];
            for(int i=0;i<bones.Length;i++)if(bones[i]!=null)rest[i]=bones[i].localRotation;
            grooveRest=rig.groove.localPosition;leftArm=rig.leftUpperArm.localRotation;rightArm=rig.rightUpperArm.localRotation;
            leftElbow=rig.leftLowerArm.localRotation;rightElbow=rig.rightLowerArm.localRotation;initialized=true;
        }
        void Update(){Initialize();if(!initialized)return;for(int i=0;i<bones.Length;i++)if(bones[i]!=null)bones[i].localRotation=rest[i];}
        public void ResetSupport(){active=false;Weight=0;}
        public void ApplyRun(float distance,float speed,float weight)
        {
            Initialize();if(!initialized||!isActiveAndEnabled)return;Weight=weight;
            if(weight<.001f){ResetSupport();return;}
            bool first=!active;if(first){Phase=0;previousLeft=previousRight=1;}
            active=true;Stride=profile.strideLength*Mathf.Lerp(.9f,1.1f,Mathf.InverseLerp(2,3.6f,speed));
            if(rig.turning!=null&&rig.turning.enabled)Stride*=rig.turning.StrideScale(true);
            Phase=Mathf.Repeat(Phase+distance/Stride,1);
            float yaw=profile.pelvisYaw.Evaluate(Phase);
            rig.groove.localPosition=Vector3.Lerp(rig.groove.localPosition,grooveRest+Vector3.up*profile.pelvisHeight.Evaluate(Phase),weight);Pending(rig.groove);
            Pose(0,new Vector3(6,yaw,Mathf.Sin(Phase*Mathf.PI*2)*.7f-(rig.turning!=null&&rig.turning.enabled?rig.turning.TurnDirection*rig.turning.Amount*2.5f:0)),weight);
            Pose(1,new Vector3(2,-yaw*.5f,0),weight);Pose(2,new Vector3(-1,-yaw*.65f,0),weight);
            float lp=Mathf.Repeat(Phase+.5f,1);
            Leg(rig.right,Phase,1,ref rightPlant,ref previousRight,first,4);
            Leg(rig.left,lp,-1,ref leftPlant,ref previousLeft,first,3);
            Arm(rig.leftUpperArm,rig.leftLowerArm,leftArm,leftElbow,Phase,35,weight);
            int layer=rig.animator.GetLayerIndex("Upper Body Wave");float wave=layer>=0?rig.animator.GetLayerWeight(layer):0;
            Arm(rig.rightUpperArm,rig.rightLowerArm,rightArm,rightElbow,lp,-35,weight*(1-wave));
        }
        void Leg(AvatarMotionQualityController.Leg leg,float p,int side,ref Vector3 plant,ref float previous,bool first,int toe)
        {
            if(first||p<previous)plant=transform.TransformPoint(new Vector3(side*profile.halfStepWidth,leg.footHeight,Stride*(.14f-p)));
            if(rig.turning!=null&&rig.turning.enabled)rig.turning.AdaptPlant(ref plant,side,true);
            previous=p;float pitch=profile.footPitch.Evaluate(p);var roll=Quaternion.AngleAxis(pitch,Vector3.right);
            Vector3 target;
            if(p<profile.stanceEnd)
            {
                // A small compliance leaves the stance responsive to turns; never a rigid foot constraint.
                var nominal=transform.TransformPoint(new Vector3(side*profile.halfStepWidth,leg.footHeight,Stride*(.14f-p)));
                var pivot=new Vector3(0,-profile.ankleToSole,profile.toeLength);
                target=Vector3.Lerp(plant,nominal,.18f)+transform.rotation*(pivot-roll*pivot);
            }
            else target=transform.TransformPoint(new Vector3(side*profile.halfStepWidth,leg.footHeight+profile.footHeight.Evaluate(p),Stride*profile.footForward.Evaluate(p)));
            var heading=transform.rotation;
            if(rig.turning!=null&&rig.turning.enabled){target=rig.turning.ClampFoot(target,side,true);heading=rig.turning.StepHeading(true);}
            Solve(leg,target,heading*roll*leg.footBasis,Weight);
            Pose(toe,new Vector3(p<profile.stanceEnd?-Mathf.Max(0,pitch)*.8f:0,0,0),Weight);
        }
        void Solve(AvatarMotionQualityController.Leg leg,Vector3 target,Quaternion rotation,float weight)
        {
            var a=leg.upper.localRotation;var b=leg.lower.localRotation;var c=leg.foot.localRotation;
            Vector3 hip=leg.upper.position,v=target-hip;float d=Mathf.Clamp(v.magnitude,.03f,(leg.upperLength+leg.lowerLength)*.998f);Vector3 dir=v.normalized;
            float along=(leg.upperLength*leg.upperLength-leg.lowerLength*leg.lowerLength+d*d)/(2*d);
            float height=Mathf.Sqrt(Mathf.Max(0,leg.upperLength*leg.upperLength-along*along));
            Vector3 bend=Vector3.ProjectOnPlane(rig.turning!=null&&rig.turning.enabled?rig.turning.BendForward(true):transform.forward,dir).normalized;
            if(rig.turning!=null&&rig.turning.enabled)bend=rig.turning.SafeBend(hip,dir,along,height,bend,leg==rig.left?-1:1);
            Vector3 knee=hip+dir*along+bend*height;
            leg.upper.rotation=Quaternion.FromToRotation(leg.lower.position-hip,knee-hip)*leg.upper.rotation;
            leg.lower.rotation=Quaternion.FromToRotation(leg.foot.position-leg.lower.position,hip+dir*d-leg.lower.position)*leg.lower.rotation;
            leg.foot.rotation=rotation;
            leg.upper.localRotation=Quaternion.Slerp(a,leg.upper.localRotation,weight);leg.lower.localRotation=Quaternion.Slerp(b,leg.lower.localRotation,weight);leg.foot.localRotation=Quaternion.Slerp(c,leg.foot.localRotation,weight);
            Pending(leg.upper);Pending(leg.lower);Pending(leg.foot);
        }
        void Pose(int i,Vector3 euler,float weight){if(bones[i]==null)return;bones[i].localRotation=Quaternion.Slerp(bones[i].localRotation,rest[i]*Quaternion.Euler(euler),weight);Pending(bones[i]);}
        void Arm(Transform upper,Transform lower,Quaternion a,Quaternion b,float p,float drop,float weight)
        {
            var rotation=Quaternion.AngleAxis(profile.armPitch.Evaluate(p),upper.parent.InverseTransformDirection(transform.right))*a*Quaternion.Euler(0,0,drop);
            upper.localRotation=Quaternion.Slerp(upper.localRotation,rotation,weight);
            rotation=Quaternion.AngleAxis(-profile.elbowFlexion.Evaluate(p),lower.parent.InverseTransformDirection(transform.right))*b;
            lower.localRotation=Quaternion.Slerp(lower.localRotation,rotation,weight);Pending(upper);Pending(lower);
        }
        static void Pending(Transform t){var b=t.GetComponent<MMDBoneTransform>();if(b!=null)b.solverResetPending=true;}
        void OnDisable(){ResetSupport();if(!initialized)return;for(int i=0;i<bones.Length;i++)if(bones[i]!=null){bones[i].localRotation=rest[i];Pending(bones[i]);}}
    }
}
