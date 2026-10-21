from ..contracts import EvalCase
CASES=[EvalCase('tool_calling.'+s,'tool_calling','Native protocol '+s,'native_'+s,required=True,expected_tool=t,expected_result={'contract':'native tool channel/result loop'},category='LLM') for s,t in [('alias', None), ('assembly', None), ('no_tool', None), ('wave', 'gesture.wave'), ('lead', 'spatial.lead'), ('expression', 'expression.set'), ('invalid', None), ('unknown', None), ('already_side', None), ('multi', 'expression.set'), ('conflict', 'spatial.trail'), ('cancel', None), ('timeout', None), ('loop', None), ('legacy', None)]]

for n in ['no_tool', 'wave', 'lead', 'expression', 'invalid', 'unknown', 'multi', 'conflict', 'legacy']:
    CASES.append(EvalCase('tool_calling.unity_'+n,'tool_calling','Actual Unity native loop '+n,'unity',level='integration',category='LLM',required=True,timeout=300))

for n,t in [('wave', 'gesture.wave'), ('lead', 'spatial.lead'), ('side', 'spatial.side'), ('happy', 'expression.set'), ('chat', None), ('already_side', None), ('nod', 'gesture.nod'), ('nearby', 'spatial.nearby')]:
    CASES.append(EvalCase('tool_calling.live_'+n,'tool_calling','Real DeepSeek selection '+n,'unity',level='live',category='LLM',required=True,timeout=300,expected_tool=t))

for n in ('prefix_identity','result_identity'):
    CASES.append(EvalCase('tool_calling.'+n,'tool_calling','Native identity '+n,'native_'+n,required=True,category='Validation'))

for n in ('policy','grounding_contract'):
    CASES.append(EvalCase('tool_calling.'+n,'tool_calling','Selection and grounding contract '+n,'native_'+n,required=True,category='Validation'))
