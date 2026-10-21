"""Observation-only performance contract. Absent observations remain absent."""
from dataclasses import dataclass,asdict,field
from datetime import datetime,timezone
import json,math,statistics

def stats(values):
    a=sorted(float(x) for x in values if isinstance(x,(int,float)) and not isinstance(x,bool) and math.isfinite(x))
    return dict(available=bool(a),n=len(a),median=statistics.median(a) if a else None,p95=a[max(0,math.ceil(.95*len(a))-1)] if a else None,min=a[0] if a else None,max=a[-1] if a else None)

@dataclass
class PerformanceSnapshot:
    run_id:str
    timestamp:str
    environment:dict
    scenario:str
    metrics:dict
    known_issues:list=field(default_factory=list)
    unavailable_metrics:dict=field(default_factory=dict)
    schema_version:int=1
    measurement_basis:str='unavailable'
    def to_json(self):return json.dumps(asdict(self),ensure_ascii=False,allow_nan=False,indent=2)

def validate(snapshot):
    if isinstance(snapshot,PerformanceSnapshot):snapshot=asdict(snapshot)
    required={'run_id','timestamp','environment','scenario','metrics','known_issues','unavailable_metrics','measurement_basis'}
    if not required.issubset(snapshot):raise ValueError('missing snapshot fields')
    if not snapshot['run_id'] or not snapshot['measurement_basis']:raise ValueError('identity/basis missing')
    json.dumps(snapshot,allow_nan=False)
    return True

def validate_budget(b):
    for name,row in b.items():
        if row['unit'] not in ('ms','bytes','ratio'):raise ValueError('budget unit')
        if not row.get('basis'):raise ValueError('budget needs rationale')
        target,warning,failure=(row[k] for k in ('target','warning','failure'))
        if not 0<=target<=warning<=failure:raise ValueError('budget order')
    return True

# Same-process monotonic only. Cross-clock UTC joins are explicitly estimates elsewhere.
ALIASES={
 'endpoint_ms':(['speech_end_estimated'],['vad_speech_ended']),
 'asr_ms':(['asr_request_started'],['asr_final_received']),
 'llm_ttft_ms':(['llm_stream_request_started'],['llm_first_token_received']),
 'llm_first_sentence_ms':(['llm_stream_request_started'],['llm_sentence_ready']),
 'tool_roundtrip_ms':(['tool_call_received'],['tool_result_received','tool_result_posted']),
 'tts_ttfa_ms':(['tts_request_started'],['tts_first_chunk_received']),
 'buffer_ms':(['tts_first_chunk_received'],['tts_playable_buffer_ready']),
 'speech_end_to_play_ms':(['speech_end_estimated'],['playback_started'])}
def waterfall(rows):
    grouped={}
    for r in rows:
        if not r.get('trace_id') or not isinstance(r.get('mono_ms'),(int,float)):continue
        grouped.setdefault((r.get('component'),r.get('clock_id'),r['trace_id']),[]).append(r)
    output=[]
    for (component,clock,identity),events in grouped.items():
        data={}
        for name,(starts,ends) in ALIASES.items():
            a=[r['mono_ms'] for r in events if r.get('event') in starts];b=[r['mono_ms'] for r in events if r.get('event') in ends]
            data[name]=min(b)-min(a) if a and b and min(b)>=min(a) else None
        output.append(dict(trace_id=identity,component=component,clock_id=clock,metrics=data,tool_turn=any('tool_call' in r.get('event','') for r in events),unavailable_metrics=[k for k,v in data.items() if v is None]))
    return output
