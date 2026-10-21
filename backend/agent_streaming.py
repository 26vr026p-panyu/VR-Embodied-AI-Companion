"""Safe incremental speech extraction and sentence-level agent transport."""
import asyncio, contextlib, json, threading, time, os
from fastapi import HTTPException, Request
from fastapi.responses import StreamingResponse

class IncrementalAgentResponseParser:
    def __init__(self):
        self.buffer=''; self.depth=0; self.inside=False; self.escape=False
        self.raw=''; self.key=None; self.value=False; self.target=False
        self.started=False; self.closed=False; self.decoded=0; self.speech=''
    def feed(self, chunk):
        self.buffer+=chunk
        if len(self.buffer)>65536: raise ValueError('response_capacity')
        fresh=[]
        for c in chunk:
            if self.inside:
                if c=='"' and not self.escape:
                    value=json.loads('"'+self.raw+'"')
                    if self.target:
                        fresh.append(value[self.decoded:]); self.speech+=value[self.decoded:]; self.closed=True
                    elif self.depth==1 and not self.value: self.key=value
                    self.inside=False
                    if self.value: self.value=False
                else:
                    self.raw+=c; self.escape=(not self.escape) if c=='\\' else False
                continue
            if c=='"':
                self.inside=True; self.raw=''; self.decoded=0
                self.target=self.depth==1 and self.value and self.key=='speech'
                if self.target:
                    if self.started: raise ValueError('duplicate_speech')
                    self.started=True
            elif c in '{[': self.depth+=1; self.value=False
            elif c in '}]': self.depth-=1; self.value=False
            elif c==':' and self.depth==1: self.value=True
            elif c==',' and self.depth==1: self.key=None; self.value=False
        if self.inside and self.target:
            end=len(self.raw); value=''
            while end:
                try:
                    value=json.loads('"'+self.raw[:end]+'"')
                    if value and 0xD800<=ord(value[-1])<=0xDBFF: end-=6; continue
                    break
                except ValueError: end-=1
            if end<=0: value=''
            delta=value[self.decoded:]; self.decoded=len(value); self.speech+=delta; fresh.append(delta)
        return ''.join(fresh)

class SentenceSegmenter:
    def __init__(self): self.tail=''
    def feed(self,text):
        result=[]
        for c in text:
            self.tail+=c
            if c in '。！？!?\n':
                if self.tail.strip(): result.append(self.tail.strip())
                self.tail=''
        return result
    def finish(self):
        result=[self.tail.strip()] if self.tail.strip() else []; self.tail=''; return result

def canonical(text): return ''.join(text.split())

def install(app, api, prefix=""):
    from native_tools import PendingToolResults, native_stream, install_result_endpoint
    broker=PendingToolResults()
    install_result_endpoint(app,api,broker,prefix)
    async def endpoint(state, request:Request):
        key=api.check_turn('stream_start')
        if key is None: raise HTTPException(400,'stream_requires_turn_identity')
        native_enabled=request.headers.get('x-native-tool-calling')=='true'
        required,reason=api.check_intervention(state) if state.mode=='exercise' else (False,'general_mode')
        history=api.conversation_sessions.setdefault(state.sessionId,[])
        messages=api.build_agent_messages(state,history,key,required,reason)
        queue=asyncio.Queue(maxsize=16); loop=asyncio.get_running_loop(); stop=threading.Event(); holder={}
        def publish(event):
            future=asyncio.run_coroutine_threadsafe(queue.put(event),loop)
            while not stop.is_set():
                try: future.result(timeout=.1); return
                except TimeoutError: pass
            future.cancel(); raise RuntimeError('transport_closed')
        def work():
            parser=IncrementalAgentResponseParser(); segmenter=SentenceSegmenter(); sent=[]; chunks=0; parser_ms=0
            def segment(text):
                api.check_turn('sentence_dispatch')
                if stop.is_set(): raise RuntimeError('cancelled')
                index=len(sent); sent.append(text)
                if index==0 and holder.get('last_tool_result_mono'):
                    from native_tools import trace
                    trace(api,key,'tool_result_to_first_sentence',duration_ms=(time.perf_counter()-holder['last_tool_result_mono'])*1000)
                if index==0 and holder.get('native_start_mono'):
                    from native_tools import trace
                    trace(api,key,'native_request_to_first_sentence',duration_ms=(time.perf_counter()-holder['native_start_mono'])*1000)
                api.trace_event('llm_first_sentence_ready' if index==0 else 'llm_sentence_ready',text_length=len(text))
                publish({'type':'speech_segment_ready','segment_id':f'{key[0]}-{index}','sequence_index':index,'text':text,'language':state.language})
            try:
                api.trace_event('llm_stream_started')
                if native_enabled:
                    stream=native_stream(api,state,key,messages,publish,stop,holder,broker,timeout=float(os.environ.get('NATIVE_TOOL_RESULT_TIMEOUT_MS','1500'))/1000)
                else:
                    stream=api.client.chat.completions.create(model='deepseek-v4-flash',messages=messages,extra_body={'thinking':{'type':'disabled'}},max_tokens=600,response_format={'type':'json_object'},stream=True,timeout=45)
                holder['stream']=stream
                if stop.is_set(): stream.close(); return
                for chunk in stream:
                    api.check_turn('stream_chunk')
                    if stop.is_set(): return
                    content=chunk.choices[0].delta.content if chunk.choices else None
                    if not content: continue
                    chunks+=1
                    if chunks==1: api.trace_event('llm_first_token'); publish({'type':'llm_first_token'})
                    was=parser.started; began=time.perf_counter(); decoded=parser.feed(content); parser_ms+=(time.perf_counter()-began)*1000
                    if parser.started and not was: api.trace_event('llm_speech_field_started'); publish({'type':'llm_speech_field_started'})
                    for text in segmenter.feed(decoded): segment(text)
                api.check_turn('stream_final')
                if stop.is_set(): return
                api.trace_event('llm_stream_finished',response_length=len(parser.buffer))
                publish({'type':'llm_stream_completed','chunk_count':chunks})
                try:
                    result=api.normalize_reply(json.loads(parser.buffer))
                    if not parser.closed or canonical(parser.speech)!=canonical(result['speech']):
                        api.trace_event('streamed_speech_mismatch'); raise ValueError('streamed_speech_mismatch')
                except Exception:
                    api.trace_event('llm_final_validation_failed'); raise
                for text in segmenter.finish(): segment(text)
                if canonical(''.join(sent))!=canonical(result['speech']): raise ValueError('streamed_speech_mismatch')
                api.check_turn('stream_history_prepare')
                if state.userMessage.strip(): api.turn_registry.prepare_reply(key,state.sessionId,state.userMessage,result['speech'])
                api.trace_event('llm_final_validated')
                publish({'type':'final_validated','native_tool_mode':bool(holder.get('native_mode')),'reply':result,'parser_ms':parser_ms,'buffered_characters':len(parser.buffer)})
                publish({'type':'sequence_closed'})
            except Exception as e:
                if not stop.is_set():
                    api.trace_event('llm_stream_failed',reason=type(e).__name__)
                    with contextlib.suppress(Exception): publish({'type':'error','reason':'llm_stream_failed','segments_emitted':len(sent),'fallback_depth':holder.get('fallback_depth',0)})
            finally:
                if holder.get('stream'):
                    with contextlib.suppress(Exception): holder['stream'].close()
        async def generate():
            task=asyncio.create_task(asyncio.to_thread(work))
            try:
                while True:
                    try: event=await asyncio.wait_for(queue.get(),timeout=.25)
                    except asyncio.TimeoutError:
                        api.turn_registry.check(key,'transport_poll')
                        if task.done() and queue.empty(): break
                        continue
                    api.turn_registry.check(key,'transport_send'); event.update(trace_id=key[0],turn_id=key[1])
                    yield (json.dumps(event,ensure_ascii=False)+'\n').encode('utf-8')
                    if event['type'] in ('sequence_closed','error'): break
            finally:
                stop.set()
                if holder.get('stream'):
                    with contextlib.suppress(Exception): holder['stream'].close()
                if not task.done(): api.trace_event('llm_stream_cancelled',reason='transport_closed')
                with contextlib.suppress(Exception,asyncio.TimeoutError): await asyncio.wait_for(asyncio.shield(task),timeout=2)
        return StreamingResponse(generate(),media_type='application/x-ndjson',headers={'Cache-Control':'no-store'})
    endpoint.__annotations__['state']=api.ExerciseState
    app.post(prefix+'/agent/stream')(endpoint)
