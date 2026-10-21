import statistics

def compare(current,baseline,warn_ratio,fail_ratio):
    rows=[]
    for name,value in current.items():
        original=baseline.get(name)
        if not isinstance(value,(int,float)) or not isinstance(original,(int,float)):
            rows.append({'metric':name,'status':'unavailable'});continue
        ratio=value/original if original else (float('inf') if value else 1)
        status='FAIL' if ratio>fail_ratio else 'WARN' if ratio>warn_ratio else 'PASS'
        rows.append({'metric':name,'current':value,'baseline':original,'ratio':None if ratio==float('inf') else ratio,'status':status})
    return rows

def aggregate_trace(rows):
    by_turn={}
    for r in rows:by_turn.setdefault((r.get('component'),r.get('clock_id'),r.get('trace_id')),[]).append(r)
    definitions={'llm_ttft_ms':('llm_stream_request_started','llm_first_token_received'),'llm_first_sentence_ms':('llm_stream_request_started','llm_sentence_ready'),'tts_ttfa_ms':('tts_request_started','tts_first_chunk_received'),'buffer_ms':('tts_first_chunk_received','playback_started'),'speech_end_to_play_ms':('vad_speech_ended','playback_started')}
    samples={k:[] for k in definitions}
    for events in by_turn.values():
        for metric,(start,end) in definitions.items():
            a=[r['mono_ms'] for r in events if r.get('event')==start and 'mono_ms' in r];b=[r['mono_ms'] for r in events if r.get('event')==end and 'mono_ms' in r]
            if a and b and b[0]>=a[0]:samples[metric].append(b[0]-a[0])
    samples['parser_ms']=[r['parser_ms'] for r in rows if isinstance(r.get('parser_ms'),(int,float))]
    return {k:{'available':bool(v),'count':len(v),'median':statistics.median(v) if v else None,'min':min(v) if v else None,'max':max(v) if v else None} for k,v in samples.items()}
