import io
import wave
import av
from dataclasses import replace
from .models import SpeechAudio, SpeechProviderError

def audio_metadata(data: bytes, format: str) -> tuple[int, float]:
    try:
        if format == "wav":
            with wave.open(io.BytesIO(data)) as audio:
                if audio.getnframes() <= 0 or audio.getnchannels() not in (1, 2):
                    raise ValueError("empty or invalid WAV")
                return audio.getframerate(), audio.getnframes() / audio.getframerate()
        with av.open(io.BytesIO(data), format=format) as container:
            frames = list(container.decode(audio=0))
            if not frames:
                raise ValueError("no decoded audio")
            return frames[0].sample_rate, sum(frame.samples / frame.sample_rate for frame in frames)
    except Exception as exc:
        raise SpeechProviderError("invalid_audio") from exc

def unity_mp3(audio: SpeechAudio) -> SpeechAudio:
    """Keep the existing Unity MPEG decoder/API; WAV never leaks through /tts."""
    if audio.format == "mp3":
        return audio
    try:
        target = io.BytesIO()
        with av.open(io.BytesIO(audio.audio_bytes), format=audio.format) as source:
            with av.open(target, mode="w", format="mp3") as output:
                stream = output.add_stream("libmp3lame", rate=audio.sample_rate)
                stream.bit_rate = 128000
                stream.layout = "mono"
                resampler = av.AudioResampler(format="fltp", layout="mono", rate=audio.sample_rate)
                for frame in source.decode(audio=0):
                    for converted in resampler.resample(frame):
                        for packet in stream.encode(converted):
                            output.mux(packet)
                for converted in resampler.resample(None):
                    for packet in stream.encode(converted):
                        output.mux(packet)
                for packet in stream.encode(None):
                    output.mux(packet)
        data = target.getvalue()
        rate, duration = audio_metadata(data, "mp3")
        return replace(audio, audio_bytes=data, format="mp3", sample_rate=rate, duration=duration)
    except SpeechProviderError:
        raise
    except Exception as exc:
        raise SpeechProviderError("audio_normalization_failed") from exc
