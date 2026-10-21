"""ASR provider boundary. Runtime dependencies are imported only by selected providers."""
from abc import ABC, abstractmethod
from dataclasses import dataclass
from types import SimpleNamespace
from pathlib import Path
import io,json,time,urllib.request,uuid,wave
import numpy as np

@dataclass(frozen=True)
class AsrCapabilities:
    implementation: str
    native_online: bool
    language_hint: bool
    timestamp_segments: bool
    physical_inference_abort: bool = False

class IAsrProvider(ABC):
    capabilities: AsrCapabilities
    @abstractmethod
    def decode(self,audio,language=None,initial_prompt=None,beam_size=5):
        """16kHz float PCM -> (text, info, timestamp segments)."""
    def open_stream(self,language=None):
        raise NotImplementedError('provider_has_no_native_online_decoder')

class FasterWhisperProvider(IAsrProvider):
    capabilities=AsrCapabilities('incremental Whisper',False,True,True)
    def __init__(self,model):self.model=model
    def decode(self,audio,language=None,initial_prompt=None,beam_size=5):
        segments,info=self.model.transcribe(audio,beam_size=beam_size,initial_prompt=initial_prompt,language=language)
        segments=list(segments)
        return ''.join(x.text for x in segments).strip(),info,segments

class WhisperCppProvider(IAsrProvider):
    capabilities=AsrCapabilities('incremental Whisper',False,True,True)
    def __init__(self,url='http://127.0.0.1:8091/inference',timeout=180):self.url=url;self.timeout=timeout
    def decode(self,audio,language=None,initial_prompt=None,beam_size=5):
        raw=np.clip(np.round(audio*32768),-32768,32767).astype('<i2');f=io.BytesIO()
        with wave.open(f,'wb') as w:w.setnchannels(1);w.setsampwidth(2);w.setframerate(16000);w.writeframes(raw.tobytes())
        boundary='Asr'+uuid.uuid4().hex;body=bytearray()
        for key,value in [('response_format','verbose_json'),('language',language or 'auto'),('beam_size',str(beam_size)),('temperature','0')]+([('prompt',initial_prompt)] if initial_prompt else []):
            body.extend(f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode())
        body.extend(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="audio.wav"\r\nContent-Type: audio/wav\r\n\r\n'.encode());body.extend(f.getvalue());body.extend(f'\r\n--{boundary}--\r\n'.encode())
        req=urllib.request.Request(self.url,data=bytes(body),headers={'Content-Type':'multipart/form-data; boundary='+boundary})
        with urllib.request.urlopen(req,timeout=self.timeout) as response:d=json.load(response)
        segments=[SimpleNamespace(text=x['text'],start=x.get('start',0),end=x.get('end',len(audio)/16000)) for x in d.get('segments',[])]
        return d.get('text','').strip(),SimpleNamespace(language=d.get('language',language or 'auto'),language_probability=None),segments

class SherpaOnlineProvider(IAsrProvider):
    capabilities=AsrCapabilities('native online Zipformer',True,False,False)
    def __init__(self,model_dir,num_threads=4):
        import sherpa_onnx
        p=Path(model_dir)
        self.recognizer=sherpa_onnx.OnlineRecognizer.from_transducer(tokens=str(p/'tokens.txt'),encoder=str(next(p.glob('encoder*.onnx'))),decoder=str(next(p.glob('decoder*.onnx'))),joiner=str(next(p.glob('joiner*.onnx'))),num_threads=num_threads,provider='cpu',enable_endpoint_detection=False,decoding_method='greedy_search')
    def open_stream(self,language=None):return SherpaOnlineSession(self.recognizer)
    def decode(self,audio,language=None,initial_prompt=None,beam_size=5):
        session=self.open_stream(language);session.accept(audio);text=session.finish()
        return text,SimpleNamespace(language='auto',language_probability=None),[]

class SherpaOnlineSession:
    def __init__(self,recognizer):self.recognizer=recognizer;self.stream=recognizer.create_stream();self.closed=False
    def accept(self,pcm):
        if self.closed:raise RuntimeError('stream_finished')
        self.stream.accept_waveform(16000,pcm)
        while self.recognizer.is_ready(self.stream):self.recognizer.decode_stream(self.stream)
        return self.recognizer.get_result(self.stream)
    def finish(self):
        if not self.closed:
            self.stream.accept_waveform(16000,np.zeros(4800,dtype=np.float32));self.stream.input_finished()
            while self.recognizer.is_ready(self.stream):self.recognizer.decode_stream(self.stream)
            self.closed=True
        return self.recognizer.get_result(self.stream)

class SherpaOnlineRemoteProvider(IAsrProvider):
    capabilities=SherpaOnlineProvider.capabilities
    def __init__(self,url='http://127.0.0.1:8901',timeout=10,capabilities=None):
        self.url=url.rstrip('/');self.timeout=timeout
        if capabilities is not None:self.capabilities=capabilities
    def open_stream(self,language=None,identity=None):return SherpaRemoteSession(self,dict(identity or {},language_hint=language,language_mode=language or 'auto'))
    def decode(self,audio,language=None,initial_prompt=None,beam_size=5):
        s=self.open_stream(language)
        try:s.accept(audio);return s.finish(),SimpleNamespace(language='auto',language_probability=None),[]
        finally:s.cancel()
    def request(self,path,data=None,method='POST',headers=None):
        req=urllib.request.Request(self.url+path,data=data,method=method,headers=headers or {})
        with urllib.request.urlopen(req,timeout=self.timeout) as response:return json.load(response)

class SherpaRemoteSession:
    def __init__(self,provider,identity):
        self.provider=provider;self.sequence=0;self.closed=False;self.identity=identity
        self.id=provider.request('/streams',json.dumps(identity).encode(),headers={'Content-Type':'application/json'})['stream_id']
    def accept(self,pcm):
        if self.closed:raise RuntimeError('stream_finished')
        text=''
        for start in range(0,len(pcm),16000):
            headers={'Content-Type':'application/octet-stream','X-Asr-Sequence':str(self.sequence),'X-Trace-Id':str(self.identity.get('trace_id','')),'X-Turn-Id':str(self.identity.get('turn_id',''))}
            text=self.provider.request('/streams/'+self.id+'/chunk',np.asarray(pcm[start:start+16000],dtype='<f4').tobytes(),headers=headers)['text'];self.sequence+=1
        return text
    def finish(self):
        text=self.provider.request('/streams/'+self.id+'/finish',b'')['text'];self.closed=True;return text
    def cancel(self):
        if not self.closed:
            self.closed=True
            try:self.provider.request('/streams/'+self.id,method='DELETE')
            except Exception:pass
