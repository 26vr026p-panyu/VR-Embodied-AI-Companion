"""Preserved ASR transport with native-online or incremental-Whisper providers."""
import asyncio,json,re,struct,time,uuid,unicodedata,os
from pathlib import Path
from types import SimpleNamespace
from asr_providers import FasterWhisperProvider,WhisperCppProvider,SherpaOnlineRemoteProvider,AsrCapabilities
from asr_language_policy import normalize_mode,reply_language
from collections import deque
import numpy as np
from fastapi import WebSocket,WebSocketDisconnect
from starlette.concurrency import run_in_threadpool
from latency_trace import emit,websocket_context
from turn_lifecycle import registry
INITIAL_PROMPT='Unity, VR, Quest, Quest 2, Wattbike, RPE, Visual Gain, LLM, DeepSeek, AI Agent, cadence, power, heart rate, 人工知能, 実験, 研究, 運動, ワットバイク, ケイデンス'
def native_reply_language(text, hint=None):
    """Downstream routing heuristic, NOT recognizer language detection/confidence.

    Kana is Japanese evidence; Han-only text is ambiguous and uses an explicit
    JA hint when present, otherwise Chinese. Latin-only uses hint or English.
    Model decoding remains automatic multilingual.
    """
    if re.search(r'[\u3040-\u30ff]', text): return 'ja'
    if re.search(r'[\u3400-\u4dbf\u4e00-\u9fff]', text): return 'ja' if hint=='ja' else 'zh'
    return hint or 'en'

HEADER=struct.Struct('<IqI')
class ProtocolError(ValueError):pass
class StablePrefix:
    def __init__(self,confirmations=3):self.history=deque(maxlen=confirmations);self.stable=''
    def observe(self,text):
        self.history.append(text)
        if len(self.history)<self.history.maxlen:return self.stable
        common=self.history[0]
        for item in self.history:
            n=0
            while n<min(len(common),len(item)) and common[n]==item[n]:n+=1
            common=common[:n]
        while common and any(len(x)>len(common) and unicodedata.combining(x[len(common)]) for x in self.history):common=common[:-1]
        if common.startswith(self.stable):self.stable=common
        return self.stable
class PcmSequence:
    def __init__(self,rate,start):self.rate=rate;self.next_index=start;self.sequence=0;self.samples=0;self.parts=[]
    def accept(self,packet):
        if len(packet)<HEADER.size:raise ProtocolError('short_chunk_header')
        sequence,start,count=HEADER.unpack_from(packet)
        if sequence!=self.sequence:raise ProtocolError('duplicate_or_missing_or_out_of_order_chunk')
        if start!=self.next_index:raise ProtocolError('sample_index_gap_or_overlap')
        if not 0<count<=self.rate//2 or len(packet)!=HEADER.size+count*2:raise ProtocolError('invalid_chunk_length')
        if self.samples+count>self.rate*120:raise ProtocolError('audio_capacity_exceeded')
        self.parts.append(np.frombuffer(packet,dtype='<i2',offset=16).astype(np.float32)/32768)
        self.samples+=count;self.sequence+=1;self.next_index+=count
    def snapshot(self):return np.concatenate(self.parts) if self.parts else np.zeros(0,dtype=np.float32)
def join_overlap(prefix,text):
    for n in range(min(len(prefix),len(text),128),0,-1):
        if prefix[-n:]==text[:n]:return prefix+text[n:]
    return prefix+text

def install_streaming_asr(app,model):
    config_path=Path(__file__).with_name('asr_runtime_config.json')
    config=json.loads(config_path.read_text(encoding='utf-8')) if config_path.exists() else {}
    selected=os.environ.get('ASR_STREAMING_PROVIDER',config.get('provider','disabled'))
    if selected=='language_aware':
        language_providers={'ja':SherpaOnlineRemoteProvider(config.get('ja_service_url','http://127.0.0.1:8902'),capabilities=AsrCapabilities('native online Nemotron 3.5',True,True,False)),'zh':SherpaOnlineRemoteProvider(config.get('zh_service_url','http://127.0.0.1:8901')),'auto':SherpaOnlineRemoteProvider(config.get('auto_service_url','http://127.0.0.1:8901'))}
        provider=language_providers['ja']
    elif selected=='sherpa_online':provider=SherpaOnlineRemoteProvider(os.environ.get('SHERPA_ASR_URL',config.get('service_url','http://127.0.0.1:8901')))
    elif selected=='whisper_cpp':provider=WhisperCppProvider(os.environ.get('WHISPER_CPP_URL','http://127.0.0.1:8091/inference'))
    elif selected=='faster_whisper_compatibility':provider=FasterWhisperProvider(model)
    elif selected=='disabled':provider=None
    else:raise ValueError('unknown_asr_provider')
    inference_lock=asyncio.Lock()
    default_provider=provider
    @app.websocket('/asr/stream')
    async def stream(socket:WebSocket):
        provider=default_provider
        await socket.accept();key=None;invalid=False;closed=False;finalized=False
        wake=asyncio.Event();endpoint=asyncio.Event();send_lock=asyncio.Lock();worker=None;receive=None
        native_session=None;native_resampler=None
        durations=[];decode_samples=0;revision=0;prefix=StablePrefix();anchored='';previous_segments=[]
        stream_id=uuid.uuid4().hex;start_at=time.perf_counter()
        async def send(message):
            async with send_lock:
                if invalid or closed:return
                registry.check(key,'asr_stream_result')
                await socket.send_json(dict(message,trace_id=key[0],turn_id=key[1],stream_id=stream_id))
        try:
            start=await asyncio.wait_for(socket.receive_json(),10)
            if start.get('type')!='start':raise ProtocolError('start_required')
            trace_id,turn_id,capture=start.get('trace_id'),start.get('turn_id'),start.get('capture_id')
            rate,first=start.get('sample_rate'),start.get('start_sample_index')
            if not isinstance(trace_id,str) or not re.fullmatch(r'[A-Za-z0-9_-]{1,100}',trace_id):raise ProtocolError('invalid_trace_id')
            if type(turn_id)is not int or not 0<turn_id<10**15:raise ProtocolError('invalid_turn_id')
            if not isinstance(capture,str) or not 0<len(capture)<=100:raise ProtocolError('invalid_capture_id')
            if rate not in (16000,24000,32000,44100,48000) or type(first)is not int or first<0:raise ProtocolError('invalid_sample_clock')
            language_mode=normalize_mode(start.get('language_mode',socket.headers.get('x-asr-language-mode',start.get('language_hint') or config.get('default_language_mode','ja'))))
            hint=None if language_mode=='auto' else language_mode
            if selected=='language_aware':provider=language_providers[language_mode]
            emit('asr_language_selected',language_mode=language_mode)
            if hint not in (None,'ja','zh','en'):raise ProtocolError('unsupported_language_hint')
            if provider is None:raise ProtocolError('realtime_provider_disabled')
            key=(trace_id,turn_id);pcm=PcmSequence(rate,first)
            def decode_native(audio,final):
                nonlocal native_session,native_resampler
                began=time.perf_counter()
                def guard():
                    state=registry.snapshot(key)
                    if invalid or (state and state['state']!='active'):
                        if native_session is not None:native_session.cancel()
                        raise RuntimeError('native_stream_cancelled')
                guard()
                if native_session is None:native_session=provider.open_stream(hint,identity={'trace_id':trace_id,'turn_id':turn_id,'capture_id':capture})
                guard()
                if rate!=16000:
                    import av
                    if native_resampler is None:native_resampler=av.AudioResampler(format='s16',layout='mono',rate=16000)
                    frames=[]
                    if len(audio):
                        source=np.clip(np.round(audio*32768),-32768,32767).astype(np.int16)
                        frame=av.AudioFrame.from_ndarray(source[np.newaxis,:],format='s16',layout='mono');frame.sample_rate=rate
                        frames+=native_resampler.resample(frame)
                    if final:frames+=native_resampler.resample(None)
                    audio=np.concatenate([f.to_ndarray().reshape(-1) for f in frames]).astype(np.float32)/32768 if frames else np.zeros(0,dtype=np.float32)
                text=native_session.accept(audio) if len(audio) else ''
                guard()
                if final:text=native_session.finish()
                guard()
                return text,SimpleNamespace(language=reply_language(language_mode,text)[0],language_probability=0.0),[],(time.perf_counter()-began)*1000
            def decode(audio,final):
                if rate!=16000:
                    import av
                    source=np.clip(np.round(audio*32768),-32768,32767).astype(np.int16)
                    frame=av.AudioFrame.from_ndarray(source[np.newaxis,:],format="s16",layout="mono");frame.sample_rate=rate
                    resampler=av.AudioResampler(format="s16",layout="mono",rate=16000)
                    frames=resampler.resample(frame)+resampler.resample(None)
                    audio=np.concatenate([f.to_ndarray().reshape(-1) for f in frames]).astype(np.float32)/32768
                began=time.perf_counter()
                text,info,segments=provider.decode(audio,beam_size=5,initial_prompt=INITIAL_PROMPT,language=hint)
                return text,info,segments,(time.perf_counter()-began)*1000
            async def decoder():
                nonlocal decode_samples,revision,anchored,previous_segments,finalized
                while not invalid:
                    await wake.wait();wake.clear()
                    if invalid:return
                    final=endpoint.is_set();snapshot=pcm.snapshot()
                    if not provider.capabilities.native_online and not final and (len(snapshot)<rate*.6 or len(snapshot)-decode_samples<rate*.5):continue
                    offset=decode_samples if provider.capabilities.native_online else (0 if final else max(0,len(snapshot)-rate*12))
                    if offset and not provider.capabilities.native_online:
                        past=[x for x in previous_segments if x[1]<=offset/rate]
                        if past:anchored=join_overlap(anchored,''.join(x[2] for x in past).strip());previous_segments=[x for x in previous_segments if x[1]>offset/rate]
                    decode_samples=len(snapshot);queued_at=time.perf_counter()
                    async with inference_lock:
                        registry.check(key,'asr_stream_decode_start')
                        text,info,segments,ms=await run_in_threadpool(decode_native if provider.capabilities.native_online else decode,snapshot[offset:],final)
                    durations.append(ms)
                    if invalid:emit('late_asr_final_discarded' if final else 'late_asr_partial_discarded',reason='stream_invalidated');return
                    state=registry.snapshot(key)
                    if state and state['state']!='active':
                        emit('late_asr_final_discarded' if final else 'late_asr_partial_discarded',reason='turn_'+state['state'])
                        return
                    registry.check(key,'asr_stream_decode_finished')
                    emit('asr_decode_finished',duration_ms=ms,audio_duration=len(snapshot[offset:])/rate,decode_queue_ms=(time.perf_counter()-queued_at)*1000-ms,backlog_audio_ms=max(0,pcm.samples-decode_samples)*1000/rate,final=final)
                    if final:
                        finalized=True;emit('asr_final_received',text_length=len(text),language=info.language,language_source='explicit_turn_language' if language_mode!='auto' else 'auto_metadata')
                        await send({'type':'final','text':text,'language':info.language,'language_probability':info.language_probability,'language_source':'explicit_turn_language' if language_mode!='auto' else 'auto_metadata','language_mode':language_mode,'revision':revision+1,'sample_count':pcm.samples,'decode_count':len(durations),'decode_mean_ms':sum(durations)/len(durations),'decode_peak_ms':max(durations),'decode_work_rtf':sum(durations)/(pcm.samples/rate*1000),'wall_rtf':(time.perf_counter()-start_at)/(pcm.samples/rate)})
                        return
                    previous_segments=[(offset/rate+s.start,offset/rate+s.end,s.text) for s in segments]
                    hypothesis=join_overlap(anchored,text)
                    if offset and not provider.capabilities.native_online:hypothesis=join_overlap(prefix.stable,hypothesis)
                    revision+=1;stable=prefix.observe(hypothesis)
                    await send({'type':'partial','text':hypothesis,'revision':revision});emit('asr_partial',text_length=len(hypothesis),revision=revision)
                    await send({'type':'stable','text':stable,'revision':revision});emit('asr_stable_prefix',text_length=len(stable),revision=revision)
            with websocket_context(trace_id,turn_id,stream_id):
                registry.register(key);registry.check(key,'asr_stream_start');emit('asr_stream_opened',capture_id=capture,sample_rate=rate)
                emit('asr_provider_selected',reason=selected)
                await send({'type':'opened','implementation':'native_online' if provider.capabilities.native_online else 'incremental','provider':selected,'language_hint_supported':provider.capabilities.language_hint,'language_mode':language_mode,'recognition_type':provider.capabilities.implementation,'decode_interval_ms':200 if provider.capabilities.native_online else 500})
                worker=asyncio.create_task(decoder());receive=asyncio.create_task(socket.receive())
                while True:
                    done,_=await asyncio.wait((receive,worker),return_when=asyncio.FIRST_COMPLETED)
                    if worker in done:await worker;break
                    message=await receive
                    if message['type']=='websocket.disconnect':invalid=True;emit('asr_stream_cancelled',reason='network_disconnect');break
                    registry.check(key,'asr_chunk_receive')
                    if message.get('bytes')is not None:
                        if endpoint.is_set():raise ProtocolError('chunk_after_endpoint')
                        pcm.accept(message['bytes'])
                        if pcm.sequence==1 or pcm.sequence%5==0:emit('asr_chunk_received',sequence_id=pcm.sequence-1,sample_count=pcm.samples)
                        wake.set()
                    else:
                        control=json.loads(message.get('text','{}'))
                        if control.get('type')=='cancel':invalid=True;registry.cancel(key,'asr_stream_cancel');emit('asr_stream_cancelled',reason='client_cancel');break
                        if control.get('type')!='end_of_utterance' or endpoint.is_set():raise ProtocolError('unexpected_control')
                        if control.get('next_sequence_id')!=pcm.sequence or control.get('end_sample_index')!=pcm.next_index:raise ProtocolError('endpoint_sequence_mismatch')
                        if pcm.samples==0:raise ProtocolError('empty_audio')
                        endpoint.set();emit('asr_endpoint_sent',sample_count=pcm.samples);wake.set()
                    receive=asyncio.create_task(socket.receive())
                emit('asr_stream_closed',status='final' if finalized else 'cancelled')
        except WebSocketDisconnect:invalid=True
        except Exception as exc:
            invalid=True
            try:await socket.send_json({'type':'error','reason':str(exc) if isinstance(exc,ProtocolError) else type(exc).__name__})
            except Exception:pass
        finally:
            invalid=True;closed=True
            import contextlib
            for task in (receive,worker):
                if task is not None:
                    if not task.done():task.cancel()
                    with contextlib.suppress(asyncio.CancelledError,Exception):await task
            if native_session is not None:
                try:await run_in_threadpool(native_session.cancel)
                except Exception:pass
            try:await socket.close()
            except Exception:pass
