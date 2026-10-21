using System;
using UnityEngine;

[Serializable] public sealed class LipSyncUnit
{
    public string phoneme, viseme, kind;
}
[Serializable] public sealed class LipSyncPlan
{
    public string language, source, timing;
    public LipSyncUnit[] units;
}
public sealed class LipSyncTimeline
{
    public int samplesPerFrame;
    public float[] intensity;
    public int[] unitIndex;
    public LipSyncUnit[] units;
    public string[] shapes;
    public float[] shapeScale;
}
/// <summary>Replaceable boundary for future forced-alignment/native timing.</summary>
public interface ILipSyncTimelineProvider
{
    LipSyncTimeline Build(AudioClip clip, LipSyncPlan plan, float silenceFloor, float relativeSilence, float maximumIntensity);
}
/// <summary>V1 approximate mora-weighted allocation across audible frames, not forced alignment.</summary>
public sealed class ApproximateMoraTimelineProvider : ILipSyncTimelineProvider
{
    public LipSyncTimeline Build(AudioClip clip, LipSyncPlan plan, float silenceFloor, float relativeSilence, float maximumIntensity)
    {
        if(clip==null || plan?.units==null || plan.units.Length==0)return null;
        int hop=Mathf.Max(1,clip.frequency/100), count=(clip.samples+hop-1)/hop;
        var samples=new float[clip.samples*clip.channels];if(!clip.GetData(samples,0))return null;
        var rms=new float[count];float peak=0;
        for(int frame=0;frame<count;frame++)
        {
            int first=frame*hop,last=Mathf.Min(clip.samples,first+hop*2);double sum=0;
            for(int s=first;s<last;s++)for(int ch=0;ch<clip.channels;ch++){float v=samples[s*clip.channels+ch];sum+=v*v;}
            rms[frame]=(float)Math.Sqrt(sum/Math.Max(1,(last-first)*clip.channels));peak=Mathf.Max(peak,rms[frame]);
        }
        float threshold=Mathf.Max(silenceFloor,peak*relativeSilence), reference=Mathf.Max(threshold*2,peak*.6f);
        int voiced=0;for(int i=0;i<count;i++)if(rms[i]>threshold)voiced++;
        var t=new LipSyncTimeline{samplesPerFrame=hop,intensity=new float[count],unitIndex=new int[count],units=plan.units,shapes=new string[plan.units.Length],shapeScale=new float[plan.units.Length]};
        var weights=new float[plan.units.Length];float total=0;
        for(int i=0;i<weights.Length;i++)
        {
            var unit=plan.units[i];bool pause=unit.phoneme=="pau"||unit.phoneme=="sil";
            weights[i]=pause?0:unit.kind=="consonant"?.25f:unit.phoneme=="cl"?.35f:unit.phoneme=="N"?.65f:1f;total+=weights[i];
            string shape=unit.viseme;float scale=1;
            if(shape=="anticipate"||shape=="neutral")
            {
                shape="closed";
                for(int j=i+1;j<plan.units.Length && j<=i+3;j++){if(plan.units[j].kind=="closure")break;if(plan.units[j].kind=="vowel"){shape=plan.units[j].viseme;break;}}
                if(shape=="closed"&&i>0)shape=t.shapes[i-1];
                scale=unit.viseme=="neutral"?.28f:.7f;
            }
            t.shapes[i]=shape;t.shapeScale[i]=scale;
        }
        int position=0,unitIndex=0;float boundary=weights[0];
        for(int i=0;i<count;i++)
        {
            if(rms[i]<=threshold||voiced==0||total==0){t.unitIndex[i]=-1;continue;}
            float target=(position+.5f)/voiced*total;
            while(unitIndex<weights.Length-1 && target>=boundary){unitIndex++;boundary+=weights[unitIndex];}
            t.unitIndex[i]=unitIndex;
            float normalized=Mathf.Clamp01((rms[i]-threshold)/(reference-threshold));
            t.intensity[i]=Mathf.Lerp(.25f,maximumIntensity,Mathf.Sqrt(normalized))*t.shapeScale[unitIndex];position++;
        }
        return t;
    }
}
