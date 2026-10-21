import json
from eval.metrics.profiling import PerformanceSnapshot,validate,validate_budget,waterfall

def run(handler,config):
    assertions=[]
    def check(name,value):
        assertions.append(dict(description=name,pass_=bool(value)))
        if not value:raise AssertionError(name)
    snapshot=PerformanceSnapshot('unit-perf','2026-10-11T00:00:00Z',{'fixture':True},'contract',{'metric':None},unavailable_metrics={'metric':'not observed'},measurement_basis='deterministic_contract_fixture')
    if handler=='perf_integrity':
        rows=[dict(component='unity',clock_id='a',trace_id='x',event='asr_request_started',mono_ms=10),dict(component='backend',clock_id='b',trace_id='x',event='asr_final_received',mono_ms=100)]
        check('Never subtract monotonic timestamps across processes',all(r['metrics']['asr_ms'] is None for r in waterfall(rows)))
        rows.append(dict(component='unity',clock_id='a',trace_id='x',event='asr_final_received',mono_ms=30))
        check('Same-clock observed interval exact',waterfall(rows)[0]['metrics']['asr_ms']==20)
    elif handler=='perf_no_fake':
        check('Missing remains null',json.loads(snapshot.to_json())['metrics']['metric'] is None)
        check('Missing event has no fabricated zero',all(v is None for v in waterfall([dict(component='unity',clock_id='x',trace_id='t',event='turn_started',mono_ms=1)])[0]['metrics'].values()))
        snapshot.metrics['bad']=float('nan')
        try:validate(snapshot);rejected=False
        except ValueError:rejected=True
        check('NaN rejected',rejected)
    elif handler=='perf_budget':
        check('Budget explicit rationale and ordering',validate_budget({'x':dict(unit='ms',target=1,warning=2,failure=3,basis='engineering target, not measured baseline')}))
        try:validate_budget({'x':dict(unit='ms',target=3,warning=2,failure=1,basis='invalid')});rejected=False
        except ValueError:rejected=True
        check('Bad budget fails',rejected)
    elif handler=='perf_serialization':
        check('Snapshot schema valid',validate(snapshot));decoded=json.loads(snapshot.to_json());check('Snapshot reproducible',decoded['run_id']==snapshot.run_id and decoded['unavailable_metrics']==snapshot.unavailable_metrics)
    else:raise ValueError(handler)
    for a in assertions:a['pass']=a.pop('pass_')
    return dict(metrics={'measurement_basis':'deterministic_contract_fixture'},assertions=assertions)
