using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace CompanionVoice
{
    /// <summary>Independent capture transform before ring/VAD/ASR. Raw production default; fail-open on every fault.</summary>
    [DisallowMultipleComponent]
    public sealed class MicrophoneEchoPipeline:MonoBehaviour
    {
        public PlaybackReferenceTap referenceTap;
        public bool ExperimentalEnabled {get;private set;}
        public bool LastUsedClean {get;private set;}
        public string FallbackReason {get;private set;}
        public double RawRms {get;private set;} public double CleanRms {get;private set;} public double ReferenceRms {get;private set;}
        public double LastProcessingMilliseconds {get;private set;}
        public double EchoDelayMilliseconds {get;private set;}
        public bool LastReferenceAvailable {get;private set;}
        int rate;float[] reference,clean;string captureId,lastReason;
        IAudioEchoCanceller processor;
        bool started,echoActive;double nextMetrics;
        public void Configure(int sampleRate,int blockSize,TTSPlayer tts,string id,bool enabled,double delayMs,float gain)
        {
            rate=sampleRate;captureId=id;ExperimentalEnabled=enabled;EchoDelayMilliseconds=delayMs;
            reference=new float[blockSize];clean=new float[blockSize];started=false;lastReason=null;nextMetrics=0;
            try {
                if(tts&&tts.audioSource){referenceTap=tts.audioSource.GetComponent<PlaybackReferenceTap>();if(!referenceTap)referenceTap=tts.audioSource.gameObject.AddComponent<PlaybackReferenceTap>();}
                processor=enabled?new ExperimentalCalibratedEchoSuppressor(gain):null;
                LatencyTrace.CaptureMark(id,"aec_initialized",new JObject{["aec_enabled"]=enabled,["implementation"]=enabled?processor.Implementation:"RAW PRODUCTION DEFAULT",["mic_rate"]=rate,["reference_rate"]=referenceTap?referenceTap.OutputRate:0,["block_samples"]=blockSize,["block_ms"]=1000.0*blockSize/rate,["echo_delay_ms"]=delayMs,["delay_source"]="manual uncalibrated estimate",["algorithmic_delay_ms"]=0});
                if(!enabled)Fallback("experimental_disabled");
            } catch(Exception e){ExperimentalEnabled=false;processor=null;LatencyTrace.CaptureMark(id,"aec_processing_failed",new JObject{["stage"]="initialize",["reason"]=e.GetType().Name});Fallback("initialization_failed");}
        }
        void Fallback(string reason)
        {
            FallbackReason=reason;
            if(reason==lastReason)return;lastReason=reason;
            LatencyTrace.CaptureMark(captureId,"aec_fallback_raw",new JObject{["aec_enabled"]=false,["aec_fallback_reason"]=reason});
        }
        public float[] Process(float[] raw,int count,long microphoneStart,double microphoneDspStart)
        {
            long start=System.Diagnostics.Stopwatch.GetTimestamp();LastUsedClean=false;RawRms=Rms(raw,count);CleanRms=RawRms;ReferenceRms=0;
            try {
                var buffer=referenceTap?referenceTap.Buffer:null;
                LastReferenceAvailable=buffer!=null&&buffer.ReadAligned(reference,count,rate,microphoneDspStart,EchoDelayMilliseconds);
                if(LastReferenceAvailable)ReferenceRms=Rms(reference,count);
                bool active=ReferenceRms>.00001;
                if(active&&!echoActive)LatencyTrace.CaptureMark(captureId,"echo_reference_active",new JObject{["reference_rate"]=buffer.SampleRate,["reference_source"]="TTS AudioSource OnAudioFilterRead",["reference_dsp_start"]=microphoneDspStart-EchoDelayMilliseconds*.001});echoActive=active;
                if(ExperimentalEnabled&&processor!=null&&LastReferenceAvailable){
                    if(!started){started=true;LatencyTrace.CaptureMark(captureId,"aec_processing_started",new JObject{["implementation"]=processor.Implementation});}
                    var timing=new EchoTimingContext{MicrophoneStartSample=microphoneStart,MicrophoneRate=rate,ReferenceRate=buffer.SampleRate,MicrophoneDspStart=microphoneDspStart,EchoDelayMilliseconds=EchoDelayMilliseconds,ReferenceAvailable=true};
                    if(processor.Process(raw,reference,clean,count,timing)){
                        bool valid=true;for(int i=0;i<count;i++)if(float.IsNaN(clean[i])||float.IsInfinity(clean[i])){valid=false;break;}
                        if(valid){LastUsedClean=true;CleanRms=Rms(clean,count);FallbackReason=null;lastReason=null;}else Fallback("invalid_clean_output");
                    }else Fallback("processor_rejected_block");
                }else if(ExperimentalEnabled)Fallback(LastReferenceAvailable?"processor_unavailable":"reference_unavailable");
            }catch(Exception e){ExperimentalEnabled=false;processor=null;LatencyTrace.CaptureMark(captureId,"aec_processing_failed",new JObject{["reason"]=e.GetType().Name});Fallback("processing_failed");}
            LastProcessingMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            double now=LatencyTrace.Now;
            if(now>=nextMetrics){nextMetrics=now+1000;LatencyTrace.CaptureMark(captureId,"aec_metrics",new JObject{["aec_enabled"]=LastUsedClean,["aec_fallback_reason"]=FallbackReason,["raw_mic_rms"]=RawRms,["clean_mic_rms"]=CleanRms,["reference_rms"]=ReferenceRms,["echo_delay_ms"]=EchoDelayMilliseconds,["echo_reduction_db"]=LastUsedClean&&ReferenceRms>0?(JToken)(20*Math.Log10(Math.Max(1e-12,RawRms)/Math.Max(1e-12,CleanRms))):JValue.CreateNull(),["reduction_basis"]="total signal RMS ratio; NOT isolated echo ERLE during double-talk",["mic_start_sample"]=microphoneStart,["mic_dsp_start_estimate"]=microphoneDspStart,["mic_time_basis"]="DSP receipt time minus pending captured PCM; hardware latency unknown",["processing_ms"]=LastProcessingMilliseconds});}
            return LastUsedClean?clean:raw;
        }
        public static double Rms(float[] pcm,int count){double total=0;for(int i=0;i<count;i++)total+=pcm[i]*pcm[i];return Math.Sqrt(total/count);}
    }
}
