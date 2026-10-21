"""Fixed synthetic PCM. Never a model voice quality or physical-mic fixture."""
import io,math,struct,wave,subprocess
from pathlib import Path

def pcm(seconds=3,rate=32000):
    return b''.join(struct.pack('<h',int(1800*math.sin(2*math.pi*330*i/rate))) for i in range(int(rate*seconds)))

def prepare_mp3(output,ffmpeg):
    target=Path(output);target.parent.mkdir(parents=True,exist_ok=True)
    wav=target.with_suffix('.wav')
    with wave.open(str(wav),'wb') as f:f.setnchannels(1);f.setsampwidth(2);f.setframerate(32000);f.writeframes(pcm())
    subprocess.run([str(ffmpeg),'-y','-loglevel','error','-i',str(wav),str(target)],check=True,timeout=20)
    return target
