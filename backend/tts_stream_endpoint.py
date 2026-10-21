import asyncio,re,uuid
from contextlib import suppress
from fastapi import WebSocket,WebSocketDisconnect
from latency_trace import websocket_context,emit
from turn_lifecycle import check_turn
from tts.models import SpeechRequest

def install(app,router):
    @app.websocket('/tts/stream')
    async def stream(ws:WebSocket):
        await ws.accept();watch=None;iterator=None
        try:
            req=await asyncio.wait_for(ws.receive_json(),5)
            trace=req['trace_id'];turn=int(req['turn_id']);identity=req['speech_request_id']
            if not re.fullmatch(r'[A-Za-z0-9_-]{1,100}',trace) or not isinstance(identity,str) or len(identity)>160:raise ValueError('identity')
            text=req['text'];language=req['language']
            if not isinstance(text,str) or not text.strip() or len(text)>4000 or language not in ('ja','zh'):raise ValueError('request')
            with websocket_context(trace,turn,identity,endpoint='/tts/stream'):
                check_turn('tts_stream_start');provider=router.streaming_provider();cap=provider.capabilities
                await ws.send_json({'type':'start','trace_id':trace,'turn_id':turn,'speech_request_id':identity,
                    'sample_rate':cap.sample_rate,'channels':cap.channels,'format':cap.output_format})
                # A receive watcher makes disconnect cancel a pending provider read.
                async def disconnect():
                    while True:
                        event=await ws.receive()
                        if event['type']=='websocket.disconnect':return
                watch=asyncio.create_task(disconnect());iterator=router.stream(SpeechRequest(text,language,style=req.get('style','neutral'),trace_id=trace,turn_id=turn))
                count=0;frames=0
                while True:
                    pending=asyncio.create_task(anext(iterator))
                    done,_=await asyncio.wait([pending,watch],return_when=asyncio.FIRST_COMPLETED)
                    if watch in done:
                        pending.cancel()
                        with suppress(asyncio.CancelledError):await pending
                        emit('tts_stream_cancelled',reason='transport_closed');return
                    try:pcm=pending.result()
                    except StopAsyncIteration:break
                    check_turn('tts_stream_send');count+=1;frames+=len(pcm)//2
                    if count==1:emit('tts_first_chunk_received',sample_count=len(pcm)//2)
                    await ws.send_bytes(pcm)
                check_turn('tts_stream_end');emit('tts_stream_ended',sample_count=frames)
                await ws.send_json({'type':'end','trace_id':trace,'turn_id':turn,'speech_request_id':identity,'total_frames':frames,'chunk_count':count})
        except WebSocketDisconnect:pass
        except Exception:
            with suppress(Exception):await ws.send_json({'type':'error','reason':'tts_stream_failed'})
        finally:
            if watch:watch.cancel()
            if iterator:
                with suppress(Exception,asyncio.CancelledError):await iterator.aclose()
            with suppress(Exception):await ws.close()
