using UnityEngine;
using CompanionAvatar;
using Newtonsoft.Json.Linq;

/// <summary>Subscribes to the existing adapter event; never submits or changes behavior.</summary>
public sealed class LatencyBehaviorObserver : MonoBehaviour
{
    AgentBehaviorAdapter adapter;
    float nextScan;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if(FindAnyObjectByType<LatencyBehaviorObserver>()==null)
            new GameObject("Latency Observation (runtime only)").AddComponent<LatencyBehaviorObserver>();
    }
    void Update()
    {
        if(adapter!=null||Time.unscaledTime<nextScan)return;
        nextScan=Time.unscaledTime+1;
        adapter=FindAnyObjectByType<AgentBehaviorAdapter>();
        if(adapter!=null)adapter.OnBehaviorSubmitted+=Submitted;
    }
    void Submitted(long id,string stage,BehaviorDecision decision)
    {
        LatencyTrace.Mark(LatencyTrace.ForAgent(id),"behavior_submitted",new JObject{
            ["stage"]=stage,["accepted"]=decision.AnyAccepted });
    }
    void OnDisable(){if(adapter!=null)adapter.OnBehaviorSubmitted-=Submitted;adapter=null;}
}
