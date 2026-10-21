"""Controlled policy and actual module tests; no production failure injection."""
import json,time,threading,statistics,asyncio
from types import SimpleNamespace
from pathlib import Path
from reliability import *

def run(handler,config):
    name=handler.removeprefix('rel_'); assertions=[];metrics={}
    def check(text,ok):
        assertions.append({'description':text,'pass':bool(ok)});assert ok,text
    def decide(component='llm_stream',code='failed',**kwargs):
        return ReliabilityPolicy.decide(FailureRecord('fixture',1,component,FailureCategory.LLM,code),**kwargs)
    if name=='llm_pre':check('pre-output complete fallback',decide().target=='complete_agent')
    elif name=='llm_post':check('post-output never replay',decide(output=True).decision==Decision.AbortTurn)
    elif name=='tts_pre':check('pre-playback complete fallback',decide('tts_stream').target=='complete_tts')
    elif name=='tts_post':check('partial audio never replay',decide('tts_stream',output=True).decision==Decision.AbortTurn)
    elif name=='normal_rejection':
        for code in NORMAL_REJECTIONS:check(code,decide('Tool',code).reason=='normal_tool_result')
    elif name=='tool_timeout':check('uncertain execution no retry',decide('Tool','tool_result_timeout',tool_requested=True).reason=='execution_unknown')
    elif name=='native_post':check('native requested forbids legacy',decide('native_stream',tool_requested=True).decision==Decision.AbortTurn)
    elif name=='cancel_priority':
        for component in ('llm_stream','native_stream','tts_stream','complete_tts'):
            check(component,decide(component,'provider_unavailable',cancelled=True).decision==Decision.Ignore)
    elif name=='loop':
        check('production graph acyclic bounded',validate_graph())
        try:validate_graph({'A':('B',),'B':('A',)})
        except ValueError:check('cycle rejected',True)
        else:check('cycle rejected',False)
    elif name=='depth':
        b=RecoveryBudget();check('first',b.fallback('native_stream','legacy_stream'));check('second',b.fallback('legacy_stream','complete_agent'));check('third blocked',not b.fallback('complete_agent','native_stream'))
    elif name=='fallback_failed':check('terminal fallback aborts',decide('complete_agent',depth=2).decision==Decision.AbortTurn)
    elif name=='retry_budget':
        b=RecoveryBudget();check('at most one',b.retry('LLM') and not b.retry('LLM'));check('cancel denies retry',not b.retry('TTS',True))
    elif name=='invalid_state':
        from eval.fixtures.native_client import NativeFixtureClient
        from eval.fixtures.source_loader import load_api
        from native_tools import native_stream,PendingToolResults
        from turn_lifecycle import TurnCancellationRegistry
        client=NativeFixtureClient();api=load_api(client);api.turn_registry=TurnCancellationRegistry();key=('invalid-state',1);api.turn_registry.register(key)
        state=api.ExerciseState(mode='general',language='ja',sessionId='invalid',userMessage='こんにちは',stateSnapshot={'version':999})
        messages=api.build_agent_messages(state,[],key);events=[];holder={}
        chunks=list(native_stream(api,state,key,messages,events.append,threading.Event(),holder,PendingToolResults()))
        check('conversation survives',bool(chunks));check('native guard remains to suppress legacy body',holder['native_mode'] is True)
        check('context marked degraded',holder['degraded_context']);check('no body request',not any(e['type']=='tool_call_requested' for e in events))
        check('provider not exposed tools',all(not c.get('tools') for c in client.calls))
    elif name=='actual_timeout':
        from eval.suites.native_checks import exercise
        client,dispatch,speech,events,broker,holder,api=exercise('NT_TIMEOUT')
        check('one dispatch only',len(dispatch)==1);check('model continues',bool(speech));check('pending closed',not broker.pending)
        check('negative grounding remains',any('tool_result_timeout' in json.dumps(c,ensure_ascii=False) for c in client.calls))
    elif name in ('history_once','history_cancel','cancel_race'):
        from turn_lifecycle import TurnCancellationRegistry
        from fastapi import HTTPException
        r=TurnCancellationRegistry();key=('fixture',1);r.register(key);sessions={}
        if name=='history_once':
            r.prepare_reply(key,'session','u','a');check('one commit',r.complete(key,sessions)['committed']);check('duplicate prevented',not r.complete(key,sessions)['committed']);check('one pair',len(sessions['session'])==2)
        else:
            r.prepare_reply(key,'session','u','a');r.cancel(key,'fixture');
            try:r.complete(key,sessions)
            except HTTPException:pass
            else:check('cancelled commit rejected',False)
            check('no history',not sessions)
            try:r.check(key,'late_fallback')
            except HTTPException:check('late result rejected',True)
            else:check('late result rejected',False)
    elif name=='health':
        m=ReliabilityMonitor();m.set_health('LLM',Health.Degraded);m.set_health('LLM',Health.Healthy);check('real-result transitions',m.snapshot()['health']['LLM']=='Healthy')
    elif name=='safe_record':
        f=FailureRecord('fixture',1,'LLM',FailureCategory.LLM,'failed');check('metadata only',not any(x in json.dumps(asdict(f)) for x in ('Authorization','api_key','raw_audio','private_prompt')))
    elif name=='known_issue':
        j=json.loads(Path('eval/known_issues.json').read_text());check('historical known issue evidence not erased',[e['underruns'] for e in j['evidence'][:2]]==[14,8] and j['first_seen'])
        check('known issue has valid status',j['status'] in ('OPEN','RESOLVED'))
        if j['status']=='RESOLVED':
            evidence=j['evidence'][-1];check('resolution includes real repeated-test report',evidence['result']=='PASS' and evidence['underruns']==0 and evidence['utterances']>=10 and Path(evidence['report']).is_file())
            measured=json.loads(Path(evidence['report']).read_text(encoding='utf-8'))
            check('resolution supported by actual production measurements',measured['production_true_underruns']==0 and len(measured['production_normal'])>=10 and measured['production_runtime_errors']==0)
    elif name=='overhead':
        f=FailureRecord('fixture',1,'llm_stream',FailureCategory.LLM,'failed');times=[]
        for _ in range(1000):
            t=time.perf_counter();ReliabilityPolicy.decide(f);times.append((time.perf_counter()-t)*1000)
        metrics={'policy_median_ms':statistics.median(times),'policy_max_ms':max(times)};check('pure bounded decision',max(times)<50)
    elif name=='actual_rescue_failure':
        from eval.fixtures.client import FixtureClient
        from eval.fixtures.source_loader import load_api
        client=FixtureClient(['{broken','{stillbroken']);api=load_api(client)
        try:api.call_agent_llm([{'role':'system','content':'fixture'},{'role':'user','content':json.dumps({'userMessage':'fixture'})}])
        except ValueError:pass
        else:check('both fail terminates',False)
        check('existing rescue budget exactly one',len(client.calls)==2)
        check('repair changes input instead of mechanical same-input retry',client.calls[0]['messages']!=client.calls[1]['messages'])
    elif name=='edge_cancel':
        from tts.router import TTSProviderRouter
        from tts.models import SpeechRequest
        from fastapi import HTTPException
        from unittest.mock import patch
        counts={'primary':0,'edge':0}
        class Primary:
            async def synthesize(self,r):counts['primary']+=1;raise HTTPException(409,'turn_cancelled')
        class Edge:
            async def synthesize(self,r):counts['edge']+=1;raise AssertionError('cancel must not invoke edge')
        router=TTSProviderRouter(providers={'gpt_sovits':Primary(),'edge':Edge()})
        async def test():
            try:await router.synthesize(SpeechRequest('fixture'))
            except HTTPException:pass
            else:check('cancel rethrown',False)
        with patch('tts.router.check_turn',return_value=None):asyncio.run(test())
        check('cancel wins',counts=={'primary':1,'edge':0})
    elif name=='edge_router':
        from tts.router import TTSProviderRouter
        from tts.models import SpeechRequest,SpeechProviderError
        from unittest.mock import patch
        counts={'primary':0,'edge':0}
        class Bad:
            async def synthesize(self,request):counts['primary']+=1;raise SpeechProviderError('fixture_unavailable')
        class Edge:
            async def synthesize(self,request):counts['edge']+=1;raise SpeechProviderError('fixture_edge_unavailable')
        router=TTSProviderRouter(providers={'gpt_sovits':Bad(),'edge':Edge()})
        async def test():
            try:await router.synthesize(SpeechRequest(text='fixture',language='ja'))
            except SpeechProviderError:pass
            else:check('both fail terminates',False)
        with patch('tts.router.check_turn',return_value=None):asyncio.run(test())
        check('no provider loop or retry',counts=={'primary':1,'edge':1})
    else:raise AssertionError(name)
    return {'assertions':assertions,'metrics':metrics}
