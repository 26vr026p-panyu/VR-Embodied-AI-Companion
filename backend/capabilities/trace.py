"""Opt-in capability-only metadata log; no production latency module changes."""
import json,os,time
from pathlib import Path
_origin=time.perf_counter()
def emit(event,**metadata):
    target=os.environ.get('CAPABILITY_TRACE_DIR')
    if not target:return
    try:
        folder=Path(target);folder.mkdir(parents=True,exist_ok=True)
        row=dict(component='backend_capability',event=event,utc_ms=time.time_ns()//1000000,mono_ms=(time.perf_counter()-_origin)*1000,**metadata)
        with (folder/('capability-'+str(os.getpid())+'.jsonl')).open('a',encoding='utf-8') as file:file.write(json.dumps(row,allow_nan=False)+'\n')
    except Exception:pass
