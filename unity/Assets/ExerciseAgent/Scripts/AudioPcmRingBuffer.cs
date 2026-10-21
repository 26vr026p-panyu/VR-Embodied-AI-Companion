using System;

/// <summary>Single bounded PCM stream. Audio callback uses no Unity API or allocations.</summary>
public sealed class AudioPcmRingBuffer
{
 public static volatile bool ProfilingEnabled;
 // Opt-in bounded event storage is allocated at construction, never in PCMReaderCallback.
 public struct ProfileEvent {public long ticks,arrivalTicks;public string kind;public int before,after,requested,consumed;public bool eos,underrun;}
 readonly ProfileEvent[] profileEvents;int profileEventCount,profileDropped;
 void Record(string kind,int before,int requested=0,int consumed=0,long arrival=0,bool underrun=false){if(profileEvents==null)return;if(profileEventCount==profileEvents.Length){profileDropped++;return;}profileEvents[profileEventCount++]=new ProfileEvent{ticks=System.Diagnostics.Stopwatch.GetTimestamp(),arrivalTicks=arrival,kind=kind,before=before,after=count,requested=requested,consumed=consumed,eos=eos,underrun=underrun};}
 public ProfileEvent[] ReadEvents(out int dropped){lock(gate){var copy=new ProfileEvent[profileEventCount];Array.Copy(profileEvents??Array.Empty<ProfileEvent>(),copy,profileEventCount);dropped=profileDropped;return copy;}}
 public struct ProfileSample { public long callbacks, requested, consumed, callbackTicks, waitTicks; public int underruns, depth, beforeUnderrun; }
 long profileCallbacks,profileRequested,profileConsumed,profileCallbackTicks,profileWaitTicks;int profileBeforeUnderrun;
 public ProfileSample ReadProfile(){lock(gate)return new ProfileSample{callbacks=profileCallbacks,requested=profileRequested,consumed=profileConsumed,callbackTicks=profileCallbackTicks,waitTicks=profileWaitTicks,underruns=Underruns,depth=count,beforeUnderrun=profileBeforeUnderrun};}
 readonly object gate=new object(); readonly float[] samples;
  readonly float[] rms=new float[8192];readonly long[] keys=new long[8192];
 int read,write,count;long rendered,lastDataEnd,totalReceived;bool armed,cancelled,eos;
 double energy;int envelopeCount;readonly int hop;
 public int Underruns {get;private set;} public int MinDepth {get;private set;}=int.MaxValue;public int MaxDepth {get;private set;}
 public AudioPcmRingBuffer(int rate,int seconds=8){samples=new float[rate*seconds];profileEvents=ProfilingEnabled?new ProfileEvent[16384]:null;hop=rate/100;for(int i=0;i<keys.Length;i++)keys[i]=-1;}
 public int Capacity=>samples.Length;
 public int Buffered {get{lock(gate)return count;}}
 public int Free {get{lock(gate)return samples.Length-count;}}
 public long Received {get{lock(gate)return totalReceived;}}
 public long LastDataEnd {get{lock(gate)return lastDataEnd;}}
 public bool IsEnded {get{lock(gate)return eos;}}
 public bool Drained {get{lock(gate)return eos&&count==0;}}
 public void Arm(){lock(gate){armed=true;Record("arm",count);}}
 public void End(){lock(gate){eos=true;Record("eos",count);}}
 public void Clear(){lock(gate){int before=count;cancelled=true;armed=false;count=0;Record("clear",before);}}
 public bool Write(float[] data,long arrivalTicks=0)
 {
  lock(gate){if(cancelled||data.Length>samples.Length-count)return false;
   int before=count;int n=Math.Min(data.Length,samples.Length-write);Array.Copy(data,0,samples,write,n);Array.Copy(data,n,samples,0,data.Length-n);
   write=(write+data.Length)%samples.Length;count+=data.Length;totalReceived+=data.Length;MaxDepth=Math.Max(MaxDepth,count);Record("append",before,consumed:data.Length,arrival:arrivalTicks);return true;}
 }
 public void Read(float[] output)
 {
  bool profile=ProfilingEnabled;long begin=profile?System.Diagnostics.Stopwatch.GetTimestamp():0;
  lock(gate)
  {
   long acquired=profile?System.Diagnostics.Stopwatch.GetTimestamp():0;
   if(profile)profileWaitTicks+=acquired-begin;
   Array.Clear(output,0,output.Length);if(!armed||cancelled)return;
   int before=count;int fresh=Math.Min(count,output.Length),n=Math.Min(fresh,samples.Length-read);
   Array.Copy(samples,read,output,0,n);Array.Copy(samples,0,output,n,fresh-n);read=(read+fresh)%samples.Length;count-=fresh;int taken=fresh;
   if(taken<output.Length&&!eos){Underruns++;if(profile)profileBeforeUnderrun=before;}
   Record("read",before,output.Length,taken,underrun:taken<output.Length&&!eos);
   if(profile){profileCallbacks++;profileRequested+=output.Length;profileConsumed+=taken;}
   MinDepth=Math.Min(MinDepth,count);if(taken>0)lastDataEnd=rendered+taken;
   for(int i=0;i<output.Length;i++)
   {
    energy+=output[i]*output[i];envelopeCount++;rendered++;
    if(envelopeCount==hop){long frame=(rendered-1)/hop;int slot=(int)(frame%rms.Length);rms[slot]=(float)Math.Sqrt(energy/hop);keys[slot]=frame;energy=0;envelopeCount=0;}
   }
   if(profile)profileCallbackTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-begin;
  }
 }
 public float Envelope(long sample){lock(gate){long frame=sample/hop;int slot=(int)(frame%rms.Length);return keys[slot]==frame?rms[slot]:0;}}
}





