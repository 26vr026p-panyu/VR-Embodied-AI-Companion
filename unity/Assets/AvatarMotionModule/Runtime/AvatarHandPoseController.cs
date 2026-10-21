using UnityEngine;
using UMT;
namespace CompanionAvatar {
 [DisallowMultipleComponent,DefaultExecutionOrder(9970)] public sealed class AvatarHandPoseController:MonoBehaviour {
  [System.Serializable] public class Joint {public Transform bone;public Vector3 curlAxis;public float idleDegrees;public Vector3 adductionAxis;public float adductionDegrees;public bool right;[System.NonSerialized] public Quaternion rest;}
  public AvatarMotionController motion;public Joint[] fingers=new Joint[0];
  [Min(.02f)] public float smoothing=.18f;public float walkCurlScale=1.1f,runCurlScale=1.4f;
  float curl=1,openRight;int wave=-1;
  void Awake(){if(!motion)motion=GetComponent<AvatarMotionController>();foreach(var j in fingers)if(j.bone)j.rest=j.bone.localRotation;if(motion&&motion.Animator)wave=motion.Animator.GetLayerIndex("Upper Body Wave");}
  void LateUpdate(){if(!motion||!motion.Animator)return;float speed=motion.TargetMoveSpeed;float wanted=Mathf.Lerp(1,walkCurlScale,Mathf.Clamp01(speed/1.2f));wanted=Mathf.Lerp(wanted,runCurlScale,Mathf.InverseLerp(1.8f,3.2f,speed));float alpha=1-Mathf.Exp(-Time.deltaTime/Mathf.Max(.02f,smoothing));curl=Mathf.Lerp(curl,wanted,alpha);float waveWeight=wave<0?0:motion.Animator.GetLayerWeight(wave);openRight=Mathf.Lerp(openRight,waveWeight,alpha);
   foreach(var j in fingers){if(!j.bone||j.curlAxis.sqrMagnitude<.1f)continue;j.bone.localRotation=j.rest*Quaternion.AngleAxis(j.adductionDegrees*(j.right?1-openRight:1),j.adductionAxis.sqrMagnitude>.01f?j.adductionAxis.normalized:Vector3.up)*Quaternion.AngleAxis(j.idleDegrees*curl*(j.right?1-openRight:1),j.curlAxis.normalized);var b=j.bone.GetComponent<MMDBoneTransform>();if(b)b.solverResetPending=true;}}
  void OnDisable(){foreach(var j in fingers)if(j.bone){j.bone.localRotation=j.rest;var b=j.bone.GetComponent<MMDBoneTransform>();if(b)b.solverResetPending=true;}}
 }
}
