"""Single context selection/budget policy for complete and streaming requests."""
from dataclasses import dataclass
import json, time, os, re
from pathlib import Path
from agent_state import validate_snapshot, semantic_state
from companion_persona import build_system_prompt, build_user_context
from latency_trace import emit

@dataclass(frozen=True)
class ContextBudget:
    max_recent_turns: int = 8
    max_context_characters: int = 16000

class AgentContextBuilder:
    def __init__(self,budget=None):
        self.budget=budget or ContextBudget()
        if not 0 <= self.budget.max_recent_turns <= 20 or self.budget.max_context_characters < 1024: raise ValueError('context_budget')

    def build(self,state,committed_history,identity,required=False,reason='none'):
        began=time.perf_counter()
        payload=getattr(state,'stateSnapshot',None)
        emit('state_snapshot_received',snapshot_version=payload.get('version') if isinstance(payload,dict) else None)
        snapshot,status=validate_snapshot(payload,identity,state.mode,state.language,state.sessionId)
        age=int(time.time()*1000)-snapshot.created_at_ms if snapshot else None
        emit('state_snapshot_validated',status=status,snapshot_age_ms=age,snapshot_version=1 if snapshot else None)
        emit('context_build_started')
        persona=build_system_prompt(state.mode)
        # Persona is frozen and must never be truncated by history or input growth.
        if len(persona)+256 > self.budget.max_context_characters: raise ValueError('budget_below_frozen_persona')
        # Read existing committed history only; complete user/assistant pairs are selected together.
        pairs=[]
        history_view=list(committed_history)
        for i in range(0,len(history_view)-1,2):
            u,a=history_view[i:i+2]
            if u.get('role')=='user' and a.get('role')=='assistant':
                pairs.append([dict(u),{'role':'assistant','content':json.dumps({'speech':a['content']},ensure_ascii=False)}])
        pairs=pairs[-self.budget.max_recent_turns:] if self.budget.max_recent_turns else []
        summary=semantic_state(snapshot)
        current=json.loads(build_user_context(state,required,reason))
        # The immutable persona already supplies the CURRENT MODE overlay; retain the same user JSON.
        def system(): return persona+('\n\nCURRENT STATE (observed at request start):\n'+summary if summary else '')
        def messages(): return [{'role':'system','content':system()}]+[m for p in pairs for m in p]+[{'role':'user','content':json.dumps(current,ensure_ascii=False)}]
        def chars(ms): return sum(len(m['content']) for m in ms)
        while pairs and chars(messages())>self.budget.max_context_characters: pairs.pop(0)
        if chars(messages())>self.budget.max_context_characters:
            # User input has bounded space. No raw string slicing of JSON or Persona.
            current['userMessage']=''
            room=self.budget.max_context_characters-chars(messages())
            original=state.userMessage
            lo,hi=0,max(0,min(len(original),room))
            while lo<hi:
                mid=(lo+hi+1)//2; current['userMessage']=original[:mid]
                if chars(messages())<=self.budget.max_context_characters: lo=mid
                else: hi=mid-1
            current['userMessage']=original[:lo]
        result=messages()
        if chars(result)>self.budget.max_context_characters:
            summary=''; result=messages()
        if chars(result)>self.budget.max_context_characters: raise ValueError('minimum_context_exceeds_budget')
        sections=['system_persona','current_mode','current_user_input']
        if summary: sections.append('current_state')
        if pairs: sections.append('recent_conversation')
        metadata={'degraded_context':snapshot is None,'snapshot_status':status,'snapshot_id':snapshot.snapshot_id if snapshot else None,'snapshot_version':1 if snapshot else None,'snapshot_age_ms':age,'context_chars':chars(result),'approximate_tokens':(chars(result)+2)//3,'recent_turn_count':len(pairs),'included_sections':sections,'state_summary':summary,'build_ms':(time.perf_counter()-began)*1000,'budget_characters':self.budget.max_context_characters,'user_input_truncated':current['userMessage']!=state.userMessage}
        emit('context_build_completed',duration_ms=metadata['build_ms'],context_chars=metadata['context_chars'],recent_turn_count=len(pairs),included_sections=sections)
        # Optional development/evaluation artifacts. No prompts, conversation text or credentials.
        target=os.environ.get('AGENT_CONTEXT_EVAL_DIR')
        if target and identity:
            try:
                directory=Path(target)/re.sub(r'[^A-Za-z0-9_-]','_',identity[0])
                directory.mkdir(parents=True,exist_ok=True)
                (directory/'context_summary.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
                if snapshot:
                    clean={name:dict(values) for name,values in snapshot.sections.items()}
                    clean.update(version=1,snapshot_id=snapshot.snapshot_id,created_at_ms=snapshot.created_at_ms,turn_id=snapshot.turn_id,trace_id=snapshot.trace_id,state_version=snapshot.state_version)
                    (directory/'state_snapshot.json').write_text(json.dumps(clean,ensure_ascii=False,indent=2),encoding='utf-8')
            except (OSError,ValueError): pass
        return result,metadata

builder=AgentContextBuilder()
def build_agent_messages(state,history,identity,required=False,reason='none'):
    return builder.build(state,history,identity,required,reason)[0]
