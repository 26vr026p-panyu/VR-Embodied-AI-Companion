"""Unified repeatable engineering evaluation. Default: unit + integration; live opt-in."""
import argparse,json,os,sys,time,uuid,hashlib,subprocess,socket,urllib.request
from pathlib import Path
from collections import Counter
from datetime import datetime,timezone
from .contracts import EvalResult,exit_code
from .suites.catalog import CASES

def hashes(backend,unity):
    files=list(backend.glob('*.py'))+list((backend/'tts').glob('*.py'))+[backend/'tts/config.json',backend/'VR_ExerciseAgent_TTS/voice_config.json',backend/'VR_ExerciseAgent_TTS/voice_style_bank.json']
    for folder in ['ExerciseAgent/Scripts','CompanionBehaviorModule/Runtime','AgentContextModule/Runtime','AvatarMotionModule/Runtime','CompanionFollowModule/Runtime','CompanionGazeModule/Runtime','LipSyncModule/Runtime','DemoPresentation/Runtime','Scripts']:
        files+=list((unity/'Assets'/folder).glob('*.cs'))
    files.append(unity/'Assets/WindsongIntegration/Scenes/Main_WindsongIntegration.unity')
    return {str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.exists()}

def free_port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('selector',nargs='?',default='all',help='all, suite name or full case ID')
    parser.add_argument('--levels',default='unit,integration');parser.add_argument('--live',action='store_true')
    parser.add_argument('--baseline');parser.add_argument('--output');parser.add_argument('--config')
    parser.add_argument('--current-metrics',help='Comparable physical summary: run_id, measurement_basis=physical, latency_ms')
    parser.add_argument('--unity-project',default=r'LOCAL_ONLY_PATH_NOT_DISTRIBUTED');parser.add_argument('--unity-bridge')
    parser.add_argument('--retained-wav',help='Authorized local-only WAV; never copied into fixtures')
    parser.add_argument('--skip-is-failure',action='store_true');parser.add_argument('--warn-is-failure',action='store_true')
    parser.add_argument('--native-live',action='store_true',help='Opt-in real DeepSeek native selection, synthetic TTS fixture')
    parser.add_argument('--list',action='store_true')
    a=parser.parse_args();backend=Path(__file__).resolve().parent.parent
    config=json.loads(Path(a.config or Path(__file__).with_name('config.json')).read_text(encoding='utf-8-sig'))
    if config['fail_ratio']<=config['warn_ratio'] or config['warn_ratio']<1:parser.error('Invalid baseline thresholds')
    levels=set(a.levels.split(','));levels.update(['live'] if a.live or a.native_live else [])
    if not levels<=set(['unit','integration','live','hardware']):parser.error('Unknown level')
    selected=[c for c in CASES if c.level in levels and any(selector in ('all',c.suite,c.id) for selector in a.selector.split(','))]
    if not selected:parser.error('No matching cases')
    if a.list:
        print('\n'.join(f'{c.id}\t{c.level}\t'+('required' if c.required else 'optional') for c in selected));return 0
    run_id=datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'-'+uuid.uuid4().hex[:8]
    output=Path(a.output or backend/'eval/reports'/run_id).resolve();output.mkdir(parents=True,exist_ok=True)
    (output/'cases').mkdir(exist_ok=True)
    started=time.perf_counter();before=hashes(backend,Path(a.unity_project));results=[];fixture=None;fixture_log=None;unity_data=None;unity_reason='Unity integration driver unavailable'
    unity_cases=[c for c in selected if c.handler=='unity']
    env=os.environ.copy();env.pop('AGENT_CONTEXT_EVAL_DIR',None);env['LATENCY_TRACE_DIR']=str(output/'fixture-traces');env['CAPABILITY_TRACE_DIR']=str(output/'tool-traces');env['NATIVE_TOOL_TRACE_DIR']=str(output/'native-tool-traces');env['RELIABILITY_TRACE_DIR']=str(output/'reliability-traces')
    if a.native_live:env['EVAL_NATIVE_LIVE']='1'
    env['PYTHONPATH']=str(backend)
    config['unity_project']=a.unity_project
    config['unity_project']=a.unity_project
    try:
        if any(c.level=='integration' and c.handler!='unity' for c in selected) or unity_cases:
            from .fixtures.tone import prepare_mp3
            ffmpeg=backend/'VR_ExerciseAgent_TTS/GPT-SoVITS/ffmpeg.exe'
            if not ffmpeg.exists():raise FileNotFoundError('Synthetic audio fixture encoder unavailable')
            prepare_mp3(output/'fixtures/tone.mp3',ffmpeg);env['EVAL_TONE_MP3']=str(output/'fixtures/tone.mp3')
            port=free_port();config['fixture_url']=f'http://127.0.0.1:{port}'
            fixture_log=(output/'fixture-service.log').open('w',encoding='utf-8')
            fixture=subprocess.Popen([sys.executable,'-m','uvicorn','eval.fixtures.service:app','--host','127.0.0.1','--port',str(port),'--no-use-colors'],cwd=backend,env=env,stdout=fixture_log,stderr=fixture_log,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
            end=time.monotonic()+15
            while time.monotonic()<end:
                if fixture.poll() is not None:break
                try:
                    with urllib.request.urlopen(config['fixture_url']+'/health',timeout=.5) as response:
                        if json.load(response).get('fixture'):break
                except Exception:time.sleep(.1)
            else:raise RuntimeError('Fixture startup timeout')
            if fixture.poll() is not None:raise RuntimeError('Fixture service startup failed; inspect local log')
        config_path=output/'run-config.json';config_path.write_text(json.dumps(config,indent=2))
        # Run each Python case in a subprocess to enforce timeouts, preserving production semantics.
        for c in selected:
            if c.handler=='unity':continue
            case_file=output/'cases'/f'{c.id}.json';began=time.perf_counter()
            try:
                completed=subprocess.run([sys.executable,'-m','eval.worker','--handler',c.handler,'--config',str(config_path),'--output',str(case_file)],cwd=backend,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=c.timeout)
                if not case_file.exists():raise RuntimeError('Worker did not produce result: '+completed.stderr.decode(errors='replace')[-500:])
                row=json.loads(case_file.read_text(encoding='utf-8'))
                results.append(EvalResult(c.id,c.suite,row.pop('status'),category=c.category,**row))
            except subprocess.TimeoutExpired:results.append(EvalResult(c.id,c.suite,'ERROR',failure_reason='Case timeout',category='Infrastructure',duration_ms=(time.perf_counter()-began)*1000))
            except Exception as e:results.append(EvalResult(c.id,c.suite,'ERROR',failure_reason=type(e).__name__+': '+str(e),category='Infrastructure',duration_ms=(time.perf_counter()-began)*1000))
        if unity_cases and a.unity_bridge:
            bridge=Path(a.unity_bridge);ready=bridge/'ready.json'
            if ready.exists():
                readiness=json.loads(ready.read_text(encoding='utf-8-sig'))
                if time.time()-readiness.get('utc_seconds',0)>15:unity_reason='Unity driver heartbeat stale; no previous test result reused'
                elif readiness.get('compiling'):unity_reason='Unity currently compiling'
                else:
                    request={'run_id':run_id,'output':str(output),'fixture_url':config['fixture_url'],'cases':[c.id for c in unity_cases],'live':a.live,'native_live':a.native_live,'retained_wav':a.retained_wav,'timeout':config['unity_wait_seconds']}
                    temporary=bridge/'request.tmp';temporary.write_text(json.dumps(request));temporary.replace(bridge/'request.json')
                    end=time.monotonic()+config['unity_wait_seconds'];result_path=output/'unity-results.json'
                    while time.monotonic()<end:
                        if result_path.exists():
                            candidate=json.loads(result_path.read_text(encoding='utf-8-sig'))
                            if candidate.get('run_id')==run_id and candidate.get('exited_play'):unity_data=candidate;break
                        time.sleep(.25)
                    if unity_data is None:unity_reason='Current-run Unity result unavailable/timeout; old results forbidden'
        if unity_cases:
            by_id={r['case_id']:r for r in unity_data.get('results',[])} if unity_data else {}
            for c in unity_cases:
                row=by_id.get(c.id)
                if row:
                    if row.get('status') not in {'PASS','FAIL','SKIP','ERROR'}:results.append(EvalResult(c.id,c.suite,'ERROR',failure_reason='Malformed Unity status',category='Infrastructure'))
                    else:results.append(EvalResult(c.id,c.suite,row['status'],metrics=row.get('metrics',{}),assertions=row.get('assertions',[]),failure_reason=row.get('failure_reason'),category=c.category,duration_ms=row.get('duration_ms',0),warnings=row.get('warnings',[]),evidence=row.get('evidence',{})))
                else:results.append(EvalResult(c.id,c.suite,'SKIP',failure_reason=unity_reason,category='Infrastructure'))
    except Exception as e:
        for c in selected:
            if c.id not in {r.case_id for r in results}:results.append(EvalResult(c.id,c.suite,'ERROR',failure_reason=type(e).__name__+': '+str(e),category='Infrastructure'))
    finally:
        if fixture:
            try:
                with urllib.request.urlopen(config['fixture_url']+'/evidence',timeout=3) as response:
                    (output/'fixture-evidence.json').write_text(json.dumps(json.load(response),ensure_ascii=False,indent=2),encoding='utf-8')
            except Exception:pass
            fixture.terminate()
            try:fixture.wait(timeout=5)
            except subprocess.TimeoutExpired:fixture.kill();fixture.wait()
        if fixture_log:fixture_log.close()
    after=hashes(backend,Path(a.unity_project));unchanged=before==after
    report_started=time.perf_counter();summaries={}
    for suite in ('state','conversation','speech','behavior','reliability','performance','tool','tool_calling'):
        rs=[r for r in results if r.suite==suite]
        summaries[suite]={'counts':dict(Counter(r.status for r in rs)),'warnings':sum(len(r.warnings) for r in rs),'duration_ms':sum(r.duration_ms for r in rs)}
    code=exit_code(results,selected,a.skip_is_failure or config['skip_blocks'],a.warn_is_failure or config['warn_blocks'])
    if not unchanged:code=1
    baseline=None
    if a.baseline:
        path=Path(a.baseline);baseline={'path':str(path.resolve()),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'latency_ms':json.loads(path.read_text(encoding='utf-8')).get('latency_ms',{})}
    # Synthetic integration has no physical SpeechEnd. Do not compare incomparable latency to a physical baseline.
    regressions={'baseline':baseline,'comparable_physical_run':'unavailable','physical_comparison_status':'SKIP','warn_ratio':config['warn_ratio'],'fail_ratio':config['fail_ratio']}
    if a.current_metrics:
        from .metrics.performance import compare
        current_path=Path(a.current_metrics);current=json.loads(current_path.read_text(encoding='utf-8'))
        if not baseline or current.get('measurement_basis')!='physical' or not current.get('run_id'):
            parser.error('Comparable metrics require a baseline and an independently identified physical measurement run')
        values={k:v.get('median') if isinstance(v,dict) else v for k,v in current.get('latency_ms',{}).items()}
        originals={k:v.get('median') if isinstance(v,dict) else v for k,v in baseline['latency_ms'].items()}
        comparisons=compare(values,originals,config['warn_ratio'],config['fail_ratio'])
        regressions.update(comparable_physical_run=current['run_id'],physical_comparison_status='EVALUATED',comparisons=comparisons,current_metrics_sha256=hashlib.sha256(current_path.read_bytes()).hexdigest())
        if any(r['status']=='FAIL' or (a.warn_is_failure or config['warn_blocks']) and r['status']=='WARN' for r in comparisons):code=1
    git=None
    try:
        p=subprocess.run(['git','-C',str(backend),'rev-parse','HEAD'],capture_output=True,text=True,timeout=2);git=p.stdout.strip() if p.returncode==0 else None
    except Exception:pass
    report={'run_id':run_id,'timestamp':datetime.now(timezone.utc).isoformat(),'commit':git,'source_hashes':before,'environment':{'python':sys.version.split()[0],'platform':sys.platform,'levels':sorted(levels),'fixture_provider_live':False},'suite_summaries':summaries,'case_results':[r.json() for r in results],'case_contracts':[c.__dict__ for c in selected],'metrics':{'CPU_percent':'unavailable','GPU_percent':'unavailable','GC_alloc':'unavailable','runner_duration_ms':(time.perf_counter()-started)*1000},'regressions':regressions,'production_unchanged':unchanged,'production_hash_changes':[p for p in before if before[p]!=after.get(p)],'fixture_service_restored':fixture is None or fixture.poll() is not None,'exit_code':code}
    issue_path=backend/'eval'/'known_issues.json'
    known=json.loads(issue_path.read_text(encoding='utf-8-sig')) if issue_path.exists() else []
    report['known_issues']=known if isinstance(known,list) else [known]
    lines=['# Evaluation Run '+run_id,'','| Suite | PASS | FAIL | ERROR | SKIP | WARN |','|---|---:|---:|---:|---:|---:|']
    for suite,s in summaries.items():lines.append('| '+suite+' | '+' | '.join(str(s['counts'].get(k,0)) for k in ('PASS','FAIL','ERROR','SKIP'))+' | '+str(s['warnings'])+' |')
    lines+=['',f'Exit code: {code}',f'Production hashes unchanged: {unchanged}','Fixture audio is synthetic. Visual naturalness, physical microphone quality and provider voice identity are not asserted.','Physical latency comparison: SKIP unless a fresh comparable physical run is supplied.','']
    for r in results:
        if r.status!='PASS' or r.warnings:lines.append(f'- {r.case_id}: {r.status}; {r.failure_reason or r.warnings}')
    if unity_data and unity_data.get('trace_path'):
        from .metrics.performance import aggregate_trace
        trace_path=Path(unity_data['trace_path'])
        if trace_path.exists():
            observations=[json.loads(line) for line in trace_path.read_text(encoding='utf-8').splitlines() if line.strip()]
            report['metrics']['unity_trace']=aggregate_trace(observations)
            report['metrics']['queue_depth_max']=max((r.get('queue_depth',0) for r in observations),default=0)
            report['metrics']['underrun_events']=sum(r.get('event')=='tts_buffer_underrun' for r in observations)
            report['metrics']['fault_scope_note']='Aggregate includes controlled fault cases; use trace IDs to separate normal/live/fault runs.'
    for issue in report['known_issues']:
        lines.append('Known issue (historical failures retained; resolution evidence in issue record): '+issue['id']+' — '+issue['status'])
    lines.append('Baseline comparisons: '+json.dumps(regressions.get('comparisons',[]),ensure_ascii=False))
    (output/'eval-summary.md').write_text('\n'.join(lines),encoding='utf-8')
    report['metrics']['report_generation_ms']=(time.perf_counter()-report_started)*1000
    (output/'eval-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    from .summarize import enrich
    derived=enrich(output/'eval-report.json');code=derived['exit_code']
    print(json.dumps({'run_id':run_id,'counts':dict(Counter(r.status for r in results)),'warnings':sum(len(r.warnings) for r in results),'exit_code':code,'report':str(output/'eval-report.json')},ensure_ascii=False))
    return code
if __name__=='__main__':raise SystemExit(main())
