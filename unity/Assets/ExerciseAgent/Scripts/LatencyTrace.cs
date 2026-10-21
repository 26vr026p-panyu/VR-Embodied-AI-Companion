using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Observation only. Trace turn IDs are independent of the frozen Agent turn counter.</summary>
public static class LatencyTrace
{
    public sealed class Turn
    {
        public string Id, InputKind;
        public long Number, AgentTurn;
        public bool Ended;
        internal readonly Dictionary<string, double> Times = new Dictionary<string, double>();
    }
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly string ClockId = Guid.NewGuid().ToString("N");
    static readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();
    static readonly AutoResetEvent Wake = new AutoResetEvent(false);
    static readonly Dictionary<long, Turn> AgentTurns = new Dictionary<long, Turn>();
    static long counter;
    static Thread writer;
    static string path;
    static double listeningAt = -1;
    public static string LogPath => path;
    public static double Now => Clock.Elapsed.TotalMilliseconds;
    public static readonly string[] UnimplementedEvents = { "tts_first_chunk", "tts_chunk_received" };

    public static void ListeningStarted() { listeningAt = Now; }
    public static Turn Begin(string inputKind)
    {
        var n = ++counter;
        var t = new Turn { Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + n.ToString("D4") + "-" + Guid.NewGuid().ToString("N").Substring(0,8), Number=n, InputKind=inputKind };
        Mark(t,"turn_started");
        if (inputKind == "microphone" && listeningAt >= 0) MarkAt(t,"listening_started",listeningAt);
        return t;
    }
    public static void Bind(Turn t,long agentTurn)
    {
        if(t==null)return;
        t.AgentTurn=agentTurn;
        // One agent exists in the current project. Do not change its ID allocation.
        AgentTurns[agentTurn]=t;
        if(AgentTurns.Count>128)AgentTurns.Remove(agentTurn-128);
        Mark(t,"agent_turn_bound");
    }
    public static Turn ForAgent(long n) { AgentTurns.TryGetValue(n,out var t); return t; }
    public static void Headers(UnityWebRequest r,Turn t)
    {
        if(t==null)return;
        try { r.SetRequestHeader("X-Trace-Id",t.Id);r.SetRequestHeader("X-Turn-Id",t.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)); } catch { }
    }
    public static void Mark(Turn t,string e,JObject metadata=null) => MarkAt(t,e,Now,metadata);
    public static void MarkAt(Turn t,string e,double at,JObject metadata=null)
    {
        if(t==null)return;
        try
        {
            t.Times[e]=at;
            var row=new JObject { ["trace_id"]=t.Id,["turn_id"]=t.Number,["agent_turn_id"]=t.AgentTurn,
                ["component"]="unity",["clock_id"]=ClockId,["event"]=e,["input_kind"]=t.InputKind,
                ["utc_ms"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-(long)Math.Max(0,Now-at),["mono_ms"]=at };
            if(metadata!=null)foreach(var p in metadata.Properties())row[p.Name]=p.Value;
            Enqueue(row);
        }
        catch { /* Logging failure cannot fail the voice turn. */ }
    }
    // Session events do not fabricate speech turns before VAD confirms speech.
    public static void CaptureMark(string captureId,string e,JObject metadata=null)
    {
        try {
            var row=new JObject{["component"]="unity",["clock_id"]=ClockId,["capture_id"]=captureId,["event"]=e,
                ["utc_ms"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),["mono_ms"]=Now};
            if(metadata!=null)foreach(var p in metadata.Properties())row[p.Name]=p.Value;
            Enqueue(row);
        } catch { }
    }
    static void Enqueue(JObject row)
    {
        if(writer==null)
        {
            path=Path.Combine(Application.persistentDataPath,"LatencyTraces","unity-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+ClockId.Substring(0,8)+".jsonl");
            writer=new Thread(WriteLoop){IsBackground=true,Name="latency-jsonl"};writer.Start();
        }
        if(Lines.Count<8192){Lines.Enqueue(row.ToString(Newtonsoft.Json.Formatting.None));Wake.Set();}
    }
    static void WriteLoop()
    {
        while(true)
        {
            Wake.WaitOne(500);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var w=new StreamWriter(path,true,new System.Text.UTF8Encoding(false)))
                    while(Lines.TryDequeue(out var line))w.WriteLine(line);
            }
            catch { while(Lines.TryDequeue(out _)){} }
        }
    }
    static JToken Delta(Turn t,string from,string to)
    {
        return t.Times.TryGetValue(from,out var a)&&t.Times.TryGetValue(to,out var b)&&b>=a ? (JToken)(b-a) : JValue.CreateNull();
    }
    public static JObject Summary(Turn t)
    {
        return new JObject {
            ["units"]="ms",["speech_end_basis"]=t.Times.ContainsKey("speech_end_estimated")?"VAD RMS silence boundary estimate":"unavailable",
            ["speech_duration"]=Delta(t,"vad_speech_started","speech_end_estimated"),
            ["speech_end_to_vad_endpoint"]=Delta(t,"speech_end_estimated","vad_speech_ended"),
            ["vad_endpoint_to_asr_final"]=Delta(t,"vad_speech_ended","asr_final_received"),
            ["asr_request_to_final"]=Delta(t,"asr_request_started","asr_final_received"),
            ["asr_final_to_supplement_end"]=Delta(t,"asr_final_received","supplement_wait_finished"),
            ["supplement_end_to_llm_complete"]=Delta(t,"supplement_wait_finished","llm_response_received"),
            ["llm_complete_to_tts_complete"]=Delta(t,"llm_response_received","tts_response_received"),
            ["tts_complete_to_playback_start"]=Delta(t,"tts_response_received","playback_started"),
            ["speech_end_to_playback_start"]=Delta(t,"speech_end_estimated","playback_started"),
            ["asr_request_to_playback_start"]=Delta(t,"asr_request_started","playback_started"),
            ["full_turn_duration"]=Delta(t,"turn_started","turn_finished"),
            ["unavailable_events"]=new JArray(UnimplementedEvents),
            ["unobserved_events"]=new JArray(),
            ["speech_duration_basis"]="latest confirmed VAD speech segment; excludes start-confirmation lead-in"
        };
    }
    public static void End(Turn t,string outcome)
    {
        if(t==null||t.Ended)return;
        t.Ended=true;Mark(t,"turn_finished",new JObject{["outcome"]=outcome});
        var summary=Summary(t);summary["outcome"]=outcome;
        Mark(t,"turn_summary",summary);
        UnityEngine.Debug.Log("===== LATENCY TRACE =====\nTrace: "+t.Id+"\n"+summary.ToString()+"\n=========================");
    }
}
