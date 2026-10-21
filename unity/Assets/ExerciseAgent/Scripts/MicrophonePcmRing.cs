using System;

/// <summary>Bounded mono float PCM history. Indices are absolute within one capture session.</summary>
public sealed class MicrophonePcmRing
{
    readonly float[] pcm; readonly bool[] aiPlayback;
    public long EndSample { get; private set; }
    public long StartSample => Math.Max(0, EndSample-pcm.Length);
    public int Capacity => pcm.Length;
    public MicrophonePcmRing(int capacity){if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));pcm=new float[capacity];aiPlayback=new bool[capacity];}
    public void Append(float[] data,int count,bool ai)
    {
        for(int i=0;i<count;i++){int slot=(int)(EndSample%pcm.Length);pcm[slot]=data[i];aiPlayback[slot]=ai;EndSample++;}
    }
    // A new confirmed interrupting utterance may use retained PCM from before old-turn cancellation.
    // This never resumes the cancelled turn; CopyRecent still clamps to this ring and requested length.
    public int CopyForInterruptingSpeech(int requested,float[] destination,out long start,out bool overlap)
        =>CopyRecent(requested,StartSample,destination,out start,out overlap);
    public int CopyRecent(int requested,long minimumIndex,float[] destination,out long start,out bool overlap)
    {
        start=Math.Max(Math.Max(StartSample,minimumIndex),EndSample-Math.Min(requested,destination.Length));overlap=false;
        int count=(int)Math.Max(0,EndSample-start);
        for(int i=0;i<count;i++){int slot=(int)((start+i)%pcm.Length);destination[i]=pcm[slot];overlap|=aiPlayback[slot];}
        return count;
    }
}

public enum SpeechEndpointState { Listening, SpeechActive, PossibleEndpoint, EndpointConfirmed }
/// <summary>VAD silence evidence only. Soft evidence never finalizes speech.</summary>
public sealed class SpeechEndpointDetector
{
    public SpeechEndpointState State { get; private set; }=SpeechEndpointState.Listening;
    public double SilenceSeconds { get; private set; }
    public bool Candidate { get; private set; }
    public bool Confirmed { get; private set; }
    public bool SoftCandidate { get; private set; }
    public bool SoftCancelled { get; private set; }
    public bool SoftPending { get; private set; }
    public string Evidence => "selected_PCM_RMS_silence";
    public void Reset(){State=SpeechEndpointState.Listening;SilenceSeconds=0;Candidate=Confirmed=SoftCandidate=SoftCancelled=SoftPending=false;}
    public void BeginSpeech(){Reset();State=SpeechEndpointState.SpeechActive;}
    public void Observe(float rms,float silenceThreshold,double elapsedSeconds,double endpointSeconds,bool activeTurn)
        =>Observe(rms,silenceThreshold,elapsedSeconds,Math.Min(.35,endpointSeconds),endpointSeconds,activeTurn);
    public void Observe(float rms,float silenceThreshold,double elapsedSeconds,double softSeconds,double hardSeconds,bool activeTurn)
    {
        Candidate=Confirmed=SoftCandidate=SoftCancelled=false;
        if(!activeTurn){Reset();return;}
        if(State==SpeechEndpointState.EndpointConfirmed)return;
        if(rms>silenceThreshold){SoftCancelled=SoftPending;SoftPending=false;State=SpeechEndpointState.SpeechActive;SilenceSeconds=0;return;}
        Candidate=State!=SpeechEndpointState.PossibleEndpoint;
        State=SpeechEndpointState.PossibleEndpoint;SilenceSeconds+=Math.Max(0,elapsedSeconds);
        if(!SoftPending&&SilenceSeconds>=Math.Min(softSeconds,hardSeconds)){SoftPending=true;SoftCandidate=true;}
        if(SilenceSeconds>=hardSeconds){State=SpeechEndpointState.EndpointConfirmed;Confirmed=true;}
    }
}
/// <summary>Short supplement horizon starts at audio finalization; ASR time consumes it.</summary>
public sealed class ConditionalSupplementWindow
{
    public double FinalizedAtMilliseconds {get;private set;}
    public double DeadlineMilliseconds {get;private set;}
    public bool Open {get;private set;}
    public void Begin(double now,double windowSeconds){FinalizedAtMilliseconds=now;DeadlineMilliseconds=now+Math.Max(0,windowSeconds)*1000;Open=true;}
    public bool CanResume(double candidateMilliseconds,double nowMilliseconds,double confirmationAllowanceMilliseconds)
        =>Open&&candidateMilliseconds>=FinalizedAtMilliseconds&&candidateMilliseconds<=DeadlineMilliseconds&&nowMilliseconds<=DeadlineMilliseconds+confirmationAllowanceMilliseconds;
    public double RemainingMilliseconds(double now)=>Open?Math.Max(0,DeadlineMilliseconds-now):0;
    public void Close(){Open=false;}
}
