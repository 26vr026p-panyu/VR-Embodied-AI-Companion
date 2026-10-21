using UnityEditor;
using UnityEngine;

namespace CompanionAvatar.Editor
{
    public static class CompanionContactDebugTools
    {
        [MenuItem("Tools/Companion Avatar/Create Contact Probe (Play Mode)")]
        static void CreateProbe()
        {
            if(!Application.isPlaying)
            {
                Debug.Log("Enter Play Mode, select a contact zone, then create a temporary contact probe.");
                return;
            }
            var zone=Selection.activeGameObject==null?null:Selection.activeGameObject.GetComponent<CompanionContactZone>();
            if(zone==null)zone=Object.FindAnyObjectByType<CompanionContactZone>();
            if(zone==null)return;
            var probe=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            probe.name="ContactDebugProbe_PlayOnly";
            probe.transform.localScale=Vector3.one*.12f;
            probe.transform.position=zone.GetComponent<Collider>().bounds.center+Vector3.forward*.35f;
            probe.GetComponent<SphereCollider>().isTrigger=true;
            var body=probe.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            probe.AddComponent<CompanionContactInteractor>();
            Selection.activeGameObject=probe;
            Debug.Log("Move this temporary probe into/out of Head, LeftHand, RightHand or Torso zones in Scene view. Stop Play to remove it.",probe);
        }
    }
}
