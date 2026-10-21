import json
from types import SimpleNamespace as NS
from behavior_contract import none_behavior

def fixture_reply(text):
    behavior=none_behavior()
    if text=='NOD':behavior['gesture']='nod'
    if text=='WAVE':behavior['gesture']='wave'
    if text=='EXPRESSION':behavior.update(expression='happy',expression_intensity=.4,expression_hold=.4)
    if text=='LEAD':behavior['spatial_intent']='lead'
    speech='一緒に歩こう。ゆっくり行こう。' if text=='QUEUE' else '一緒に歩こう。'
    return {'action':'CHAT','speech':speech,'behavior':behavior}

class FixtureClient:
    """Explicitly synthetic provider. Uses real parser/context/history/stream endpoint logic."""
    def __init__(self,outputs=None):self.outputs=list(outputs or []);self.calls=[];self.chat=NS(completions=self)
    def create(self,**kwargs):
        self.calls.append(kwargs)
        current=json.loads(next(m['content'] for m in reversed(kwargs['messages']) if m['role']=='user'))['userMessage']
        if current.startswith('NT_'):
            from .native_client import NativeFixtureClient
            if not hasattr(self,'native'):self.native=NativeFixtureClient()
            return self.native.create(**kwargs)
        text=self.outputs.pop(0) if self.outputs else json.dumps(fixture_reply(current),ensure_ascii=False)
        if kwargs.get('stream'):
            class Stream:
                def __iter__(self):
                    for i in range(0,len(text),7):yield NS(choices=[NS(delta=NS(content=text[i:i+7]))])
                def close(self):pass
            return Stream()
        return NS(choices=[NS(message=NS(content=text),finish_reason='stop')],usage=None)
