"""Install only the temporary evaluation bridge; never saves a Unity Scene."""
import argparse
from pathlib import Path

def main():
    p=argparse.ArgumentParser();p.add_argument('--project',required=True);p.add_argument('--bridge',required=True);a=p.parse_args()
    project=Path(a.project).resolve();bridge=Path(a.bridge).resolve()
    if not (project/'ProjectSettings/ProjectVersion.txt').exists():p.error('Not a Unity project')
    target=project/'Assets/TemporaryAgentEvaluation/UnityEvaluationDriver.cs'
    if target.exists():p.error('Temporary driver already exists; no overwrite')
    source=Path(__file__).with_name('fixtures')/'UnityEvaluationDriver.cs'
    text=source.read_text(encoding='utf-8')
    start=text.index('const string Bridge=');end=text.index(';',start)+1
    location=str(bridge).replace('\\','/')+'/'
    if '"' in location:p.error('Unsupported quote in bridge path')
    text=text[:start]+'const string Bridge="'+location+'";'+text[end:]
    target.parent.mkdir(parents=True,exist_ok=True);bridge.mkdir(parents=True,exist_ok=True)
    target.write_text(text,encoding='utf-8')
    print('Installed temporary driver. Assets → Refresh, then run eval.runner --unity-bridge '+str(bridge))
if __name__=='__main__':main()
