from dataclasses import dataclass,field,asdict
from typing import Literal

CATEGORIES={'Input','State','Tool','ASR','LLM','Validation','TTS','Playback','Behavior','Cancellation','History','Performance','Infrastructure','exercise'}
@dataclass(frozen=True)
class EvalCase:
    id:str
    suite:str
    description:str
    handler:str
    inputs:dict=field(default_factory=dict)
    expected:dict=field(default_factory=dict)
    tags:tuple=()
    timeout:float=10
    required:bool=True
    level:str='unit'
    category:str='Validation'
    expected_tool:str|None=None
    forbidden_tools:tuple=()
    expected_result:object=None

@dataclass
class EvalResult:
    case_id:str
    suite:str
    status:Literal['PASS','FAIL','SKIP','ERROR']
    metrics:dict=field(default_factory=dict)
    assertions:list=field(default_factory=list)
    failure_reason:str|None=None
    category:str|None=None
    duration_ms:float=0
    warnings:list=field(default_factory=list)
    evidence:dict=field(default_factory=dict)
    def json(self):return asdict(self)

class SkipCase(Exception):pass

def exit_code(results,cases,skip_blocks=False,warn_blocks=False):
    by_id={c.id:c for c in cases}
    return 1 if any(by_id[r.case_id].required and (r.status in ('FAIL','ERROR') or skip_blocks and r.status=='SKIP' or warn_blocks and r.warnings) for r in results) else 0
