"""Execute selected actual source definitions without heavy import/model side effects.
The function body/AST is unchanged; only decorator registration is omitted.
"""
import ast,json,types
from pathlib import Path
from typing import Any,Optional,Literal
from fastapi import HTTPException
from pydantic import BaseModel,Field
from latency_trace import emit,span
from behavior_contract import normalize_reply,none_behavior
from companion_persona import RESCUE_INSTRUCTION,build_system_prompt,build_user_context
from agent_context import build_agent_messages
from turn_lifecycle import TurnCancellationRegistry,check_turn

def load_api(client=None):
    path=Path(__file__).resolve().parents[2]/'server.py'
    names={'ExerciseState','check_intervention','parse_llm_json','print_llm_usage','request_llm','call_agent_llm','agent'}
    tree=ast.parse(path.read_text(encoding='utf-8-sig'))
    definitions=[n for n in tree.body if isinstance(n,(ast.FunctionDef,ast.ClassDef)) and n.name in names]
    for n in definitions:n.decorator_list=[]
    namespace=dict(globals(),client=client,conversation_sessions={},turn_registry=TurnCancellationRegistry(),trace_event=emit,trace_span=span)
    exec(compile(ast.Module(body=definitions,type_ignores=[]),str(path),'exec'),namespace)
    api=types.SimpleNamespace(**namespace)
    api._namespace=namespace
    return api
