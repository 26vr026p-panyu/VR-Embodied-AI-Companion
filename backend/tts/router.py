import json
from pathlib import Path
from fastapi import HTTPException
from latency_trace import emit, current_turn_key, span
from turn_lifecycle import check_turn
from reliability import FailureRecord, FailureCategory, monitor, Decision, Health
from .audio import unity_mp3
from .models import SpeechRequest, SpeechProviderError
from .providers.edge_provider import EdgeTTSProvider
from .providers.gpt_sovits_provider import GPTSoVITSProvider

class TTSProviderRouter:
    def __init__(self, config_path=None, providers=None):
        path = Path(config_path) if config_path else Path(__file__).with_name("config.json")
        self.config = json.loads(path.read_text(encoding="utf-8"))
        self.providers = providers or {"edge": EdgeTTSProvider(), "gpt_sovits": GPTSoVITSProvider(self.config)}

    async def synthesize(self, request: SpeechRequest):
        primary = self.config["primary_provider"]
        fallback = self.config.get("fallback_provider")
        reason = None
        reliability_failure = None
        reliability_decision = None
        fallback_began = None
        for name in dict.fromkeys(x for x in (primary, fallback) if x):
            check_turn("tts_router_before")
            emit("tts_provider_selected", requested_provider=primary, actual_provider=name,
                 voice_id=request.voice_id or self.config["default_voice_id"], fallback=name != primary,
                 fallback_reason=reason, requested_style=request.style)
            try:
                with span("tts_provider", provider=name):
                    audio = await self.providers[name].synthesize(request)
                    check_turn("tts_provider_after")
                    audio = unity_mp3(audio)
                check_turn("tts_normalized_after")
                emit("tts_audio_ready", provider=audio.provider, requested_provider=primary,
                     actual_provider=audio.provider, voice_id=audio.voice_id, model=audio.model,
                     synthesis_duration_ms=audio.synthesis_duration_ms,
                     audio_duration_ms=audio.duration * 1000,
                     rtf=audio.synthesis_duration_ms / (audio.duration * 1000),
                     fallback=name != primary, fallback_reason=reason, language=audio.language,
                     requested_style=request.style, actual_style=audio.style, profile_id=audio.profile_id, style_status=audio.style_status)
                monitor.set_health('TTS',Health.Healthy)
                if reliability_failure and name!=primary:
                    import time
                    monitor.event('fallback_succeeded',reliability_failure,reliability_decision,fallback_depth=2,added_latency_ms=(time.perf_counter()-fallback_began)*1000)
                return audio, name != primary, reason
            except HTTPException:
                raise  # Cancellation must never trigger an audible fallback.
            except SpeechProviderError as exc:
                check_turn("tts_fallback_guard")
                reason = str(exc)
                key=current_turn_key() or ('untraced',0)
                if name==primary and fallback and fallback!=primary:
                    import time
                    reliability_failure=FailureRecord(key[0],key[1],'complete_tts',FailureCategory.TTS,'provider_unavailable')
                    reliability_decision=monitor.failure(reliability_failure)
                    monitor.set_health('TTS',Health.Degraded)
                    monitor.event('fallback_started',reliability_failure,reliability_decision,fallback_depth=2)
                    fallback_began=time.perf_counter()
                elif reliability_failure:
                    monitor.event('fallback_failed',reliability_failure,reliability_decision,fallback_depth=2)
                    monitor.failure(FailureRecord(key[0],key[1],'edge_tts',FailureCategory.TTS,'fallback_provider_failed'),depth=2)
                    monitor.set_health('TTS',Health.Unavailable)
                emit("tts_provider_failed", provider=name, requested_provider=primary,
                     fallback_reason=reason)
        raise SpeechProviderError(reason or "tts_no_provider")

    def streaming_provider(self):
        provider=self.providers[self.config['primary_provider']]
        if not provider.capabilities.supports_streaming_audio:raise SpeechProviderError('streaming_not_supported')
        return provider

    async def stream(self, request):
        provider=self.streaming_provider()
        emit('tts_stream_provider_selected',provider=provider.name,voice_id=request.voice_id or self.config['default_voice_id'])
        async for chunk in provider.stream(request):yield chunk

def request_identity():
    key = current_turn_key()
    return key if key is not None else (None, None)

