import asyncio
import time
import httpx
from .base import TTSProvider
from ..models import SpeechRequest, SpeechAudio, SpeechProviderError, TTSProviderCapabilities
from ..audio import audio_metadata
from turn_lifecycle import check_turn
from latency_trace import emit
from ..profiles import VoiceProfileBank

class GPTSoVITSProvider(TTSProvider):
    name = "gpt_sovits"
    capabilities = TTSProviderCapabilities(supports_streaming_audio=True)

    def __init__(self, config):
        self.config = config
        self.gate = asyncio.Lock()
        self.profiles = VoiceProfileBank(config["voices"])

    async def synthesize(self, request: SpeechRequest) -> SpeechAudio:
        voice_id = request.voice_id or self.config["default_voice_id"]
        voice = self.config["voices"].get(voice_id)
        if voice is None:
            raise SpeechProviderError("unknown_voice_id")
        profile, style_reason = self.profiles.select(voice_id, request.style)
        emit("voice_style_selected", requested_style=request.style, actual_style=profile.style, profile_id=profile.profile_id, status=profile.status, prompt_verified=profile.prompt_verified, style_fallback=style_reason is not None, style_fallback_reason=style_reason, operation=request.style, stage=profile.profile_id, reason=style_reason, fallback=style_reason is not None)
        headers = {}
        if request.trace_id and request.turn_id is not None:
            headers = {"X-Trace-Id": request.trace_id, "X-Turn-Id": str(request.turn_id)}
        payload = {"text": request.text, "text_lang": request.language,
                   "ref_audio_path": profile.reference_audio, "prompt_text": profile.prompt_text,
                   "prompt_lang": profile.prompt_language, "speed_factor": request.speed,
                   "profile_id": profile.profile_id, "media_type": "wav", "streaming_mode": False, "text_split_method": "cut5"}
        async with self.gate:
            check_turn("gpt_sovits_queue_finished")
            started = time.perf_counter()
            try:
                async with httpx.AsyncClient(timeout=httpx.Timeout(self.config.get("timeout_seconds", 240), connect=3), trust_env=False) as client:
                    response = await client.post(voice["service_url"].rstrip("/") + "/tts", json=payload, headers=headers)
            except httpx.TimeoutException as exc:
                raise SpeechProviderError("gpt_sovits_timeout") from exc
            except httpx.RequestError as exc:
                raise SpeechProviderError("gpt_sovits_unavailable") from exc
            check_turn("gpt_sovits_response")
            if response.status_code != 200:
                raise SpeechProviderError("gpt_sovits_http_" + str(response.status_code))
            if response.headers.get("X-Voice-Id") != voice_id:
                raise SpeechProviderError("gpt_sovits_voice_mismatch")
            if response.headers.get("X-TTS-Profile") != profile.profile_id:
                raise SpeechProviderError("gpt_sovits_profile_mismatch")
            rate, duration = audio_metadata(response.content, "wav")
            return SpeechAudio(response.content, "wav", rate, duration, self.name, voice_id,
                               request.language, voice["model"], (time.perf_counter() - started) * 1000,
                               style=profile.style, profile_id=profile.profile_id, style_status=profile.status)

    async def stream(self, request):
        import struct
        voice_id=request.voice_id or self.config['default_voice_id']
        voice=self.config['voices'].get(voice_id)
        if voice is None:raise SpeechProviderError('unknown_voice_id')
        profile,reason=self.profiles.select(voice_id,request.style)
        headers={'X-Trace-Id':request.trace_id,'X-Turn-Id':str(request.turn_id)} if request.trace_id else {}
        payload={'text':request.text,'text_lang':request.language,'profile_id':profile.profile_id,
            'ref_audio_path':profile.reference_audio,'prompt_text':profile.prompt_text,'prompt_lang':profile.prompt_language,
            'speed_factor':request.speed,'mode':self.config.get('streaming_mode',3)}
        queued=time.perf_counter();last_fragment=None;fragment_index=0
        async with self.gate:
            emit('tts_provider_queue_finished',queue_wait_ms=(time.perf_counter()-queued)*1000)
            check_turn('tts_stream_queue')
            try:
                async with httpx.AsyncClient(timeout=httpx.Timeout(self.config.get('timeout_seconds',240),connect=3),trust_env=False) as client:
                    async with client.stream('POST',voice['service_url'].rstrip('/')+'/tts/stream',json=payload,headers=headers) as response:
                        if response.status_code!=200:raise SpeechProviderError('stream_service_http_'+str(response.status_code))
                        if response.headers.get('X-Voice-Id')!=voice_id or response.headers.get('X-TTS-Profile')!=profile.profile_id:raise SpeechProviderError('stream_profile_mismatch')
                        buffer=bytearray();ended=False
                        async for data in response.aiter_bytes():
                            check_turn('tts_stream_chunk');buffer.extend(data)
                            while len(buffer)>=8:
                                rate,length=struct.unpack('<II',buffer[:8])
                                if length>2000000 or length%2:raise SpeechProviderError('stream_frame_capacity')
                                if len(buffer)<8+length:break
                                pcm=bytes(buffer[8:8+length]);del buffer[:8+length]
                                if rate==0:
                                    ended=True;break
                                if rate!=self.capabilities.sample_rate:raise SpeechProviderError('stream_rate_mismatch')
                                now=time.perf_counter();fragment_index+=1
                                emit('tts_provider_fragment',chunk_index=fragment_index,sample_count=len(pcm)//2,chunk_interval_ms=(now-last_fragment)*1000 if last_fragment is not None else None)
                                last_fragment=now
                                yield pcm
                            if ended:break
                        if not ended:raise SpeechProviderError('stream_missing_eos')
            except (httpx.RequestError,httpx.TimeoutException) as exc:raise SpeechProviderError('stream_provider_transport') from exc
