"""Canonical capability contract. Not imported into LLM prompts or server routes."""
from dataclasses import dataclass
from pathlib import Path
from types import MappingProxyType
import hashlib,json,math,time
from agent_state import AgentStateSnapshot
from .trace import emit

EXECUTORS=frozenset(('gesture.nod','gesture.wave','expression.set','spatial.lead','spatial.side','spatial.trail','spatial.nearby'))

@dataclass(frozen=True)
class CapabilityDescriptor:
    name:str
    serialized:str
    def to_dict(self):return json.loads(self.serialized)

@dataclass(frozen=True)
class ToolCall:
    call_id:str
    tool_name:str
    arguments_json:str
    turn_id:int
    trace_id:str
    state_version:int
    @property
    def arguments(self):return json.loads(self.arguments_json)
    @classmethod
    def parse(cls,value):
        fields={'call_id','tool_name','arguments','turn_id','trace_id','state_version'}
        if not isinstance(value,dict) or set(value)!=fields:raise ValueError('invalid_arguments')
        for key in ('call_id','tool_name','trace_id'):
            text=value[key]
            if not isinstance(text,str) or not text.strip() or len(text)>96 or any(ord(c)<32 for c in text):raise ValueError('invalid_arguments')
        if not isinstance(value['arguments'],dict):raise ValueError('invalid_arguments')
        for key in ('turn_id','state_version'):
            if type(value[key]) is not int or not 0<value[key]<=9223372036854775807:raise ValueError('invalid_arguments')
        args=json.dumps(value['arguments'],allow_nan=False,separators=(',',':'))
        if len(args.encode('utf-8'))>4096:raise ValueError('invalid_arguments')
        return cls(value['call_id'],value['tool_name'],args,value['turn_id'],value['trace_id'],value['state_version'])

class CapabilityRegistry:
    def __init__(self,path=None):
        raw=Path(path or Path(__file__).with_name('capability_manifest.json')).read_bytes()
        manifest=json.loads(raw)
        names=[t['name'] for t in manifest['tools']]
        if manifest['registry_version']!=1 or len(names)!=len(set(names)) or set(names)!=EXECUTORS:raise ValueError('executor_contract_mismatch')
        for t in manifest['tools']:
            required={'name','version','category','description','argument_schema','side_effect','priority_class','idempotency','availability_policy','preconditions','executor','completion_semantics'}
            if set(t)!=required or t['version']!=1 or t['priority_class']!='normal' or t['executor']!='unity.orchestrator':raise ValueError('invalid_manifest')
            s=t['argument_schema']
            if s['type']!='object' or s['additionalProperties'] is not False or not isinstance(s['properties'],dict):raise ValueError('invalid_schema')
        self.manifest_hash=hashlib.sha256(raw).hexdigest();self.registry_version=1
        self._manifest=json.dumps(manifest,separators=(',',':'),ensure_ascii=False)
        self.descriptors=MappingProxyType({t['name']:CapabilityDescriptor(t['name'],json.dumps(t)) for t in manifest['tools']})
        emit('tool_registry_loaded',registry_version=1,manifest_hash=self.manifest_hash)
    def manifest(self):return dict(json.loads(self._manifest),manifest_hash=self.manifest_hash)
    def validate_arguments(self,name,args):
        if name not in self.descriptors:raise ValueError('unknown_tool')
        if not isinstance(args,dict):raise ValueError('invalid_arguments')
        schema=self.descriptors[name].to_dict()['argument_schema'];properties=schema['properties']
        if set(args)-set(properties) or set(schema.get('required',[]))-set(args):raise ValueError('invalid_arguments')
        result={}
        for key,s in properties.items():
            if key not in args and 'default' not in s:continue
            value=args.get(key,s.get('default'))
            if s['type']=='string':
                if not isinstance(value,str) or value not in s['enum']:raise ValueError('invalid_arguments')
            elif type(value) not in (int,float) or not math.isfinite(value) or not s['minimum']<=value<=s['maximum']:raise ValueError('invalid_arguments')
            result[key]=value
        if result.get('expression')=='neutral':result['intensity']=result['hold_seconds']=0
        return result
    def availability(self,snapshot):
        if not isinstance(snapshot,AgentStateSnapshot):raise TypeError('Validated immutable AgentStateSnapshot required')
        start=time.perf_counter();sections=snapshot.sections;turn=sections.get('turn',{});avatar=sections.get('avatar',{});user=sections.get('user',{})
        terminal=turn.get('cancelled',False) or turn.get('lifecycle') in ('completed','failed','cancelled')
        items={}
        for name in self.descriptors:
            reason=None;basis='snapshot';goal=False
            if terminal:reason='invalid_turn'
            elif name.startswith('gesture.'):
                if user.get('is_speaking'):reason='user_speaking'
                elif avatar.get('gesture') not in (None,'none'):reason='busy'
            elif name.startswith('spatial.'):
                mode=name.split('.')[1];effective=avatar.get('spatial_mode');intent=avatar.get('spatial_intent')
                goal=effective==mode
                if intent==mode:reason='already_in_state';basis='preferred_intent'
                elif effective==mode and intent==mode:reason='already_in_state';basis='effective_and_intent'
                # Nearby is not inferred from a stationary effective Stay: Lead can still be the preferred intent.
            items[name]={'available':reason is None,'reason':reason,'basis':basis,'effective_goal_reached':goal}
        duration=(time.perf_counter()-start)*1000
        emit('tool_availability_evaluated',trace_id=snapshot.trace_id,turn_id=snapshot.turn_id,state_version=snapshot.state_version,availability_evaluation_ms=duration)
        return {'registry_version':1,'manifest_hash':self.manifest_hash,'state_version':snapshot.state_version,'tools':items,'availability_evaluation_ms':duration}
    def get_available_tools(self,snapshot):
        availability=self.availability(snapshot)
        return [d.to_dict() for name,d in self.descriptors.items() if availability['tools'][name]['available']]
