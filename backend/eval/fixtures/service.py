"""Isolated HTTP/WS fault fixture service. No production file edits/provider calls."""
import asyncio,json,os,time
from pathlib import Path
from fastapi import FastAPI,Request,WebSocket
from fastapi.responses import Response,StreamingResponse,JSONResponse
from latency_trace import LatencyTraceMiddleware
from agent_streaming import install
from .client import FixtureClient
from .source_loader import load_api
from .tone import pcm

class EvaluationTraceMiddleware:
    def __init__(self,app):self.app=app
    async def __call__(self,scope,receive,send):
        prefixes=('/native-live/','/llm-before/','/llm-after/','/reliability-race/','/invalid-state/')
        prefix=next((p for p in prefixes if scope.get('path','').startswith(p)),None)
        if scope.get('type')=='http' and prefix:
            async def forward(observed_scope,reader,writer):await self.app(scope,reader,writer)
            await LatencyTraceMiddleware(forward)(dict(scope,path=scope['path'].removeprefix(prefix.rstrip('/'))),receive,send)
        else:await LatencyTraceMiddleware(self.app)(scope,receive,send)

client=FixtureClient();api=load_api(client);app=FastAPI();app.add_middleware(EvaluationTraceMiddleware)
# Frozen check_turn uses the process-local registry; make API own that same isolated registry.
from turn_lifecycle import registry
api.turn_registry=registry;api._namespace['turn_registry']=registry
app.post('/agent')(api.agent);install(app,api)
live_api=None
if os.environ.get('EVAL_NATIVE_LIVE')=='1':
    from openai import OpenAI
    from dotenv import load_dotenv
    load_dotenv(Path(__file__).resolve().parents[2]/'.env')
    live_api=load_api(OpenAI(api_key=os.environ.get('DEEPSEEK_API_KEY'),base_url='https://api.deepseek.com'))
    live_api.turn_registry=registry;live_api._namespace['turn_registry']=registry
    live_api.conversation_sessions=api.conversation_sessions;live_api._namespace['conversation_sessions']=api.conversation_sessions
    app.post('/native-live/agent')(live_api.agent)
    install(app,live_api,prefix='/native-live')


@app.get('/health')
def health():return {'fixture':True,'provider_live':False}

@app.post('/llm-before/agent/stream')
async def llm_before():return Response(status_code=503)
app.post('/llm-before/agent')(api.agent)

@app.post('/llm-after/agent/stream')
async def llm_after(request:Request):
    data=await request.json();trace=request.headers['x-trace-id'];turn=int(request.headers['x-turn-id']);key=(trace,turn)
    registry.register(key);registry.prepare_reply(key,data['sessionId'],data['userMessage'],'SYNTHETIC_INCOMPLETE_NOT_COMMIT')
    async def chunks():
        yield json.dumps({'type':'speech_segment_ready','trace_id':trace,'turn_id':turn,'segment_id':trace+'-0','sequence_index':0,'text':'一緒に歩こう。','language':'ja'})+'\n'
        await asyncio.sleep(1.5)
        yield json.dumps({'type':'error','trace_id':trace,'turn_id':turn,'reason':'controlled_after_partial'})+'\n'
    return StreamingResponse(chunks(),media_type='application/x-ndjson')

@app.get('/tts')
async def complete_tts():return Response(Path(os.environ['EVAL_TONE_MP3']).read_bytes(),media_type='audio/mpeg')

async def tts_fixture(ws,fail=False):
    await ws.accept();r=await ws.receive_json()
    registry.register((r['trace_id'],int(r['turn_id'])))
    identity={k:r[k] for k in ('trace_id','turn_id','speech_request_id')}
    await ws.send_json(dict(type='start',sample_rate=32000,channels=1,format='pcm16',**identity))
    data=pcm(3)
    for i in range(0,len(data),8192):
        await ws.send_bytes(data[i:i+8192]);await asyncio.sleep(.004)
    if fail:
        await asyncio.sleep(.8);await ws.send_json({'type':'error','reason':'controlled_after_audio'})
    else:await ws.send_json(dict(type='end',total_frames=len(data)//2,**identity))
    await ws.close()
@app.websocket('/tts/stream')
async def tts_stream(ws:WebSocket):await tts_fixture(ws)
@app.websocket('/tts-after/stream')
async def tts_after(ws:WebSocket):await tts_fixture(ws,True)
@app.post('/lipsync/plan')
async def plan():return {'language':'ja','source':'synthetic_evaluation_fixture','timing':'synthetic','units':[{'phoneme':'a','viseme':'aa','kind':'vowel'}]}
@app.post('/turn/cancel')
async def cancel(request:Request):
    p=await request.json();return registry.cancel((p['trace_id'],p['turn_id']),p.get('reason','evaluation'))
@app.post('/turn/complete')
async def complete(request:Request):
    p=await request.json();return registry.complete((p['trace_id'],p['turn_id']),api.conversation_sessions)
@app.get('/evidence')
def evidence():return {'calls':len(client.calls),'history':api.conversation_sessions,'turns':{f'{k[0]}:{k[1]}':registry.snapshot(k) for k in registry._entries}}

# Reliability-only controlled endpoints. Main Backend is never fault-injected.
@app.post('/reliability-race/agent/stream')
async def reliability_before():return Response(status_code=503)
@app.post('/reliability-race/agent')
async def reliability_delayed(state:api.ExerciseState):
    await asyncio.sleep(1)
    return api.agent(state)
actual_stream=next(r.endpoint for r in app.routes if getattr(r,'path',None)=='/agent/stream')
@app.post('/invalid-state/agent/stream')
async def invalid_state_stream(state:api.ExerciseState,request:Request):
    return await actual_stream(state.model_copy(update={'stateSnapshot':{'version':999}}),request)
