"""Bounded native tool loop; no controller execution or committed tool history."""
import contextlib, json, os, threading, time, uuid, re
from pathlib import Path
_trace_lock=threading.Lock()
_trace_origin=time.perf_counter()
from fastapi import HTTPException, Request
from agent_state import validate_snapshot, semantic_state
from capabilities.registry import CapabilityRegistry, ToolCall
from capabilities.trace import emit as capability_emit
from action_tool_policy import ActionToolChoicePolicy
from reliability import FailureRecord, FailureCategory, Decision, monitor, Health

TOOL_DESCRIPTIONS = {
 'gesture.wave': 'Perform the existing wave gesture. Use for explicit requests such as 「手を振って」「バイバイして」. Do not repeat an active gesture.',
 'gesture.nod': 'Perform the existing nod gesture. Use for an explicit request such as 「うなずいて」. Ordinary agreement does not require a nod.',
 'spatial.lead': 'Move into the existing lead/guide formation. Use when explicitly asked to go, walk or run in front: 「前を走って」「先導して」. Do not repeat the requested current intent/state.',
 'spatial.side': 'Move into the existing side-by-side formation. Use for explicit requests: 「隣を走って」「横に来て」「一緒に並んで走って」. Do not repeat the requested current intent/state.',
 'spatial.trail': 'Move into the existing trailing formation. Use for explicit requests such as 「後ろを走って」. Do not repeat the current intent/state.',
 'spatial.nearby': 'Switch to existing nearby social positioning. Use for explicit requests such as 「近くに来て」「そばに来て」. Do not repeat the current intent/state.',
 'expression.set': 'Set a supported semantic facial expression. For an explicit smile request 「笑って」「笑顔を見せて」 use expression="happy". Ordinary conversation does not require an expression tool.'
}
RESULT_GROUNDING = '''Tool results and the latest state snapshot are authoritative. Accepted with completion_scope="dispatch_only" means accepted for execution, NOT that animation or movement has finished. Never claim completed action without explicit completion evidence. Use prospective/intention language such as 「手を振るね」「前に行くね」, not 「振ったよ」「もう移動したよ」. Rejected/Failed/Cancelled means no confirmed execution: do not claim it happened or expose internal policy jargon. If the requested intent/state is already active, acknowledge continuing that state (「このまま隣で走るね」), never claim a new move to it. A preferred intent already being active is enough to avoid a redundant request, even if its effective mode differs. Do not claim an action is underway either: dispatch_only is not evidence of started/completed animation. After a result, prefer final speech; do not repeat the action to get completion evidence.'''
TOOL_INSTRUCTION = '''Native tool mode: When the user explicitly requests a physical action and an appropriate AVAILABLE tool can perform it, you MUST use that tool instead of only saying you will do it. Ordinary conversation does not require a tool. Never call a tool merely to create visible activity or repeat an active action. Tool-call turns use API tool_calls without user-facing content; only final speech uses the existing JSON format. Wait for authoritative tool results before final speech. In final JSON, legacy behavior must be none/neutral no-op; only native tools authorize physical behavior.''' + '\n' + RESULT_GROUNDING

def grounded_instruction(registry, snapshot):
    availability=registry.availability(snapshot)['tools']
    avatar=dict(snapshot.sections.get('avatar',{}))
    facts={k:avatar.get(k) for k in ('spatial_intent','spatial_mode','gesture')}
    unavailable={name:item['reason'] for name,item in availability.items() if not item['available']}
    return TOOL_INSTRUCTION + '\nAuthoritative observed state (not completion evidence): ' + json.dumps(facts,separators=(',',':')) + '\nUnavailable tools: ' + json.dumps(unavailable,separators=(',',':')) + '\nIf requested spatial intent is already active, state that it continues, not that you will move there now. If a tool is unavailable for another reason, do not claim its execution.'

class ToolApiAliasMap:
    def __init__(self, registry):
        self.forward={name:name.replace('.', '_') for name in registry.descriptors}
        self.reverse={alias:name for name,alias in self.forward.items()}
        if len(self.reverse)!=len(self.forward): raise ValueError('alias_collision')
        if any(not alias.replace('_','').isalnum() for alias in self.reverse): raise ValueError('invalid_alias')
    def definitions(self, registry, snapshot):
        return [{'type':'function','function':{'name':self.forward[t['name']], 'description':TOOL_DESCRIPTIONS.get(t['name'],t['description']), 'parameters':t['argument_schema']}} for t in registry.get_available_tools(snapshot)]

class DeepSeekToolCallAssembler:
    def __init__(self): self.items={}; self.fragments=0
    def feed(self, deltas):
        for d in deltas:
            index=d.index
            if type(index) is not int or not 0<=index<32: raise ValueError('tool_index_capacity')
            item=self.items.setdefault(index,{'id':'','type':'function','function':{'name':'','arguments':''}})
            if getattr(d,'id',None):
                if item['id'] and item['id']!=d.id: raise ValueError('tool_id_changed')
                item['id']=d.id
            if getattr(d,'type',None) not in (None,'function'): raise ValueError('tool_type')
            f=getattr(d,'function',None)
            if f:
                item['function']['name']+=getattr(f,'name',None) or ''
                item['function']['arguments']+=getattr(f,'arguments',None) or ''
            if len(item['function']['arguments'])>4096 or len(item['function']['name'])>96 or len(item['id'])>128: raise ValueError('tool_capacity')
            self.fragments+=1
    def finish(self):
        items=[self.items[k] for k in sorted(self.items)]
        if not items or any(not i['id'] or not i['function']['name'] for i in items) or len({i['id'] for i in items})!=len(items): raise ValueError('invalid_tool_assembly')
        return items

def trace(api,key,event,**meta):
    api.trace_event(event,**meta)
    capability_emit(event,trace_id=key[0],turn_id=key[1],**meta)
    try:
        target=Path(os.environ.get('NATIVE_TOOL_TRACE_DIR',str(Path(__file__).parent/'native_tool_traces')))
        row=dict(component='backend_native_tools',event=event,trace_id=key[0],turn_id=key[1],utc_ms=time.time_ns()//1000000,mono_ms=(time.perf_counter()-_trace_origin)*1000,**meta)
        with _trace_lock:
            target.mkdir(parents=True,exist_ok=True)
            with (target/('native-'+str(os.getpid())+'.jsonl')).open('a',encoding='utf-8') as file:file.write(json.dumps(row,allow_nan=False)+'\n')
    except Exception:pass

class PendingToolResults:
    def __init__(self): self.lock=threading.RLock();self.pending={}
    def create(self, identity):
        with self.lock:
            slot={'identity':identity,'event':threading.Event(),'reply':None,'open':True}
            self.pending[identity['call_id']]=slot;return slot
    def submit(self, body, api):
        with self.lock:
            slot=self.pending.get(body.get('call_id'))
            if not slot or not slot['open']: raise HTTPException(409,'unknown_or_closed_tool_request')
            if any(body.get(k)!=v for k,v in slot['identity'].items()): raise HTTPException(409,'tool_result_identity')
            api.turn_registry.check((body['trace_id'],body['turn_id']),'tool_result_receive')
            if slot['reply'] is not None: raise HTTPException(409,'duplicate_tool_result')
            result=body.get('result')
            if not isinstance(result,dict) or result.get('call_id')!=body['call_id'] or result.get('tool_name')!=slot['identity']['tool_name']: raise HTTPException(400,'tool_result_contract')
            if result.get('status') not in ('Accepted','Rejected','Failed','Cancelled','Duplicate'): raise HTTPException(400,'tool_result_status')
            if result.get('completion_scope')!='dispatch_only': raise HTTPException(400,'tool_result_completion_scope')
            for field in ('requested_state_version','execution_state_version','started_at','completed_at'):
                if type(result.get(field)) is not int or result[field]<0: raise HTTPException(400,'tool_result_metadata')
            if result.get('reason') is not None and (not isinstance(result['reason'],str) or len(result['reason'])>96): raise HTTPException(400,'tool_result_reason')
            slot['reply']=body;slot['event'].set();return {'status':'received'}
    def close(self, slot):
        with self.lock:
            slot['open']=False;self.pending.pop(slot['identity']['call_id'],None)

def compact_result(name,status,reason=None):
    return {'tool':name,'status':status,'reason':reason,'completion_scope':'dispatch_only' if status in ('Accepted','Duplicate') else 'none'}

def native_stream(api,state,key,messages,publish,stop,holder,broker,timeout=1.5):
    registry=CapabilityRegistry();aliases=ToolApiAliasMap(registry)
    snapshot,status=validate_snapshot(state.stateSnapshot,key,state.mode,state.language,state.sessionId)
    if snapshot is None:
        api.turn_registry.check(key,'degraded_context_guard')
        failure=FailureRecord(key[0],key[1],'State',FailureCategory.State,'invalid_state')
        monitor.failure(failure)
        holder['degraded_context']=True;holder['native_mode']=True
        request_id=uuid.uuid4().hex;holder['agent_request_id']=request_id
        publish(dict(type='native_tool_mode_started',agent_request_id=request_id,manifest_hash=registry.manifest_hash,degraded_context=True))
        # ContextBuilder already omitted invalid state. No tools and no legacy body dispatch.
        stream=api.client.chat.completions.create(model='deepseek-v4-flash',messages=messages,extra_body={'thinking':{'type':'disabled'}},max_tokens=600,response_format={'type':'json_object'},stream=True,timeout=45)
        holder['stream']=stream
        try:
            for chunk in stream:
                api.turn_registry.check(key,'degraded_context_chunk')
                if stop.is_set():return
                yield chunk
            monitor.set_health('LLM',Health.Healthy)
        finally:stream.close()
        return
    request_id=uuid.uuid4().hex; holder['agent_request_id']=request_id;holder['native_mode']=True
    legacy_messages=[dict(m) for m in messages]
    messages=[dict(m) for m in messages];messages[0]=dict(messages[0],content=messages[0]['content']+'\n\n'+grounded_instruction(registry,snapshot))
    trace(api,key,'llm_tool_mode_started',agent_request_id=request_id,manifest_hash=registry.manifest_hash)
    publish(dict(type='native_tool_mode_started',agent_request_id=request_id,manifest_hash=registry.manifest_hash))
    holder['native_start_mono']=time.perf_counter();used=False;call_identity_map={}
    try:
        for round_index in range(3):
            api.turn_registry.check(key,'tool_round_start')
            if stop.is_set(): raise RuntimeError('cancelled')
            tools=aliases.definitions(registry,snapshot) if round_index<2 else []
            if round_index==2:
                trace(api,key,'llm_tool_loop_limit')
                messages.append({'role':'system','content':'tool_loop_limit_reached: no further tools. Return final speech JSON now.'})
            kwargs=dict(model='deepseek-v4-flash',messages=messages,extra_body={'thinking':{'type':'disabled'}},max_tokens=600,response_format={'type':'json_object'},stream=True,timeout=45)
            decision=ActionToolChoicePolicy().choose(state.userMessage,tools,continuation=round_index>0)
            tools=list(decision.tools)
            if tools: kwargs.update(tools=tools,tool_choice=decision.choice)
            trace(api,key,'tool_selection_policy',action_intent_policy=decision.intent,tool_choice_sent=decision.choice if tools else 'none',tools_exposed=[t['function']['name'] for t in tools],requested_tool=decision.requested_tool,policy_reason=decision.reason,tool_round=round_index)
            trace(api,key,'llm_tool_round_started',available_tool_count=len(tools),tool_round=round_index)
            assembler=DeepSeekToolCallAssembler();mode=None;begin=time.perf_counter();first=True
            stream=api.client.chat.completions.create(**kwargs);holder['stream']=stream
            try:
                for chunk in stream:
                    api.turn_registry.check(key,'native_chunk')
                    if stop.is_set(): raise RuntimeError('cancelled')
                    if not chunk.choices: continue
                    delta=chunk.choices[0].delta;calls=getattr(delta,'tool_calls',None);content=getattr(delta,'content',None)
                    if first and (calls or content and content.strip()):
                        first=False
                        if holder.get('last_tool_result_mono'):
                            trace(api,key,'tool_result_to_llm_first_token',duration_ms=(time.perf_counter()-holder['last_tool_result_mono'])*1000)
                    if calls:
                        if mode=='speech':
                            trace(api,key,'llm_tool_protocol_violation',reason='late_tool_after_speech');continue
                        if mode is None:
                            mode='tool';assembly_begin=time.perf_counter();trace(api,key,'llm_tool_call_delta',tool_decision_latency_ms=(time.perf_counter()-begin)*1000)
                        assembler.feed(calls)
                    if content and content.strip():
                        if mode=='tool': trace(api,key,'llm_tool_protocol_violation',reason='speech_in_tool_mode');continue
                        mode='speech'
                    if mode=='speech' and content:
                        holder['native_speech_emitted']=True
                        yield chunk
                if mode=='speech': return
                if mode!='tool': raise ValueError('empty_native_model_turn')
                if round_index>=2: raise ValueError('tools_after_loop_limit')
                calls=assembler.finish()
                trace(api,key,'llm_tool_call_assembled',tool_assembly_ms=(time.perf_counter()-assembly_begin)*1000,call_count=len(calls),fragment_count=assembler.fragments)
            finally:
                with contextlib.suppress(Exception): stream.close()
            messages.append({'role':'assistant','content':None,'tool_calls':calls})
            categories=set()
            for index,item in enumerate(calls):
                api.turn_registry.check(key,'tool_prevalidation')
                if stop.is_set(): raise RuntimeError('cancelled')
                validation=time.perf_counter();alias=item['function']['name'];name=aliases.reverse.get(alias);reason=None
                if not name: reason='unknown_tool'
                elif index>=3: reason='tool_call_limit'
                else:
                    category=registry.descriptors[name].to_dict()['category']
                    if category in categories: reason='conflicting_tool_call'
                    else:
                        try:
                            args=json.loads(item['function']['arguments'],parse_constant=lambda _:(_ for _ in ()).throw(ValueError('invalid_arguments')))
                            args=registry.validate_arguments(name,args)
                            availability=registry.availability(snapshot)['tools'][name]
                            if not availability['available']: reason=availability['reason']
                        except (ValueError,TypeError): reason='invalid_arguments'
                    categories.add(category)
                if not reason and item['id'] in call_identity_map and call_identity_map[item['id']][1]!=(name,json.dumps(args,sort_keys=True,separators=(',',':'))):reason='conflicting_tool_call'
                trace(api,key,'tool_alias_resolved',tool_name=name or 'unknown')
                trace(api,key,'tool_backend_validated',backend_validation_ms=(time.perf_counter()-validation)*1000,reason=reason)
                if reason: result=compact_result(name or alias,'Rejected',reason)
                else:
                    fingerprint=(name,json.dumps(args,sort_keys=True,separators=(',',':')))
                    previous=call_identity_map.get(item['id'])
                    call_id=previous[0] if previous else uuid.uuid5(uuid.UUID(request_id),item['id']).hex
                    call_identity_map[item['id']]=(call_id,fingerprint)
                    call=ToolCall.parse(dict(call_id=call_id,tool_name=name,arguments=args,turn_id=key[1],trace_id=key[0],state_version=snapshot.state_version))
                    identity=dict(agent_request_id=request_id,turn_id=key[1],trace_id=key[0],api_tool_call_id=item['id'],call_id=call_id,tool_name=name,manifest_hash=registry.manifest_hash)
                    slot=broker.create(identity);sent=time.perf_counter();holder['tool_requested']=True;used=True
                    try:
                        trace(api,key,'tool_request_sent_to_unity',call_id=call_id,tool_name=name)
                        publish(dict(type='tool_call_requested',identity=identity,call=dict(call_id=call.call_id,tool_name=name,arguments=args,turn_id=key[1],trace_id=key[0],state_version=snapshot.state_version)))
                        deadline=time.monotonic()+timeout
                        while not slot['event'].wait(.025):
                            api.turn_registry.check(key,'tool_wait')
                            if stop.is_set(): raise RuntimeError('cancelled')
                            if time.monotonic()>=deadline: break
                        api.turn_registry.check(key,'tool_result_resume')
                        if stop.is_set(): raise RuntimeError('cancelled')
                        if slot['reply']:
                            body=slot['reply'];r=body['result'];result=compact_result(name,r['status'],r.get('reason'))
                            if r['status']=='Failed':
                                monitor.failure(FailureRecord(key[0],key[1],'Tool',FailureCategory.Tool,'tool_result_timeout' if r.get('reason')=='tool_result_timeout' else 'executor_failed'),tool_requested=True)
                                monitor.set_health('Tool',Health.Degraded)
                            refreshed,refresh_status=validate_snapshot(body.get('stateSnapshot'),key,state.mode,state.language,state.sessionId)
                            if refreshed is None or refreshed.state_version<snapshot.state_version: raise ValueError('invalid_refreshed_snapshot')
                            snapshot=refreshed
                            trace(api,key,'tool_result_received',Unity_dispatch_roundtrip_ms=(time.perf_counter()-sent)*1000,status=r['status'],tool_name=name)
                        else:
                            result=compact_result(name,'Failed','tool_result_timeout')
                            failure=FailureRecord(key[0],key[1],'Tool',FailureCategory.Tool,'tool_result_timeout')
                            monitor.failure(failure,tool_requested=True)
                            monitor.set_health('Tool',Health.Degraded)
                            trace(api,key,'tool_execution_unknown',call_id=call_id,tool_name=name)
                    finally: broker.close(slot)
                holder['last_tool_result_mono']=time.perf_counter()
                messages.append({'role':'tool','tool_call_id':item['id'],'content':json.dumps(result,separators=(',',':'))})
                trace(api,key,'llm_tool_result_submitted',status=result['status'],reason=result['reason'],completion_scope=result['completion_scope'])
            # Replace observed state in the existing ContextBuilder system message, not history.
            refreshed_state=state.model_copy(update={'stateSnapshot':dict(version=1,snapshot_id=snapshot.snapshot_id,created_at_ms=snapshot.created_at_ms,turn_id=snapshot.turn_id,trace_id=snapshot.trace_id,state_version=snapshot.state_version,**{n:dict(v) for n,v in snapshot.sections.items()})})
            fresh_messages=api.build_agent_messages(refreshed_state,[],key,False,'tool_continuation')
            messages[0]={'role':'system','content':fresh_messages[0]['content']+'\n\n'+grounded_instruction(registry,snapshot)}
            messages.append({'role':'system','content': 'Continuation after authoritative tool results: no completion evidence was reported. Any Accepted action is only dispatched. Describe your intention, not an accomplished action. In particular NEVER say 「うなずいたよ」「頷いたよ」「振ったよ」「移動したよ」 on dispatch_only. A Rejected/Failed action was not confirmed executed. For already-active spatial intent, acknowledge continuing it. Return truthful final speech JSON; do not repeat a tool to obtain completion.'})
            failed_results=[json.loads(m['content']) for m in messages if m['role']=='tool' and json.loads(m['content']).get('status') not in ('Accepted','Duplicate')]
            if failed_results:
                messages.append({'role':'system','content':'Authoritative negative tool outcomes: '+json.dumps(failed_results,separators=(',',':'))+' These actions were NOT confirmed accepted/executed. Do NOT promise to perform these actions now, do NOT claim you performed them, and do NOT retry their tools. Acknowledge inability or uncertainty naturally without internal codes. Do not invent a physical cause (such as occupied hands) from a busy/timeout status. Other accepted actions may be described only as intentions.'})
            trace(api,key,'llm_tool_round_completed',state_version=snapshot.state_version)
    except Exception:
        # The lifecycle guard runs before any recovery; cancelled identities cannot fall back.
        api.turn_registry.check(key,'native_fallback')
        failure=FailureRecord(key[0],key[1],'native_stream',FailureCategory.LLM,'native_infrastructure_failed')
        decision=monitor.failure(failure,output=bool(holder.get('native_speech_emitted')),tool_requested=used,cancelled=stop.is_set())
        monitor.set_health('LLM',Health.Degraded)
        if decision.decision!=Decision.Fallback:raise
        holder['fallback_depth']=1
        monitor.event('fallback_started',failure,decision,fallback_depth=1)
        began=time.perf_counter()
        holder['native_mode']=False;trace(api,key,'native_tool_fallback',reason='infrastructure_before_dispatch')
        legacy=api.client.chat.completions.create(model='deepseek-v4-flash',messages=legacy_messages,extra_body={'thinking':{'type':'disabled'}},max_tokens=600,response_format={'type':'json_object'},stream=True,timeout=45)
        holder['stream']=legacy
        try:
            for chunk in legacy:
                api.turn_registry.check(key,'native_legacy_chunk')
                if stop.is_set():return
                yield chunk
            monitor.event('fallback_succeeded',failure,decision,fallback_depth=1,added_latency_ms=(time.perf_counter()-began)*1000)
            monitor.set_health('LLM',Health.Healthy)
        except Exception:
            monitor.event('fallback_failed',failure,decision,fallback_depth=1)
            raise
        finally:legacy.close()

def install_result_endpoint(app,api,broker,prefix=''):
    @app.post(prefix+'/agent/tool-result')
    async def tool_result(request:Request):
        try:
            raw=await request.body()
            if len(raw)>16384: raise HTTPException(413,'tool_result_capacity')
            body=json.loads(raw)
            if not isinstance(body,dict): raise HTTPException(400,'tool_result_object')
            return broker.submit(body,api)
        except HTTPException as e:
            identity=body if isinstance(locals().get('body'),dict) else {}
            if re.fullmatch(r'[A-Za-z0-9_-]{1,96}',str(identity.get('trace_id',''))) and type(identity.get('turn_id')) is int:
                trace(api,(identity['trace_id'],identity['turn_id']),'tool_result_discarded',reason=str(e.detail))
            raise
        except (ValueError,TypeError): raise HTTPException(400,'invalid_tool_result')
