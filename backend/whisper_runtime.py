"""Complete-WAV medium/int8 runtime; explicit readiness and single model residency."""
from dataclasses import dataclass, asdict
from pathlib import Path
import json,time,threading

@dataclass(frozen=True)
class WhisperRuntimeConfig:
    model: str = 'medium'
    compute_type: str = 'int8'
    cpu_threads: int = 0
    num_workers: int = 1
    beam_size: int = 5
    best_of: int = 5
    condition_on_previous_text: bool = True
    without_timestamps: bool = False
    def __post_init__(self):
        if self.model!='medium' or self.compute_type!='int8':raise ValueError('Production ASR must remain medium/int8')
        if self.cpu_threads not in (0,8,12,16) or self.num_workers not in (1,2):raise ValueError('Unsupported benchmark runtime setting')
        if self.beam_size not in (1,5) or self.best_of not in (1,5):raise ValueError('Unsupported decoding setting')
    @classmethod
    def read(cls,path):
        p=Path(path)
        return cls(**json.loads(p.read_text(encoding='utf-8'))) if p.exists() else cls()
    def decode_options(self):
        return dict(beam_size=self.beam_size,best_of=self.best_of,condition_on_previous_text=self.condition_on_previous_text,without_timestamps=self.without_timestamps)

class WhisperRuntime:
    def __init__(self,config):
        self.config=config;self.model=None;self.ready=False;self.load_count=0;self.load_ms=None;self._lock=threading.Lock()
    def load(self,factory=None):
        with self._lock:
            if self.model is not None:return self.model
            if factory is None:
                from faster_whisper import WhisperModel
                factory=WhisperModel
            t=time.perf_counter()
            model=factory(self.config.model,device='cpu',compute_type=self.config.compute_type,cpu_threads=self.config.cpu_threads,num_workers=self.config.num_workers)
            self.load_ms=(time.perf_counter()-t)*1000;self.load_count+=1
            self.model=model;self.ready=True
            return model
    def status(self):
        return dict(asr_ready=self.ready,model_resident=self.model is not None,load_count=self.load_count,load_ms=self.load_ms,runtime_instance=str(id(self.model)) if self.model is not None else None,config=asdict(self.config))
