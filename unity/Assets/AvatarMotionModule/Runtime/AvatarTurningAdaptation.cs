using UnityEngine;
namespace CompanionAvatar {
 /// <summary>Turning-only foot placement and braking. No formation targets or root writes.</summary>
 [DisallowMultipleComponent] public sealed class AvatarTurningAdaptation : MonoBehaviour {
  public float walkMinimumHalfWidth=.055f,runMinimumHalfWidth=.075f;
  public float walkStrideReduction=.12f,runStrideReduction=.26f;
  public float largeRunTurnAngle=70,releaseAngle=25;
  public Vector3 MovementHeading {get;private set;}
  public float TurnAngle {get;private set;}
  public float TurnDirection {get;private set;}
  public float Amount {get;private set;}
  public bool BrakingForTurn {get;private set;}
  float yawDelta,lastYaw;bool initialized;Vector3 requestedDirection;
  public void Sample(Vector3 displacement,float dt){
   float yaw=transform.eulerAngles.y;if(!initialized){lastYaw=yaw;initialized=true;}
   yawDelta=Mathf.DeltaAngle(lastYaw,yaw);lastYaw=yaw;
   if(displacement.sqrMagnitude>.0000001f)MovementHeading=Vector3.ProjectOnPlane(displacement,Vector3.up).normalized;else MovementHeading=transform.forward;
   TurnAngle=Vector3.SignedAngle(transform.forward,MovementHeading,Vector3.up);TurnDirection=Mathf.Sign(Mathf.Abs(yawDelta)>.02f?yawDelta:TurnAngle);
   float target=Mathf.Clamp01(Mathf.Max(Mathf.Abs(yawDelta)/Mathf.Max(dt,.001f)/75,Mathf.Abs(TurnAngle)/45));
   Amount=Mathf.MoveTowards(Amount,target,dt*5);if(target<.001f&&Amount<.005f)Amount=0;
  }
  public float StrideScale(bool run)=>1-Amount*(run?runStrideReduction:walkStrideReduction);
  public Quaternion StepHeading(bool run){float a=Mathf.Clamp(TurnAngle,-(run?18:12),run?18:12)*Amount;return Quaternion.AngleAxis(a,Vector3.up)*transform.rotation;}
  public Vector3 BendForward(bool run)=>(StepHeading(run)*Vector3.forward);
  // Rotate the knee bend plane without changing either segment length.
  public Vector3 SafeBend(Vector3 hip,Vector3 direction,float along,float height,Vector3 preferred,int side){
   if(Amount<=0||height<.0001f)return preferred;
   Vector3 centre=hip+direction*along;
   float minimum=.035f;
   if(side*transform.InverseTransformPoint(centre+preferred*height).x>=minimum)return preferred;
   Vector3 outward=Vector3.ProjectOnPlane(transform.right*side,direction).normalized;
   if(outward.sqrMagnitude<.01f)return preferred;
   float lo=0,hi=1;
   for(int i=0;i<12;i++){
    float mid=(lo+hi)*.5f;
    Vector3 candidate=Vector3.Slerp(preferred,outward,mid).normalized;
    if(side*transform.InverseTransformPoint(centre+candidate*height).x<minimum)lo=mid;else hi=mid;
   }
   return Vector3.Slerp(preferred,outward,hi).normalized;
  }
  public Vector3 ClampFoot(Vector3 target,int side,bool run){if(Amount<=0)return target;var local=transform.InverseTransformPoint(target);local.x=side*Mathf.Max(side*local.x,run?runMinimumHalfWidth:walkMinimumHalfWidth);return transform.TransformPoint(local);}
  public void AdaptPlant(ref Vector3 anchor,int side,bool run){if(Amount<=0)return;anchor=transform.position+Quaternion.AngleAxis(yawDelta*.85f,Vector3.up)*(anchor-transform.position);anchor=ClampFoot(anchor,side,run);}
  public Quaternion SupportHeading(Quaternion old,bool run)=>Amount<=0?old:Quaternion.Slerp(old,StepHeading(run),1-Mathf.Exp(-Time.deltaTime*10*Amount));
  public Vector3 LimitVelocity(Vector3 desired,Vector3 current,float dt){
   if(desired.sqrMagnitude<.01f){BrakingForTurn=false;return desired;}
   requestedDirection=desired.normalized;float angle=Vector3.Angle(transform.forward,requestedDirection);
   if(Mathf.Max(current.magnitude,desired.magnitude)>1.8f&&angle>largeRunTurnAngle)BrakingForTurn=true;
   if(BrakingForTurn&&angle<releaseAngle)BrakingForTurn=false;
   return BrakingForTurn?Vector3.zero:desired;
  }
  public Vector3 Facing(Vector3 normal,float speed)=>BrakingForTurn?(speed<.65f?requestedDirection:transform.forward):normal;
  public float YawLimit(float normal,float speed)=>BrakingForTurn?(speed>=.65f?25:75):normal;
  void OnDisable(){Amount=0;initialized=false;BrakingForTurn=false;}
 }
}
