from abc import ABC, abstractmethod
from ..models import SpeechRequest, SpeechAudio, TTSProviderCapabilities, SpeechProviderError

class TTSProvider(ABC):
    name: str
    capabilities = TTSProviderCapabilities()

    async def stream(self, request):
        raise SpeechProviderError("streaming_not_supported")
        yield b""


    @abstractmethod
    async def synthesize(self, request: SpeechRequest) -> SpeechAudio:
        raise NotImplementedError
