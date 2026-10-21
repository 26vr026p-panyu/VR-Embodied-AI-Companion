import copy,json,time,statistics,uuid,urllib.request
from pathlib import Path
from types import SimpleNamespace as NS
from ..contracts import SkipCase,EvalCase,EvalResult,exit_code
from ..fixtures.client import FixtureClient
from ..fixtures.source_loader import load_api
from ..metrics.performance import compare
from agent_context import AgentContextBuilder
from agent_state import validate_snapshot
from behavior_contract import normalize_reply,none_behavior
from companion_persona import build_system_prompt
from turn_lifecycle import TurnCancellationRegistry
from agent_streaming import IncrementalAgentResponseParser,SentenceSegmenter

def state():
    identity=('eval-contract',41)
    p={'version':1,'snapshot_id':'eval-41','created_at_ms':int(time.time()*1000),'turn_id':41,'trace_id':identity[0],'state_version':1,'conversation':{'mode':'general','language':'ja','session_id':'eval'},'turn':{'lifecycle':'processingllm','cancelled':False},'speech':{'state':'idle','is_speaking':False,'queue_depth':0},'avatar':{'expression':'neutral','gesture':'none','spatial_mode':'lead','locomotion':'idle'},'user':{'is_speaking':False},'environment':{},'system':{'language_mode':'ja'}}
    return NS(mode='general',language='ja',sessionId='eval',userMessage='前を走って',stateSnapshot=p),identity

def run(handler,config):
    s,key=state();builder=AgentContextBuilder();metrics={};assertions=[]
    def check(description,condition):
        assertions.append({'description':description,'pass':bool(condition)})
        if not condition:raise AssertionError(description)
    if handler=='readonly':
        snap,status=validate_snapshot(s.stateSnapshot,key,s.mode,s.language,s.sessionId)
        try:snap.sections['avatar']['gesture']='bad';mutable=True
        except TypeError:mutable=False
        check('frozen nested snapshot',status=='valid' and not mutable)
    elif handler=='general':
        messages,meta=builder.build(s,[],key)
        check('no exercise object in General user context','exercise' not in json.loads(messages[-1]['content']))
        check('no invented telemetry','heartRate' not in json.dumps(s.stateSnapshot) and 'rpe' not in json.dumps(s.stateSnapshot))
    elif handler in ('history','history_once','late'):
        r=TurnCancellationRegistry();sessions={};r.register(key);r.prepare_reply(key,'eval','user','CANCELLED_SECRET');r.cancel(key,'evaluation')
        blocked=False
        try:r.complete(key,sessions)
        except Exception:blocked=True
        check('cancelled pending commit rejected',blocked and not sessions and not r.snapshot(key)['pending'])
        if handler=='late':
            rejected=False
            try:r.check(key,'late_result')
            except Exception:rejected=True
            check('late result rejected by actual registry',rejected)
        if handler=='history':
            m,meta=builder.build(s,sessions.get('eval',[]),key)
            check('cancelled assistant absent from Context','CANCELLED_SECRET' not in json.dumps(m))
        if handler=='history_once':
            new=('completed-contract',42);r.register(new);r.prepare_reply(new,'eval','u','a')
            first=r.complete(new,sessions);second=r.complete(new,sessions)
            check('exactly once',first['committed'] and not second['committed'] and len(sessions['eval'])==2)
    elif handler=='malformed':
        mutations=[lambda p:p.update(version=True),lambda p:p.update(turn_id=42),lambda p:p['avatar'].update(gesture='fly'),lambda p:p['environment'].update(scene='x'*9000)]
        for mutate in mutations:
            test=copy.deepcopy(s);mutate(test.stateSnapshot);m,meta=builder.build(test,[],key)
            check('rejected state, minimal context',meta['snapshot_status']=='rejected' and 'CURRENT STATE' not in m[0]['content'])
        metrics['snapshot_validation_fallback_count']=len(mutations)
    elif handler=='budget':
        history=[]
        for i in range(12):history.extend([{'role':'user','content':f'u{i}'},{'role':'assistant','content':f'a{i}'}])
        m,meta=builder.build(s,history,key)
        check('latest complete 8 pairs',meta['recent_turn_count']==8 and m[1]['content']=='u4' and m[-2]['role']=='assistant')
        check('history not mutated',len(history)==24)
        s.userMessage='"\\漢'*20000;m,meta=builder.build(s,history,key)
        check('hard context character bound',meta['context_chars']<=meta['budget_characters'])
        check('persona intact',m[0]['content'].startswith(build_system_prompt('general')))
        check('valid user JSON',isinstance(json.loads(m[-1]['content']),dict))
        metrics={'context_chars':meta['context_chars'],'budget_usage':meta['context_chars']/meta['budget_characters']}
    elif handler in ('valid','speech_only','enum'):
        p={'action':'CHAT','speech':'一緒に歩こう。','behavior':none_behavior()}
        if handler=='valid':p['behavior'].update(gesture='nod',expression='happy',expression_intensity=.4,expression_hold=2,spatial_intent='side')
        if handler=='enum':p['behavior']['gesture']='fly'
        result=normalize_reply(p)
        check('nonempty speech and valid action',bool(result['speech']) and result['action']=='CHAT')
        check('safe behavior',result['behavior']['gesture']==('nod' if handler=='valid' else 'none'))
        if handler=='speech_only':check('all body channels none',result['behavior']==none_behavior())
    elif handler=='rescue_json':
        client=FixtureClient(['{broken','自然な短い返事です。']);api=load_api(client)
        result=api.call_agent_llm([{'role':'system','content':'fixture persona'},{'role':'user','content':json.dumps({'userMessage':'fixture'})}])
        check('primary + rescue requested',len(client.calls)==2)
        check('rescue is speech-only',result['behavior']==none_behavior() and result['action']=='CHAT')
    elif handler=='context':
        client=FixtureClient();api=load_api(client);m,meta=builder.build(s,[],key);api.call_agent_llm(m)
        check('observed state actually passed to provider','currently leading the user' in client.calls[0]['messages'][0]['content'])
    elif handler=='chunks':
        p=IncrementalAgentResponseParser();text=json.dumps({'speech':'一緒に歩こう。引用「こんにちは」。','action':'CHAT'},ensure_ascii=True)
        for character in text:p.feed(character)
        check('actual incremental parser reconstructs Unicode speech',p.closed and p.speech==json.loads(text)['speech'])
        segmenter=SentenceSegmenter();parts=[]
        for c in p.speech:parts+=segmenter.feed(c)
        parts+=segmenter.finish();check('sentence order stable',''.join(parts)==p.speech)
    elif handler=='context_cost':
        samples=[]
        for i in range(50):_,meta=builder.build(s,[],key);samples.append(meta['build_ms'])
        metrics={'context_build_ms':{'min':min(samples),'median':statistics.median(samples),'max':max(samples)},'context_chars':meta['context_chars'],'CPU_percent':'unavailable','GPU_percent':'unavailable','GC_alloc':'unavailable'}
        check('valid measured context budget',meta['context_chars']<=meta['budget_characters'])
    elif handler=='thresholds':
        rows=compare({'steady':100,'warn':125,'fail':160,'missing':None},{'steady':100,'warn':100,'fail':100,'missing':100},config['warn_ratio'],config['fail_ratio'])
        check('configured comparison statuses',[x['status'] for x in rows]==['PASS','WARN','FAIL','unavailable'])
    elif handler=='exit_codes':
        required=EvalCase('req','test','fixture','unused');optional=EvalCase('opt','test','fixture','unused',required=False)
        check('required FAIL nonzero',exit_code([EvalResult('req','test','FAIL')],[required])!=0)
        check('required ERROR nonzero',exit_code([EvalResult('req','test','ERROR')],[required])!=0)
        check('optional FAIL does not block',exit_code([EvalResult('opt','test','FAIL')],[optional])==0)
        check('configured SKIP policy',exit_code([EvalResult('req','test','SKIP')],[required],True)!=0 and exit_code([EvalResult('req','test','SKIP')],[required])==0)
        check('configured WARN policy',exit_code([EvalResult('req','test','PASS',warnings=['warning'])],[required],False,True)!=0)
    elif handler.startswith('http_'):
        trace='eval-http-'+uuid.uuid4().hex[:12];key=(trace,1);s.stateSnapshot.update(trace_id=trace,turn_id=1)
        payload={'mode':s.mode,'language':s.language,'sessionId':trace,'userMessage':'NOD','stateSnapshot':s.stateSnapshot};payload['stateSnapshot']['conversation']['session_id']=trace
        def post(route,body):
            req=urllib.request.Request(config['fixture_url']+route,data=json.dumps(body).encode(),headers={'Content-Type':'application/json','X-Trace-Id':trace,'X-Turn-Id':'1'})
            with urllib.request.urlopen(req,timeout=10) as response:return response.status,response.read().decode()
        if handler=='http_malformed':payload['stateSnapshot']['version']='bad'
        route='/agent/stream' if handler=='http_stream' else '/agent'
        status,raw=post(route,payload)
        reply=next((e['reply'] for e in map(json.loads,raw.splitlines()) if e.get('type')=='final_validated'),None) if route.endswith('stream') else json.loads(raw)
        check('actual isolated endpoint responds safely',status==200 and reply and bool(reply['speech']))
        identity={'trace_id':trace,'turn_id':1}
        if handler=='http_history':
            post('/turn/complete',identity);status,second=post('/turn/complete',identity)
            check('HTTP completion idempotent',json.loads(second)['already_completed'])
        else:post('/turn/cancel',dict(identity,reason='fixture_no_playback'))
    elif handler=='live_llm':
        payload={'mode':'general','language':'ja','sessionId':'eval_live_'+uuid.uuid4().hex,'userMessage':'今日は研究を進めました。短く自然に返事して。'}
        trace='eval-live-'+uuid.uuid4().hex[:12];identity={'trace_id':trace,'turn_id':1}
        try:
            req=urllib.request.Request('http://127.0.0.1:8000/agent/stream',data=json.dumps(payload).encode(),headers={'Content-Type':'application/json','X-Trace-Id':trace,'X-Turn-Id':'1'})
            with urllib.request.urlopen(req,timeout=80) as response:events=[json.loads(line) for line in response.read().decode().splitlines()]
        except urllib.error.URLError as e:raise SkipCase('Main Backend/live provider unavailable: '+type(e).__name__)
        reply=next((e['reply'] for e in events if e.get('type')=='final_validated'),None)
        check('real provider schema validated',reply is not None and normalize_reply(reply)==reply)
        req=urllib.request.Request('http://127.0.0.1:8000/turn/cancel',data=json.dumps(dict(identity,reason='eval_no_audio')).encode(),headers={'Content-Type':'application/json'})
        with urllib.request.urlopen(req,timeout=5):pass
        metrics={'provider_live':True,'reply_characters':len(reply['speech'])}
    elif handler=='physical_metrics':raise SkipCase('No new comparable physical-microphone run supplied; retained/fixture audio is not physical E2E')
    else:raise ValueError('Unknown handler: '+handler)
    return {'metrics':metrics,'assertions':assertions}
