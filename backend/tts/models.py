from dataclasses import dataclass

@dataclass(frozen=True)
class SpeechRequest:
    text: str
    language: str = "ja"
    voice_id: str = ""
    style: str = "neutral"
    speed: float = 1.0
    trace_id: str | None = None
    turn_id: int | None = None

@dataclass(frozen=True)
class SpeechAudio:
    audio_bytes: bytes
    format: str
    sample_rate: int
    duration: float
    provider: str
    voice_id: str
    language: str
    model: str
    synthesis_duration_ms: float = 0.0
    style: str = "neutral"
    profile_id: str = "neutral"
    style_status: str = "production"
    # Chunk delivery and viseme timelines are intentionally not implemented.

class SpeechProviderError(Exception):
    """Safe diagnostic code; never includes user text, credentials or provider payloads."""

@dataclass(frozen=True)
class TTSProviderCapabilities:
    supports_complete_audio: bool = True
    supports_streaming_audio: bool = False
    output_format: str = "pcm16"
    sample_rate: int = 32000
    channels: int = 1
