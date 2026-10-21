using UnityEngine;
namespace CompanionVoice
{
    /// <summary>Source-local observer: never alters output, owns no AudioSource/Listener and excludes other GameObjects' audio.</summary>
    [DisallowMultipleComponent]
    public sealed class PlaybackReferenceTap:MonoBehaviour
    {
        volatile PlaybackReferenceBuffer buffer;
        AudioSource source;
        public PlaybackReferenceBuffer Buffer => buffer;
        public int OutputRate {get;private set;}
        public int LastChannels {get;private set;}
        double lastDspTime; public double LastDspTime => System.Threading.Volatile.Read(ref lastDspTime);
        public int ClipTimeSamples {get;private set;}
        public bool IsPlaying {get;private set;}
        void Awake(){Initialize();}
        void OnEnable(){AudioSettings.OnAudioConfigurationChanged+=ConfigurationChanged;}
        void OnDisable(){AudioSettings.OnAudioConfigurationChanged-=ConfigurationChanged;}
        void ConfigurationChanged(bool deviceChanged){Initialize();}
        void Initialize()
        {
            var sources=GetComponents<AudioSource>();
            if(sources.Length!=1){buffer=null;return;}
            source=sources[0];OutputRate=AudioSettings.outputSampleRate;
            buffer=OutputRate>0?new PlaybackReferenceBuffer(OutputRate):null;
        }
        void Update(){if(source){IsPlaying=source.isPlaying;ClipTimeSamples=source.clip!=null?source.timeSamples:0;}}
        void OnAudioFilterRead(float[] data,int channels)
        {
            // Audio thread: no object lookup, logging, file I/O or waveform modification.
            var target=buffer;if(target==null)return;
            double dsp=AudioSettings.dspTime;LastChannels=channels;System.Threading.Volatile.Write(ref lastDspTime,dsp);
            target.Write(data,channels,dsp);
        }
    }
}

