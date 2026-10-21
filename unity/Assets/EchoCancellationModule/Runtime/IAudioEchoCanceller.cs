using System;
namespace CompanionVoice
{
    public struct EchoTimingContext
    {
        public long MicrophoneStartSample;
        public int MicrophoneRate, ReferenceRate;
        public double MicrophoneDspStart, EchoDelayMilliseconds;
        public bool ReferenceAvailable;
    }
    public interface IAudioEchoCanceller
    {
        string Implementation { get; }
        double AlgorithmicDelayMilliseconds { get; }
        bool Process(float[] microphone,float[] alignedReference,float[] cleaned,int count,EchoTimingContext timing);
    }
    /// <summary>EXPERIMENTAL ECHO SUPPRESSION: fixed calibrated linear subtraction.
    /// No adaptive filter, double-talk detector, nonlinear echo handling or production AEC claim.</summary>
    public sealed class ExperimentalCalibratedEchoSuppressor : IAudioEchoCanceller
    {
        readonly float gain;
        public string Implementation => "EXPERIMENTAL ECHO SUPPRESSION";
        public double AlgorithmicDelayMilliseconds => 0;
        public ExperimentalCalibratedEchoSuppressor(float calibratedGain)
        {if(float.IsNaN(calibratedGain)||float.IsInfinity(calibratedGain)||calibratedGain<0||calibratedGain>2)throw new ArgumentOutOfRangeException(nameof(calibratedGain));gain=calibratedGain;}
        public bool Process(float[] microphone,float[] alignedReference,float[] cleaned,int count,EchoTimingContext timing)
        {
            if(!timing.ReferenceAvailable)return false;
            for(int i=0;i<count;i++)cleaned[i]=microphone[i]-gain*alignedReference[i];
            return true;
        }
    }
}
