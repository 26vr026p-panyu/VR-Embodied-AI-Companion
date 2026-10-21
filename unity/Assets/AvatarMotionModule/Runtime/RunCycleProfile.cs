using UnityEngine;
namespace CompanionAvatar
{
    [CreateAssetMenu(menuName="Companion/Run Cycle Profile")]
    public sealed class RunCycleProfile:ScriptableObject
    {
        public float strideLength=1.8f,stanceEnd=.38f,halfStepWidth=.07f;
        public float toeLength=.135f,ankleToSole=.075f;
        public AnimationCurve pelvisHeight,pelvisYaw,footForward,footHeight,footPitch,armPitch,elbowFlexion;
        public void BuildAuthoredCycle()
        {
            pelvisHeight=C(0,-.035f,.12f,-.065f,.3f,-.005f,.44f,.025f,.5f,-.035f,.62f,-.065f,.8f,-.005f,.94f,.025f,1,-.035f);
            pelvisYaw=C(0,-3,.25f,0,.5f,3,.75f,0,1,-3);
            footForward=C(0,.14f,.38f,-.21f,.50f,-.26f,.62f,-.16f,.76f,.08f,.88f,.20f,1,.14f);
            footHeight=C(0,.005f,.38f,.06f,.50f,.25f,.62f,.34f,.76f,.26f,.88f,.12f,.96f,.025f,1,.005f);
            footPitch=C(0,2,.12f,0,.23f,0,.30f,15,.38f,30,.50f,35,.65f,22,.78f,5,.9f,-3,1,2);
            armPitch=C(0,-32,.12f,-23,.3f,16,.5f,29,.62f,19,.8f,-14,1,-32);
            elbowFlexion=C(0,86,.25f,78,.5f,74,.75f,82,1,86);
        }
        static AnimationCurve C(params float[] v)
        {
            var c=new AnimationCurve();for(int i=0;i<v.Length;i+=2)c.AddKey(v[i],v[i+1]);
            for(int i=0;i<c.length;i++)c.SmoothTangents(i,0);
            c.preWrapMode=c.postWrapMode=WrapMode.Loop;return c;
        }
    }
}
