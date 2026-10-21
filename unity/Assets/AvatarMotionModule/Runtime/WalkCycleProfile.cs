using UnityEngine;

namespace CompanionAvatar
{
    /// <summary>One complete right-contact to right-contact walking cycle, independent of the run gait.</summary>
    [CreateAssetMenu(menuName="Companion/Walk Cycle Profile")]
    public sealed class WalkCycleProfile : ScriptableObject
    {
        [Header("Timing and foot geometry (metres)")]
        public float strideLength=1.22f;
        public float stanceEnd=.62f;
        public float heelLength=.065f, toeLength=.135f, ankleToSole=.075f;
        public float halfStepWidth=.065f;
        public float runBlendStart=1.8f,runBlendEnd=2.2f;
        [Header("Body — phase 0/0.5 = right/left contact; 0.1/0.6 = down; 0.3/0.8 = passing")]
        public AnimationCurve pelvisSide,pelvisHeight,pelvisYaw,pelvisRoll;
        public AnimationCurve spinePitch,shoulderYaw;
        [Header("Per foot — contact 0, flat 0.12, heel rise 0.42, toe-off 0.62, passing 0.8")]
        public AnimationCurve swingForward,swingHeight,footPitch,toeBend;
        [Header("Arm opposite this foot — forward at foot contact")]
        public AnimationCurve armPitch,elbowFlexion;

        public void BuildAuthoredCycle()
        {
            pelvisSide=C(0,0,.1f,.018f,.28f,.032f,.42f,.017f,.5f,0,.6f,-.018f,.78f,-.032f,.92f,-.017f,1,0);
            pelvisHeight=C(0,-.025f,.1f,-.036f,.3f,-.006f,.42f,-.012f,.5f,-.025f,.6f,-.036f,.8f,-.006f,.92f,-.012f,1,-.025f);
            pelvisYaw=C(0,-4,.12f,-2.5f,.3f,1,.5f,4,.62f,2.5f,.8f,-1,1,-4);
            pelvisRoll=C(0,0,.16f,2,.3f,1.5f,.5f,0,.66f,-2,.8f,-1.5f,1,0);
            spinePitch=C(0,2,.1f,3,.3f,1.5f,.5f,2,.6f,3,.8f,1.5f,1,2);
            shoulderYaw=C(0,1.5f,.25f,0,.5f,-1.5f,.75f,0,1,1.5f);
            // Swing is a shaped recovery path, not a lifted sinusoid of the whole leg.
            swingForward=C(0,.28f,.62f,-.285f,.70f,-.25f,.80f,-.015f,.9f,.21f,.97f,.278f,1,.28f);
            swingHeight=C(0,.012f,.62f,.06f,.70f,.145f,.78f,.12f,.86f,.065f,.94f,.024f,1,.012f);
            footPitch=C(0,-14,.1f,0,.4f,0,.5f,12,.56f,25,.62f,36,.7f,15,.8f,-2,.9f,-9,1,-14);
            toeBend=C(0,0,.42f,0,.54f,-14,.62f,-30,.7f,-4,.8f,0,1,0);
            armPitch=C(0,-19,.12f,-15,.3f,6,.5f,16,.62f,12,.8f,-7,1,-19);
            elbowFlexion=C(0,24,.18f,20,.38f,13,.5f,14,.7f,18,.9f,25,1,24);
        }
        static AnimationCurve C(params float[] values)
        {
            int count=values.Length/2;var keys=new Keyframe[count];
            for(int i=0;i<count;i++)
            {
                float tangent=0;
                if(i>0&&i<count-1)
                {
                    float a=(values[2*i+1]-values[2*i-1])/(values[2*i]-values[2*i-2]);
                    float b=(values[2*i+3]-values[2*i+1])/(values[2*i+2]-values[2*i]);
                    if(a*b>0)tangent=(a+b)*.5f;
                }
                keys[i]=new Keyframe(values[2*i],values[2*i+1],tangent,tangent);
            }
            // Wrapped derivative for body curves; contact/foot flat transitions have authored zero tangents.
            if(Mathf.Approximately(values[1],values[values.Length-1]))
            {
                float slope=(values[3]-values[values.Length-3])/(values[2]+1-values[values.Length-4]);
                keys[0].inTangent=keys[0].outTangent=slope;keys[count-1].inTangent=keys[count-1].outTangent=slope;
            }
            return new AnimationCurve(keys){preWrapMode=WrapMode.Loop,postWrapMode=WrapMode.Loop};
        }
    }
}
