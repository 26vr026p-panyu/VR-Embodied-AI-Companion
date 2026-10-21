"""Deterministic failure policy. No audio, avatar, network or history execution."""
from dataclasses import dataclass, asdict, field
from enum import Enum
from collections import Counter
from pathlib import Path
import json, os, time, uuid, threading

class FailureCategory(str, Enum):
    Input='Input'; State='State'; ASR='ASR'; LLM='LLM'; Validation='Validation'; Tool='Tool'
    TTS='TTS'; Playback='Playback'; Cancellation='Cancellation'; History='History'
    Infrastructure='Infrastructure'; Performance='Performance'
class Severity(str, Enum):
    Info='Info'; Warning='Warning'; Recoverable='Recoverable'; TurnFatal='TurnFatal'; SystemFatal='SystemFatal'
class Decision(str, Enum):
    Retry='Retry'; Fallback='Fallback'; Degrade='Degrade'; AbortTurn='AbortTurn'; Ignore='Ignore'; Escalate='Escalate'
class Health(str, Enum):
    Healthy='Healthy'; Degraded='Degraded'; Unavailable='Unavailable'

GRAPH={'native_stream':('legacy_stream',), 'legacy_stream':('complete_agent',),
       'llm_stream':('complete_agent',), 'tts_stream':('complete_tts',),
       'complete_tts':('edge_tts',), 'invalid_state':('minimal_context',),
       'experimental_asr_stream':('complete_wav',)}
NORMAL_REJECTIONS=frozenset(('already_in_state','user_speaking','busy','cooldown','policy_rejected'))
RECOVERY_TEXT={'ja':'ごめん、今うまく返せなかった。','zh':'抱歉，这次没能回答。'}

def validate_graph(graph=GRAPH):
    def visit(node,path):
        if node in path: raise ValueError('fallback_cycle')
        if len(path)>2: raise ValueError('fallback_depth')
        for target in graph.get(node,()): visit(target,path+(node,))
    for node in graph: visit(node,())
    return True
validate_graph()

@dataclass(frozen=True)
class FailureRecord:
    trace_id:str; turn_id:int; component:str; category:FailureCategory; code:str
    severity:Severity=Severity.Recoverable
    recoverability:str='policy'
    failure_id:str=field(default_factory=lambda:uuid.uuid4().hex)
    occurred_at:int=field(default_factory=lambda:time.time_ns()//1000000)
    message_safe:str='Component operation failed.'
    retry_count:int=0; fallback_attempted:bool=False; fallback_target:str|None=None

@dataclass(frozen=True)
class ReliabilityDecision:
    decision:Decision; reason:str; target:str|None=None; max_attempts:int=0

@dataclass
class RecoveryBudget:
    path:list=field(default_factory=list)
    retries:dict=field(default_factory=dict)
    def fallback(self,source,target,cancelled=False):
        if cancelled or len(self.path)>=2 or target not in GRAPH.get(source,()): return False
        if any(target in pair for pair in self.path): return False
        if self.path and self.path[-1][1]!=source: return False
        self.path.append((source,target)); return True
    def retry(self,component,cancelled=False):
        if cancelled or self.retries.get(component,0)>=1:return False
        self.retries[component]=1;return True

class ReliabilityPolicy:
    @staticmethod
    def decide(failure, *, cancelled=False, output=False, tool_requested=False, depth=0):
        if cancelled:return ReliabilityDecision(Decision.Ignore,'cancelled')
        if failure.code in NORMAL_REJECTIONS:return ReliabilityDecision(Decision.Ignore,'normal_tool_result')
        if failure.code=='invalid_state':return ReliabilityDecision(Decision.Degrade,'invalid_optional_state','minimal_context')
        if failure.code=='tool_result_timeout':return ReliabilityDecision(Decision.Degrade,'execution_unknown','tool_result_failed')
        if failure.code=='manifest_mismatch':return ReliabilityDecision(Decision.Escalate,'executor_contract_mismatch','disable_native_tools')
        if failure.component=='complete_agent' and failure.code=='primary_attempt_failed':
            return ReliabilityDecision(Decision.Retry,'existing_modified_input_rescue','json_rescue',1)
        if depth>=2:return ReliabilityDecision(Decision.AbortTurn,'fallback_budget_exhausted')
        if failure.component=='native_stream' and not output and not tool_requested:
            return ReliabilityDecision(Decision.Fallback,'before_dispatch','legacy_stream',1)
        if failure.component in ('llm_stream','legacy_stream') and not output and not tool_requested:
            return ReliabilityDecision(Decision.Fallback,'before_output','complete_agent',1)
        if failure.component=='tts_stream' and not output:
            return ReliabilityDecision(Decision.Fallback,'before_playback','complete_tts',1)
        if failure.component=='complete_tts' and failure.code=='provider_unavailable':
            return ReliabilityDecision(Decision.Fallback,'provider_unavailable','edge_tts',1)
        return ReliabilityDecision(Decision.AbortTurn,'no_safe_replay')

class ReliabilityMonitor:
    def __init__(self):
        self.lock=threading.RLock();self.metrics=Counter();self.health={k:Health.Healthy for k in ('LLM','TTS','Tool','Backend','ASR')}
        self.origin=time.perf_counter()
    def event(self,event,failure=None,decision=None,**meta):
        # Only caller-owned machine identifiers; never exception text or request payload.
        row=dict(event=event,component='backend',utc_ms=time.time_ns()//1000000,mono_ms=(time.perf_counter()-self.origin)*1000)
        if failure:row.update(asdict(failure))
        if decision:row.update(decision=decision.decision.value,reason=decision.reason,target=decision.target,fallback_target=decision.target,max_attempts=decision.max_attempts)
        row.update(meta)
        try:
            with self.lock:
                self.metrics[event]+=1
                if event=='failure_detected':
                    self.metrics['component:'+failure.component]+=1;self.metrics['category:'+failure.category.value]+=1
                target=Path(os.environ.get('RELIABILITY_TRACE_DIR',str(Path(__file__).parent/'reliability_traces')))
                target.mkdir(parents=True,exist_ok=True)
                with (target/f'reliability-{os.getpid()}.jsonl').open('a',encoding='utf-8') as out:out.write(json.dumps(row)+'\n')
        except (OSError,ValueError,TypeError):pass # Observability must fail open.
    def failure(self,failure,**context):
        began=time.perf_counter();decision=ReliabilityPolicy.decide(failure,**context)
        if decision.reason!='normal_tool_result':self.event('failure_detected',failure)
        self.event('reliability_decision',failure,decision,policy_ms=(time.perf_counter()-began)*1000,fallback_depth=context.get('depth',0))
        if decision.decision==Decision.Degrade:self.event('turn_degraded',failure,decision)
        if decision.decision==Decision.AbortTurn:self.event('turn_aborted',failure,decision)
        return decision
    def set_health(self,component,health):
        with self.lock:
            if self.health.get(component)==health:return
            self.health[component]=health;self.event('component_health_changed',health_component=component,health=health.value)
    def snapshot(self):
        with self.lock:
            values=dict(self.metrics)
            for kind in ('retry','fallback'):
                n=values.get(kind+'_started',0);values[kind+'_success_rate']=values.get(kind+'_succeeded',0)/n if n else None
            return dict(metrics=values,health={k:v.value for k,v in self.health.items()})

monitor=ReliabilityMonitor()
