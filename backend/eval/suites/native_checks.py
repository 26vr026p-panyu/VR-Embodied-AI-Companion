import json,time,threading,statistics
from types import SimpleNamespace as N
from capabilities.registry import CapabilityRegistry
from native_tools import ToolApiAliasMap,DeepSeekToolCallAssembler,PendingToolResults,native_stream
from eval.fixtures.native_client import NativeFixtureClient
from eval.fixtures.source_loader import load_api
from turn_lifecycle import TurnCancellationRegistry
from fastapi import HTTPException

def payload(key,intent='side',version=1):
    return dict(version=1,snapshot_id='native-eval',created_at_ms=int(time.time()*1000),turn_id=key[1],trace_id=key[0],state_version=version,
      conversation=dict(mode='general',language='ja',session_id='native-eval'),turn=dict(lifecycle='processingllm',cancelled=False),
      speech=dict(state='idle',is_speaking=False),avatar=dict(spatial_intent=intent,spatial_mode='stay',gesture='none'),user=dict(is_speaking=False,activity='stationary'),environment={},system=dict(language_mode='ja'))

def exercise(scenario,timeout=.02,cancel=False):
    client=NativeFixtureClient();api=load_api(client);api.turn_registry=TurnCancellationRegistry();key=('native-eval',1);api.turn_registry.register(key)
    state=api.ExerciseState(mode='general',language='ja',sessionId='native-eval',userMessage=scenario,stateSnapshot=payload(key))
    broker=PendingToolResults();events=[];dispatch=[];stop=threading.Event();holder={}
    def publish(e):
        events.append(e)
        if e['type']=='tool_call_requested':
            ident=e['identity'];call=e['call'];dispatch.append(call)
            if cancel:api.turn_registry.cancel(key,'fixture_cancel');return
            if scenario=='NT_TIMEOUT':return
            result=dict(call_id=call['call_id'],tool_name=call['tool_name'],status='Accepted',reason=None,completion_scope='dispatch_only',requested_state_version=call['state_version'],execution_state_version=call['state_version']+1,started_at=1,completed_at=2)
            intent=call['tool_name'].split('.')[1] if call['tool_name'].startswith('spatial.') else 'side'
            body=dict(ident,result=result,stateSnapshot=payload(key,intent,call['state_version']+1))
            broker.submit(body,api)
            try:broker.submit(body,api)
            except HTTPException:pass
            else:raise AssertionError('duplicate result accepted')
    chunks=list(native_stream(api,state,key,api.build_agent_messages(state,[],key),publish,stop,holder,broker,timeout))
    speech=''.join(c.choices[0].delta.content or '' for c in chunks)
    return client,dispatch,speech,events,broker,holder,api

def run(handler,config):
    scenario=handler.removeprefix('native_');assertions=[];metrics={}
    def check(text,ok):assertions.append(dict(description=text,pass_=bool(ok)));assert ok,text
    if scenario=='alias':
        r=CapabilityRegistry();a=ToolApiAliasMap(r);check('Alias one-to-one canonical preserved',len(a.forward)==len(a.reverse)==7 and all(a.reverse[v]==k for k,v in a.forward.items()))
    elif scenario=='assembly':
        a=DeepSeekToolCallAssembler();a.feed([N(index=1,id='b',function=N(name='gesture_wave',arguments='{'),type='function'),N(index=0,id='a',function=N(name='gesture_nod',arguments='{}'),type='function')]);a.feed([N(index=1,id=None,function=N(name=None,arguments='}'),type=None)])
        check('Interleaved fragments ordered once on complete',a.finish()[0]['id']=='a' and json.loads(a.finish()[1]['function']['arguments'])=={})
    elif scenario=='prefix_identity':
        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from eval.fixtures.service import EvaluationTraceMiddleware
        from agent_streaming import install
        api=load_api(NativeFixtureClient());app=FastAPI();app.add_middleware(EvaluationTraceMiddleware);install(app,api,prefix='/native-live')
        key=('prefix-eval',1)
        with TestClient(app) as client:
            response=client.post('/native-live/agent/stream',headers={'X-Trace-Id':key[0],'X-Turn-Id':'1','X-Native-Tool-Calling':'true'},json={'mode':'general','language':'ja','sessionId':'native-eval','userMessage':'NT_NONE','stateSnapshot':payload(key)})
        check('Prefixed fixture preserves real trace identity',response.status_code==200 and 'final_validated' in response.text)
    elif scenario=='result_identity':
        broker=PendingToolResults();api=load_api();api.turn_registry=TurnCancellationRegistry();api.turn_registry.register(('identity-eval',1))
        identity=dict(agent_request_id='request',trace_id='identity-eval',turn_id=1,api_tool_call_id='api',call_id='canonical',tool_name='gesture.nod',manifest_hash='hash')
        slot=broker.create(identity);result=dict(call_id='canonical',tool_name='gesture.nod',status='Accepted',reason=None,completion_scope='dispatch_only',requested_state_version=1,execution_state_version=2,started_at=1,completed_at=2)
        body=dict(identity,result=result)
        for field in identity:
            try:broker.submit(dict(body,**{field:2 if field=='turn_id' else 'wrong'}),api)
            except HTTPException:pass
            else:raise AssertionError('Wrong identity accepted: '+field)
        check('All pending identity dimensions enforced',slot['reply'] is None)
        broker.close(slot)
        try:broker.submit(body,api)
        except HTTPException:check('Late closed result discarded',True)
        else:raise AssertionError('Late result accepted')
    elif scenario=='policy':
        from action_tool_policy import ActionToolChoicePolicy
        from agent_state import validate_snapshot
        api=load_api();key=('native-eval',1)
        snap,_=validate_snapshot(payload(key,intent='trail'),key,'general','ja','native-eval')
        tools=ToolApiAliasMap(CapabilityRegistry()).definitions(CapabilityRegistry(),snap);policy=ActionToolChoicePolicy()
        wave=policy.choose('手を振って。',tools)
        check('Narrow Wave named choice only one available gesture',wave.choice=={'type':'function','function':{'name':'gesture_wave'}} and len(wave.tools)==1)
        for text in ('前を走ってくれる？','隣を走って。','近くに来て。'):
            d=policy.choose(text,tools);check('Explicit spatial required category only',d.choice=='required' and all(t['function']['name'].startswith('spatial_') for t in d.tools))
        check('Smile required expression only',policy.choose('笑って。',tools).choice=='required')
        for text in ('今日は疲れた','手を振らないで','「手を振って」と言われた','前を走っている人がいる','手を振ってから前を走って'):
            check('Narrative negative quoted mixed request remains AUTO',policy.choose(text,tools).choice=='auto')
        check('Unavailable requested action not replaced or forced',not policy.choose('後ろを走って。',tools).tools)
        check('Continuation is AUTO not repeated force',policy.choose('手を振って。',tools,continuation=True).choice=='auto')
    elif scenario=='grounding_contract':
        from native_tools import grounded_instruction
        from agent_state import validate_snapshot
        key=('native-eval',1);snap,_=validate_snapshot(payload(key),key,'general','ja','native-eval')
        text=grounded_instruction(CapabilityRegistry(),snap)
        check('Authoritative intent and unavailability passed independently of heuristic', 'already_in_state' in text and 'spatial.side' in text)
        check('No false completion or underway permission', 'NOT that animation' in text and 'not evidence of started/completed' in text)
    elif scenario=='cancel':
        try:exercise('NT_CANCEL',cancel=True)
        except HTTPException as e:check('Cancelled wait rejects continuation',e.status_code==409)
        else:raise AssertionError('cancel resumed')
    else:
        name={'no_tool':'NT_NONE','wave':'NT_WAVE','lead':'NT_LEAD','expression':'NT_EXPRESSION','invalid':'NT_INVALID','unknown':'NT_UNKNOWN','already_side':'NT_SIDE','multi':'NT_MULTI','conflict':'NT_CONFLICT','timeout':'NT_TIMEOUT','loop':'NT_LOOP','legacy':'NT_LEGACY'}[scenario]
        started=time.perf_counter();client,dispatch,speech,events,broker,holder,api=exercise(name);metrics['elapsed_ms']=(time.perf_counter()-started)*1000
        check('Final nonempty JSON speech',bool(json.loads(speech)['speech']))
        check('No pending loop after request',not broker.pending)
        check('No committed user-facing tool history',not api.conversation_sessions)
        definitions=[t['function']['name'] for t in client.calls[0].get('tools',[])]
        check('Already Side filtered before model request','spatial_side' not in definitions)
        results=[json.loads(m['content']) for m in client.calls[-1]['messages'] if m['role']=='tool']
        if scenario=='no_tool':check('No extra LLM round and no body dispatch',len(client.calls)==1 and not dispatch)
        if scenario in ('invalid','unknown','already_side'):check('Backend failure never dispatches Unity',not dispatch and results[0]['status']=='Rejected')
        if scenario=='conflict':check('One spatial dispatch; later call rejected',len(dispatch)==1 and results[-1]['reason']=='conflicting_tool_call')
        if scenario=='multi':check('Compatible tools sequential model order',[c['tool_name'] for c in dispatch]==['expression.set','gesture.nod'])
        if scenario=='timeout':check('Timeout returned to model without retry',len(dispatch)==1 and results[0]['reason']=='tool_result_timeout')
        if scenario=='loop':check('Two rounds then final only; repeated API ID preserves canonical ID',len(dispatch)==2 and len(client.calls)==3 and not client.calls[-1].get('tools') and dispatch[0]['call_id']==dispatch[1]['call_id'])
        if scenario in ('wave','lead','expression'):check('Canonical dispatch and dispatch-only result',len(dispatch)==1 and results[0]['completion_scope']=='dispatch_only')
        if scenario=='legacy':check('Native authority flag retained for final legacy suppression',holder['native_mode'] is True and json.loads(speech)['behavior']['gesture']=='wave')
        metrics.update(model_requests=len(client.calls),dispatched_tools=[c['tool_name'] for c in dispatch],tool_results=results)
    for a in assertions:a['pass']=a.pop('pass_')
    return dict(assertions=assertions,metrics=metrics)
