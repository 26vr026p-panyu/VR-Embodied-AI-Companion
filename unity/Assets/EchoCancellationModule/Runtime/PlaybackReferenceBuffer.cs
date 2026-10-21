using System;
namespace CompanionVoice
{
    /// <summary>Bounded mono TTS reference addressed by output-DSP sample index. Missing/gapped data fails open.</summary>
    public sealed class PlaybackReferenceBuffer
    {
        readonly float[] pcm; readonly long[] tags; readonly object gate=new object();
        public readonly int SampleRate;
        long framesWritten;
        public long FramesWritten {get{lock(gate)return framesWritten;}}
        public PlaybackReferenceBuffer(int rate,int seconds=4)
        {
            if(rate<=0||seconds<1)throw new ArgumentOutOfRangeException();SampleRate=rate;
            pcm=new float[rate*seconds];tags=new long[pcm.Length];for(int i=0;i<tags.Length;i++)tags[i]=long.MinValue;
        }
        public void Write(float[] interleaved,int channels,double dspStart)
        {
            if(channels<=0||double.IsNaN(dspStart)||dspStart<0)return;
            long start=(long)Math.Round(dspStart*SampleRate);
            lock(gate){int frames=interleaved.Length/channels;for(int i=0;i<frames;i++){float mono=0;for(int c=0;c<channels;c++)mono+=interleaved[i*channels+c];int slot=(int)((start+i)%pcm.Length);pcm[slot]=mono/channels;tags[slot]=start+i;}framesWritten+=frames;}
        }
        public bool ReadAligned(float[] destination,int count,int microphoneRate,double microphoneDspStart,double echoDelayMs)
        {
            if(microphoneRate<=0||echoDelayMs<0||double.IsNaN(microphoneDspStart))return false;
            double first=(microphoneDspStart-echoDelayMs*.001)*SampleRate;
            lock(gate){for(int i=0;i<count;i++){
                double at=first+(double)i*SampleRate/microphoneRate;long index=(long)Math.Floor(at);if(index<0)return false;
                int a=(int)(index%pcm.Length),b=(a+1)%pcm.Length;double blend=at-index;
                if(tags[a]!=index||(blend>1e-6&&tags[b]!=index+1))return false;
                destination[i]=(float)(pcm[a]+(blend>1e-6?(pcm[b]-pcm[a])*blend:0));
            }}return true;
        }
    }
}
