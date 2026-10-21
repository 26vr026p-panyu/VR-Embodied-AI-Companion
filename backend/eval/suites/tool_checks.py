import json,re,time,statistics
from pathlib import Path
from types import MappingProxyType
from capabilities import CapabilityRegistry,ToolCall
from agent_state import AgentStateSnapshot

def snapshot(intent='side',effective='side',speaking=False,gesture='none',terminal=False):
    sections={'turn':{'lifecycle':'cancelled' if terminal else 'processingllm','cancelled':terminal},'avatar':{'spatial_intent':intent,'spatial_mode':effective,'gesture':gesture},'user':{'is_speaking':speaking,'activity':'stationary' if effective=='stay' else 'moving'}}
    return AgentStateSnapshot('eval',int(time.time()*1000),1,'tool-eval',1,MappingProxyType({k:MappingProxyType(v) for k,v in sections.items()}))

def run(handler,config):
    registry=CapabilityRegistry();assertions=[];metrics={}
    def check(description,condition):
        assertions.append({'description':description,'pass':bool(condition)})
        assert condition,description
    def invalid(name,args,reason='invalid_arguments'):
        try:registry.validate_arguments(name,args)
        except ValueError as e:return str(e)==reason
        return False
    if handler=='tool_manifest':
        import hashlib
        root=Path(__file__).resolve().parents[2];unity=Path(config.get('unity_project','LOCAL_ONLY_PATH_NOT_DISTRIBUTED'))
        resource=unity/'Assets/CompanionCapabilityModule/Resources/CompanionCapabilities/capability_manifest.json'
        check('Unity resource bytes equal canonical manifest hash',hashlib.sha256(resource.read_bytes()).hexdigest()==registry.manifest_hash)
        cs=(unity/'Assets/CompanionCapabilityModule/Runtime/CapabilityRegistry.cs').read_text(encoding='utf-8')
        literal=re.search(r'ExecutorNames=\{([^}]+)\}',cs).group(1)
        check('Unity executor allowlist exactly matches canonical catalog',set(re.findall(r'"([a-z.]+)"',literal))==set(registry.descriptors))
        check('Seven unique registered names',len(registry.descriptors)==7)
        manifest=registry.manifest();manifest['tools'].clear();check('Manifest caller cannot mutate registry',len(registry.manifest()['tools'])==7)
        metrics={'registry_version':1,'manifest_hash':registry.manifest_hash}
    elif handler=='tool_args':
        check('Unknown capability rejected',invalid('raw_motion.run',{},'unknown_tool'))
        check('Invalid semantic enum rejected',invalid('expression.set',{'expression':'angryDragon'}))
        check('No arbitrary XYZ or priority',invalid('spatial.lead',{'x':4}) and invalid('gesture.nod',{'priority':'safety'}))
        check('Required expression missing',invalid('expression.set',{}))
        check('Bounds and strict numeric types',all(invalid('expression.set',{'expression':'happy','intensity':v}) for v in [1.01,-.1,True,None,'0.6',float('nan'),float('inf')]))
        check('Hold bounded',invalid('expression.set',{'expression':'happy','hold_seconds':11}))
        check('Neutral reset semantics',registry.validate_arguments('expression.set',{'expression':'neutral'})=={'expression':'neutral','intensity':0,'hold_seconds':0})
        check('Defaults stable',registry.validate_arguments('expression.set',{'expression':'happy'})=={'expression':'happy','intensity':.6,'hold_seconds':3})
    elif handler=='tool_availability':
        side=registry.availability(snapshot())['tools'];check('Already Side unavailable',side['spatial.side']['reason']=='already_in_state')
        speaking=registry.availability(snapshot(speaking=True))['tools'];check('User speaking filters gestures',not speaking['gesture.wave']['available'])
        busy=registry.availability(snapshot(gesture='nod'))['tools'];check('Existing active gesture filters other gesture',busy['gesture.wave']['reason']=='busy')
        stay=registry.availability(snapshot('lead','stay'))['tools'];check('Stationary intent is not completed Lead',stay['spatial.lead']['basis']=='preferred_intent' and not stay['spatial.lead']['effective_goal_reached'])
        check('Terminal snapshots reject all',all(not t['available'] for t in registry.availability(snapshot(terminal=True))['tools'].values()))
        check('Descriptors available without prompt mutation',all(d['name']!='spatial.side' for d in registry.get_available_tools(snapshot())))
    elif handler=='tool_call':
        v={'call_id':'test','tool_name':'gesture.nod','arguments':{},'turn_id':1,'trace_id':'trace','state_version':1}
        c=ToolCall.parse(v);v['arguments']['unsafe']=1;check('Call freezes arguments',c.arguments=={})
        for patch in [{'turn_id':True},{'state_version':0},{'arguments':[]},{'extra':1},{'call_id':''}]:
            try:ToolCall.parse({**v,'arguments':{},**patch})
            except (ValueError,TypeError):pass
            else:raise AssertionError('Bad ToolCall accepted')
        check('Invalid call types rejected',True)
    elif handler=='tool_cost':
        values=[]
        for _ in range(50):
            began=time.perf_counter();registry.availability(snapshot());registry.validate_arguments('expression.set',{'expression':'happy'});values.append((time.perf_counter()-began)*1000)
        metrics={'availability_plus_validation_ms':{'median':statistics.median(values),'min':min(values),'max':max(values)}}
        check('Measured finite nonnegative cost',0<=metrics['availability_plus_validation_ms']['median']<1000)
    else:raise ValueError(handler)
    return {'assertions':assertions,'metrics':metrics}
