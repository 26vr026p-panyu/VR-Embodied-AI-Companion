"""Physical Quest evidence only. Missing hardware is SKIP, never desktop-derived PASS."""
import json
from pathlib import Path
from ..contracts import SkipCase

def run(handler,config):
    path=Path(config.get('quest_report',r'LOCAL_ONLY_PATH_NOT_DISTRIBUTED'))
    if not path.exists():raise SkipCase('No physical Quest report')
    r=json.loads(path.read_text(encoding='utf-8'))
    if not r.get('hardware_verified'):raise SkipCase('Quest not connected / physical validation not run')
    assert r.get('mode') in ('PCVR','AndroidStandalone'),'Runtime mode must be explicit'
    assert r.get('device'),'Physical HMD identity must be observed'
    key=handler.removeprefix('quest_')
    gate=r.get('gates',{}).get(key,{})
    if gate.get('status') in (None,'NOT RUN','NOT VERIFIED','SKIP'):raise SkipCase('Physical gate not measured: '+key)
    assert gate.get('status')=='PASS','Physical gate failed: '+key
    evidence=gate.get('evidence',[])
    assert evidence and all(Path(p).is_file() for p in evidence),'Actual gate evidence files required'
    return dict(metrics={'runtime_mode':r['mode'],'physical_hardware':True},assertions=[{'description':'Actual '+key+' evidence recorded','pass':True}],evidence={'files':evidence})
