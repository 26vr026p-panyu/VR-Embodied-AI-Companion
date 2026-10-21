using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Unity.Profiling;

/// <summary>Opt-in player measurements, never treats Editor/PCVR as Quest standalone evidence.</summary>
public sealed class QuestPerformanceProbe : MonoBehaviour
{
    public string scenario="Q1 Idle";
    public float maxDurationSeconds=600;
    const int Capacity=65536;
    readonly List<float> frames=new List<float>(Capacity),main=new List<float>(Capacity),gpu=new List<float>(Capacity);
    ProfilerRecorder mainThread,gc;
    readonly FrameTiming[] timing=new FrameTiming[1];
    double start,nextMemory;
    long gcTotal,gcMax,memoryStart,memoryMax;
    bool saved;
    public void BeginScenario(string name){Save();scenario=name;frames.Clear();main.Clear();gpu.Clear();gcTotal=gcMax=0;memoryStart=memoryMax=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();start=Time.realtimeSinceStartupAsDouble;saved=false;}
    void OnEnable(){mainThread=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);BeginScenario(scenario);}
    void Update()
    {
        if(saved)return;
        if(frames.Count>=Capacity||Time.realtimeSinceStartupAsDouble-start>=maxDurationSeconds){Save();return;}
        frames.Add(Time.unscaledDeltaTime*1000);
        if(mainThread.Valid&&mainThread.Count>0&&mainThread.LastValue>0)main.Add(mainThread.LastValue*1e-6f);
        if(gc.Valid&&gc.Count>0){gcTotal+=gc.LastValue;gcMax=Math.Max(gcMax,gc.LastValue);}
        FrameTimingManager.CaptureFrameTimings();
        if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].gpuFrameTime>0)gpu.Add((float)timing[0].gpuFrameTime);
        if(Time.realtimeSinceStartupAsDouble>=nextMemory){memoryMax=Math.Max(memoryMax,UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());nextMemory=Time.realtimeSinceStartupAsDouble+1;}
    }
    static Newtonsoft.Json.Linq.JToken Stats(List<float> values)
    {
        if(values.Count==0)return Newtonsoft.Json.Linq.JValue.CreateNull();
        var sorted=values.ToArray();Array.Sort(sorted);
        return new Newtonsoft.Json.Linq.JObject{["samples"]=sorted.Length,["median_ms"]=sorted[sorted.Length/2],["p95_ms"]=sorted[Math.Min(sorted.Length-1,(int)Math.Ceiling(sorted.Length*.95)-1)],["max_ms"]=sorted[sorted.Length-1]};
    }
    public void Save()
    {
        if(saved||frames.Count==0)return;saved=true;
        var row=new Newtonsoft.Json.Linq.JObject{["scenario"]=scenario,["platform"]=Application.platform.ToString(),["standalone_quest_evidence"]=Application.platform==RuntimePlatform.Android&&UnityEngine.XR.XRSettings.isDeviceActive,["duration_seconds"]=Time.realtimeSinceStartupAsDouble-start,["frame"]=Stats(frames),["main_thread"]=Stats(main),["gpu"]=Stats(gpu),["gc_counter_available"]=gc.Valid,["gc_alloc_mean_bytes_per_frame"]=gc.Valid?(Newtonsoft.Json.Linq.JToken)new Newtonsoft.Json.Linq.JValue((double)gcTotal/frames.Count):Newtonsoft.Json.Linq.JValue.CreateNull(),["gc_alloc_max_bytes_per_frame"]=gc.Valid?(Newtonsoft.Json.Linq.JToken)new Newtonsoft.Json.Linq.JValue(gcMax):Newtonsoft.Json.Linq.JValue.CreateNull(),["allocated_memory_start_bytes"]=memoryStart,["allocated_memory_peak_bytes"]=memoryMax,["thermal"]=Newtonsoft.Json.Linq.JValue.CreateNull()};
        try{var folder=Path.Combine(Application.persistentDataPath,"QuestValidation");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"performance.jsonl"),row.ToString(Newtonsoft.Json.Formatting.None)+"\n");}catch(Exception e){Debug.LogWarning("[QuestPerformance] Write failed: "+e.GetType().Name);}
    }
    void OnApplicationPause(bool pause){if(pause)Save();}
    void OnDisable(){Save();mainThread.Dispose();gc.Dispose();}
}
