"""Narrow request policy: constrains provider choices, never executes an action."""
from dataclasses import dataclass
import re

@dataclass(frozen=True)
class ActionToolDecision:
    intent: str = 'ordinary'
    choice: object = 'auto'
    tools: tuple = ()
    requested_tool: str | None = None
    reason: str = 'ambiguous_or_ordinary'

class ActionToolChoicePolicy:
    # Full utterance matches only. Quotes, negation, narrative and mixed requests
    # deliberately do not qualify. Do not expand this into a language parser.
    RULES = (
        ('gesture_request', 'gesture_wave', r'(?:手を振って|バイバイして)(?:くれる|くれるかな|ください|ね)?', True),
        ('gesture_request', 'gesture_nod', r'(?:うなずいて|頷いて)(?:くれる|ください|ね)?', False),
        ('expression_request', 'expression_set', r'(?:笑って|笑顔を見せて)(?:くれる|ください|ね)?', False),
        ('spatial_request', 'spatial_lead', r'(?:前を(?:走って|歩いて)|前に(?:来て|行って)|先導して)(?:くれる|ください|ね)?', False),
        ('spatial_request', 'spatial_side', r'(?:隣を(?:走って|歩いて)|横に来て|一緒に並んで走って)(?:くれる|ください|ね)?', False),
        ('spatial_request', 'spatial_trail', r'後ろを(?:走って|歩いて)(?:くれる|ください|ね)?', False),
        ('spatial_request', 'spatial_nearby', r'(?:近くに来て|そばに来て)(?:くれる|ください|ね)?', False),
    )
    def choose(self, text, tools, *, continuation=False):
        tools=tuple(tools)
        if continuation:return ActionToolDecision(tools=tools,reason='result_continuation_auto')
        if not isinstance(text,str):return ActionToolDecision(tools=tools)
        text=re.sub(r'[。！？?!\s]+$','',text.strip())
        for intent,name,pattern,named in self.RULES:
            if not re.fullmatch(pattern,text):continue
            prefix={'gesture_request':'gesture_', 'expression_request':'expression_', 'spatial_request':'spatial_'}[intent]
            category=tuple(t for t in tools if t['function']['name'].startswith(prefix))
            if name not in {t['function']['name'] for t in category}:
                # Never force another tool when requested one is unavailable.
                return ActionToolDecision(intent,'auto',(),name,'requested_tool_unavailable')
            if named:
                category=tuple(t for t in category if t['function']['name']==name)
                choice={'type':'function','function':{'name':name}}
            else:choice='required'
            return ActionToolDecision(intent,choice,category,name,'high_confidence_full_utterance')
        return ActionToolDecision(tools=tools)
