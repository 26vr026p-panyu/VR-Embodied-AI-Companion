import argparse,json,time,contextlib,io
from pathlib import Path
from .suites.checks import run as legacy_run
from .suites.tool_checks import run as tool_run
from .suites.native_checks import run as native_run
from .suites.reliability_checks import run as reliability_run
from .suites.performance_checks import run as performance_run
from .suites.asr_runtime_checks import run as asr_runtime_run
from .suites.tts_runtime_checks import run as tts_runtime_run
from .suites.quest_checks import run as quest_run
def run(handler,config):return quest_run(handler,config) if handler.startswith("quest_") else tts_runtime_run(handler,config) if handler.startswith("tts_runtime_") else asr_runtime_run(handler,config) if handler.startswith('asr_') else performance_run(handler,config) if handler.startswith('perf_') else reliability_run(handler,config) if handler.startswith("rel_") else native_run(handler,config) if handler.startswith('native_') else tool_run(handler,config) if handler.startswith('tool_') else legacy_run(handler,config)
from .contracts import SkipCase

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--handler',required=True);parser.add_argument('--config',required=True);parser.add_argument('--output',required=True);a=parser.parse_args()
    started=time.perf_counter();result={}
    try:
        with contextlib.redirect_stdout(io.StringIO()):result=run(a.handler,json.loads(Path(a.config).read_text()))
        result['status']='PASS'
    except SkipCase as e:result=dict(status='SKIP',failure_reason=str(e))
    except AssertionError as e:result=dict(status='FAIL',failure_reason=str(e),assertions=[{'description':str(e),'pass':False}])
    except Exception as e:result=dict(status='ERROR',failure_reason=type(e).__name__+': '+str(e))
    result['duration_ms']=(time.perf_counter()-started)*1000
    Path(a.output).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
if __name__=='__main__':main()

