import time
import edge_tts
from .base import TTSProvider
from ..models import SpeechRequest, SpeechAudio, SpeechProviderError
from ..audio import audio_metadata

class EdgeTTSProvider(TTSProvider):
    name = "edge"

    async def synthesize(self, request: SpeechRequest) -> SpeechAudio:
        # Preserve the existing two voices and default rate exactly.
        voice = "zh-CN-XiaoxiaoNeural" if request.language == "zh" else "ja-JP-NanamiNeural"
        rate = f"{round((request.speed - 1) * 100):+d}%"
        started = time.perf_counter()
        data = bytearray()
        try:
            async for chunk in edge_tts.Communicate(request.text, voice, rate=rate).stream():
                if chunk["type"] == "audio":
                    data.extend(chunk["data"])
        except Exception as exc:
            raise SpeechProviderError("edge_synthesis_failed") from exc
        sample_rate, duration = audio_metadata(bytes(data), "mp3")
        return SpeechAudio(bytes(data), "mp3", sample_rate, duration, self.name,
                           voice, request.language, "EdgeNeural", (time.perf_counter() - started) * 1000)
