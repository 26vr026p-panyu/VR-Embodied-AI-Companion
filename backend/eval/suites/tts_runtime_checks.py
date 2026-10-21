"""Actual Roxy/Unity evidence gates. Missing measurements are SKIP, never inferred PASS."""
import json
from pathlib import Path
from ..contracts import SkipCase
def run(handler,config):
    path=Path(config.get('tts_runtime_report',r'LOCAL_ONLY_PATH_NOT_DISTRIBUTED'))
    if not path.exists():raise SkipCase('Roxy runtime evidence not completed')
    r=json.loads(path.read_text(encoding='utf-8'));a=[]
    def check(name,value):
        a.append({'description':name,'pass':bool(value)})
        if not value:raise AssertionError(name)
    name=handler.removeprefix('tts_runtime_')
    if name=='ready_state':
        check('Cold health distinguishes model load from reference readiness',r['cold_health']['model_loaded'] and not r['cold_health']['synthesis_ready'])
        check('Live production health confirms synthesis readiness',all(r['production_health'][x] for x in ('process_alive','model_loaded','reference_prepared','synthesis_ready')))
    elif name=='first_pcm_cold':
        check('Real short and medium cold first PCM recorded',all(r['service_bench'][k][0]['first_pcm_ms']>0 for k in ('cold','cold-medium')))
    elif name=='first_pcm_warm':
        check('Three same-text warm samples measured',all(len(r['service_bench'][k])>=3 for k in ('warmup','warm-medium')))
        check('Startup synthesis audio discarded',r['startup_warmup']['event']=='startup_warmup_discarded')
    elif name=='true_underrun':
        check('12 mixed production utterances actually completed',len(r['production_normal'])>=12 and all(x['completed_count']==1 for x in r['production_normal']))
        check('No true underrun including actual LLM stress',r['production_true_underruns']==0)
        check('Unity runtime exception count zero',r['production_runtime_errors']==0)
    elif name=='buffer_depth':
        check('Actual AudioClip prefetch measured',all(x['streams'] and x['streams'][0]['prefill_samples']>0 for x in r['production_normal']))
        check('Bounded profiling dropped no event',all(s['dropped']==0 for x in r['production_rows'] for s in x['streams']))
        check('EOS drain distinct from producer-active shortage',r['cold_true_underruns']>0 and all(e['producer_active'] for x in r['cold_rows'] for s in x['streams'] for e in s['underrun_events']))
    elif name=='barge_cancel':
        x=r['cancel'];check('Active stream interrupted exactly once',x['interrupted_count']==1 and x['play_started_s']>0)
        check('Source stopped and mouth closed',x['source_stopped'] and x['lip_closed'])
    else:raise ValueError(handler)
    return dict(metrics={'measurement_basis':'actual_native_roxy_and_unity_play','case':name},assertions=a)
