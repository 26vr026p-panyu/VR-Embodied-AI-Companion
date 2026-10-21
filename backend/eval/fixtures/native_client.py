"""Explicit synthetic native-channel fixture; never a production provider."""
import json
from types import SimpleNamespace as N
from eval.fixtures.client import fixture_reply

SCENARIOS={
 'NT_WAVE':[('gesture_wave',{})], 'NT_LEAD':[('spatial_lead',{})],
 'NT_EXPRESSION':[('expression_set',{'expression':'happy'})],
 'NT_INVALID':[('expression_set',{'expression':'dragon'})],
 'NT_UNKNOWN':[('unregistered_tool',{})], 'NT_SIDE':[('spatial_side',{})],
 'NT_MULTI':[('expression_set',{'expression':'happy'}),('gesture_nod',{})],
 'NT_CONFLICT':[('spatial_trail',{}),('spatial_lead',{})],
 'NT_TIMEOUT':[('gesture_nod',{})], 'NT_CANCEL':[('gesture_wave',{})],
 'NT_LOOP':[('expression_set',{'expression':'neutral'})],
 'NT_LEGACY':[('expression_set',{'expression':'neutral'})]
}
class NativeFixtureClient:
    def __init__(self):self.calls=[];self.chat=N(completions=self)
    def create(self,**kwargs):
        self.calls.append(json.loads(json.dumps(kwargs)))
        text=json.loads(next(m['content'] for m in reversed(kwargs['messages']) if m['role']=='user'))['userMessage']
        has_result=any(m['role']=='tool' for m in kwargs['messages'])
        calls=SCENARIOS.get(text,[]) if not has_result or text=='NT_LOOP' and kwargs.get('tools') else []
        class Stream:
            closed=False
            def __iter__(self):
                if calls:
                    for index,(name,args) in enumerate(calls):
                        arguments=json.dumps(args)
                        for offset in range(0,len(arguments),4):
                            d=N(index=index,id='fixture_'+str(index) if offset==0 else None,type='function' if offset==0 else None,function=N(name=name if offset==0 else None,arguments=arguments[offset:offset+4]))
                            yield N(choices=[N(delta=N(content=None,tool_calls=[d]))])
                else:
                    reply=fixture_reply('WAVE' if text=='NT_LEGACY' else 'SPEECH_ONLY')
                    content=json.dumps(reply,ensure_ascii=False)
                    for i in range(0,len(content),7):yield N(choices=[N(delta=N(content=content[i:i+7],tool_calls=None))])
            def close(self):self.closed=True
        return Stream()
