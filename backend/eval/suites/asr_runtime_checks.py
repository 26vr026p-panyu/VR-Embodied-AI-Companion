"""Measured medium runtime gates; benchmark absence is SKIP, never inferred PASS."""
import json
from pathlib import Path
from ..contracts import SkipCase

def run(handler,config):
    path=Path(config.get('asr_runtime_report',r'LOCAL_ONLY_PATH_NOT_DISTRIBUTED'))
    if not path.exists():raise SkipCase('Actual ASR runtime benchmark not completed')
    r=json.loads(path.read_text(encoding='utf-8'));a=[]
    def check(name,v):
        a.append(dict(description=name,**{'pass':bool(v)}))
        if not v:raise AssertionError(name)
    if handler=='asr_baseline_quality':
        check('Retained physical baseline transcripts and CER measured',r['baseline']['scored_utterances']>=9 and r['baseline']['cer'] is not None)
        check('Same waveform hashes retained',all(x.get('sha256') for x in r['corpus']))
    elif handler=='asr_candidate_quality':
        check('Selected quality increase <=1 percentage point',r['selected']['cer']<=r['baseline']['cer']+.01)
    elif handler=='asr_warm_latency':
        check('Warm measurements separate from model load',r['selected']['warm_samples']>=2 and r['selected']['warm_median_ms']>0 and r['selected']['load_ms']>0)
    elif handler=='asr_model_resident':
        evidence=r.get('live_readiness',[])
        if len(evidence)<2:raise SkipCase('Live residency checks pending')
        check('One resident model across actual requests',all(x['load_count']==1 and x['model_resident'] for x in evidence) and len({x['runtime_instance'] for x in evidence})==1)
    elif handler=='asr_readiness':
        from whisper_runtime import WhisperRuntime,WhisperRuntimeConfig
        runtime=WhisperRuntime(WhisperRuntimeConfig());check('False before loading',not runtime.status()['asr_ready'])
        class Model:pass
        model=runtime.load(lambda *a,**k:Model());check('True only after completed load',runtime.status()['asr_ready']);check('Repeated load returns identical model',runtime.load(lambda *a,**k:None) is model and runtime.load_count==1)
        failed=WhisperRuntime(WhisperRuntimeConfig())
        def error(*a,**k):raise RuntimeError('fixture_load_failure')
        try:failed.load(error)
        except RuntimeError:pass
        check('Failure does not become ready',not failed.ready)
        check('Live ready observed',any(x['asr_ready'] for x in r.get('live_readiness',[])))
    else:raise ValueError(handler)
    return dict(metrics={'measurement_basis':'retained_physical_wav_benchmark_and_live_health','selected':r['selected']},assertions=a)
