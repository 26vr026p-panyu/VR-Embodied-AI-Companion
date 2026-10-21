"""Derive metrics and verify history against current-run evidence only."""
import json,sys
from pathlib import Path
from .metrics.performance import aggregate_trace

def enrich(path):
    path=Path(path);report=json.loads(path.read_text(encoding='utf-8'));unity_path=path.parent/'unity-results.json'
    if unity_path.exists():
        unity=json.loads(unity_path.read_text(encoding='utf-8'))
        if unity.get('run_id')!=report['run_id']:raise ValueError('Different Unity run identity')
        trace=Path(unity['trace_path']) if unity.get('trace_path') else None
        if trace and trace.exists():
            rows=[json.loads(line) for line in trace.read_text(encoding='utf-8').splitlines() if line.strip()]
            by_id={}
            for row in rows:
                if row.get('trace_id'):by_id.setdefault(row['trace_id'],[]).append(row)
            live_ids={r['metrics']['trace_id'] for r in unity.get('results',[]) if r.get('metrics',{}).get('provider_live') and r['metrics'].get('trace_id')}
            report['metrics']['trace_by_turn']={identity:dict(measurement_basis='retained_audio_live' if identity in live_ids else 'synthetic_or_controlled_fixture',metrics=aggregate_trace(events),underruns=max((r.get('count',0) for r in events if r.get('event')=='tts_buffer_underrun'),default=0),queue_depth_max=max((r.get('queue_depth',0) for r in events),default=0)) for identity,events in by_id.items()}
            report['metrics']['physical_speech_end_to_play']='unavailable: no fresh physical microphone turn'
    evidence_path=path.parent/'fixture-evidence.json'
    if evidence_path.exists():
        evidence=json.loads(evidence_path.read_text(encoding='utf-8'))
        backend_rows=[]
        for trace in (path.parent/'fixture-traces').glob('*.jsonl'):
            backend_rows.extend(json.loads(line) for line in trace.read_text(encoding='utf-8').splitlines() if line.strip())
        for result in report['case_results']:
            if result['case_id'] not in ('reliability.llm_after','reliability.tts_after'):continue
            identity=result.get('metrics',{}).get('trace_id')
            if not identity:continue
            entries=[entry for key,entry in evidence['turns'].items() if key.startswith(identity+':')]
            commits=sum(row.get('event')=='history_committed' and row.get('trace_id')==identity for row in backend_rows)
            safe=bool(entries) and all(e['state']=='cancelled' and not e['pending'] for e in entries) and commits==0
            result['evidence']['backend_history']={'snapshots':entries,'history_committed_events':commits}
            result['assertions'].append({'description':'Failed partial speech leaves zero normal history commits and no pending reply','pass':safe})
            if not safe:
                result['status']='FAIL';result['failure_reason']='Backend history evidence failed';report['exit_code']=1
    path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    return report

if __name__=='__main__':enrich(sys.argv[1])
