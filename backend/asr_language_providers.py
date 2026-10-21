"""Language-aware recognizers; imported only inside the isolated ASR runtime."""
from pathlib import Path
from types import SimpleNamespace
import numpy as np
from asr_providers import IAsrProvider, AsrCapabilities

class NemotronOnlineProvider(IAsrProvider):
    capabilities=AsrCapabilities('native online Nemotron 3.5',True,True,False)
    def __init__(self,model_dir,num_threads=4):
        import sherpa_onnx
        p=Path(model_dir)
        self.recognizer=sherpa_onnx.OnlineRecognizer.from_transducer(tokens=str(p/'tokens.txt'),encoder=str(p/'encoder.int8.onnx'),decoder=str(p/'decoder.int8.onnx'),joiner=str(p/'joiner.int8.onnx'),num_threads=num_threads,enable_endpoint_detection=False,decoding_method='greedy_search')
    def open_stream(self,language='ja'):
        return NemotronOnlineSession(self.recognizer,language or 'auto')
    def decode(self,audio,language='ja',initial_prompt=None,beam_size=5):
        s=self.open_stream(language);s.accept(audio)
        return s.finish(),SimpleNamespace(language=language or 'auto',language_probability=0.0),[]

class NemotronOnlineSession:
    def __init__(self,recognizer,language):
        if language not in ('ja','zh','auto','en'):raise ValueError('unsupported_language_mode')
        self.recognizer=recognizer;self.stream=recognizer.create_stream();self.closed=False
        # Official per-stream decoder prompt, before ANY waveform.
        self.stream.set_option('language',language)
        if self.stream.get_option('language')!=language:raise RuntimeError('language_option_not_applied')
        self.language=language
    def accept(self,pcm):
        if self.closed:raise RuntimeError('stream_finished')
        self.stream.accept_waveform(16000,pcm)
        while self.recognizer.is_ready(self.stream):self.recognizer.decode_stream(self.stream)
        return self.recognizer.get_result(self.stream)
    def finish(self):
        if not self.closed:
            # Drain padded final chunk, no wall-clock endpoint wait introduced.
            self.stream.accept_waveform(16000,np.zeros(10560,dtype=np.float32));self.stream.input_finished()
            while self.recognizer.is_ready(self.stream):self.recognizer.decode_stream(self.stream)
            self.closed=True
        return self.recognizer.get_result(self.stream)

class ReazonSpeechProvider(IAsrProvider):
    capabilities=AsrCapabilities('offline Japanese-only endpoint final',False,True,False)
    def __init__(self,model_dir,num_threads=4):
        import sherpa_onnx
        p=Path(model_dir)
        self.recognizer=sherpa_onnx.OfflineRecognizer.from_transducer(tokens=str(p/'tokens.txt'),encoder=str(next(p.glob('encoder*.int8.onnx'))),decoder=str(next(p.glob('decoder*.int8.onnx'))),joiner=str(next(p.glob('joiner*.int8.onnx'))),num_threads=num_threads,decoding_method='greedy_search')
    def decode(self,audio,language='ja',initial_prompt=None,beam_size=5):
        if language not in ('ja',None):raise ValueError('reazonspeech_japanese_only')
        s=self.recognizer.create_stream();s.accept_waveform(16000,audio);self.recognizer.decode_stream(s)
        return s.result.text,SimpleNamespace(language='ja',language_probability=0.0),[]
