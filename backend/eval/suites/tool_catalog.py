from ..contracts import EvalCase

CASES=[]
for suffix,handler in [('manifest','tool_manifest'),('arguments','tool_args'),('availability','tool_availability'),('call_contract','tool_call'),('performance','tool_cost')]:
    CASES.append(EvalCase('tool.'+suffix,'tool','Capability contract '+suffix,handler,level='unit',category='Validation',required=True))

RUNTIME=[
    ('unity_manifest',None),('unknown','unregistered.tool'),('invalid_args','expression.set'),('duplicate','expression.set'),
    ('nod','gesture.nod'),('wave','gesture.wave'),('user_speaking','gesture.wave'),('happy','expression.set'),
    ('range','expression.set'),('neutral','expression.set'),('side','spatial.side'),('already_side','spatial.side'),
    ('lead','spatial.lead'),('stationary_lead','spatial.lead'),('cancelled_turn','gesture.wave'),
    ('cancel_during_dispatch','gesture.nod'),('state_version','expression.set'),('reentrant','expression.set'),
    ('safety','gesture.nod'),('trail','spatial.trail'),('nearby','spatial.nearby'),('unity_performance',None)]
for suffix,tool in RUNTIME:
    CASES.append(EvalCase('tool.'+suffix,'tool','Actual Unity capability '+suffix,'unity',level='integration',category='Behavior',required=True,timeout=300,expected_tool=tool,forbidden_tools=('raw_motion.*','set_position'),expected_result={'contract':'semantic dispatch only'}))
