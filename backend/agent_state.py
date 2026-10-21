"""V1 request-time, read-only state contract. No control or memory ownership."""
from dataclasses import dataclass
from types import MappingProxyType
import json, math, re, time

MAX_SNAPSHOT_BYTES = 8192
ENUMS = {
    'conversation': {'mode': {'exercise','general'}, 'language': {'ja','zh','auto','en'}},
    'turn': {'lifecycle': {'created','listening','processingasr','waitingsupplement','processingllm','processingtts','speaking','completed','cancelled','failed'}},
    'speech': {'state': {'idle','preparing','ready','speaking','interrupted','completed','failed'}, 'source': {'conversation','proactive','exercise','system','tool'}},
    'avatar': {'expression': {'neutral','happy','sad','surprised','angry','relaxed','concerned','shy','unknown'}, 'gesture': {'none','nod','wave'}, 'spatial_mode': {'side','lead','trail','stay','approach','roam'}, 'spatial_intent': {'side','lead','trail','stay','approach','roam'}, 'locomotion': {'idle','walking','running'}},
    'user': {'activity': {'moving','stationary'}},
    'environment': {'relative_side': {'left','right','aligned'}, 'relative_forward': {'ahead','behind','aligned'}},
    'system': {'language_mode': {'ja','zh','auto'}},
}
FIELDS = {
    'conversation': {'mode','language','session_id'},
    'turn': {'lifecycle','cancelled'},
    'speech': {'state','request_id','queue_depth','is_speaking','source'},
    'avatar': {'expression','gesture','spatial_mode','spatial_intent','locomotion'},
    'user': {'is_speaking','activity'},
    'environment': {'scene','distance_m','relative_side','relative_forward'},
    'system': {'language_mode'},
}

@dataclass(frozen=True)
class AgentStateSnapshot:
    snapshot_id: str
    created_at_ms: int
    turn_id: int
    trace_id: str
    state_version: int
    sections: object
    version: int = 1

def validate_snapshot(payload, identity, mode, language, session_id):
    """Reject the entire optional state payload, never the conversation request."""
    if payload is None: return None, 'absent'
    try:
        encoded = json.dumps(payload, ensure_ascii=False, allow_nan=False)
        if len(encoded.encode('utf-8')) > MAX_SNAPSHOT_BYTES: raise ValueError('size')
        if not isinstance(payload, dict): raise ValueError('object')
        allowed = {'version','snapshot_id','created_at_ms','turn_id','trace_id','state_version'} | set(FIELDS)
        if set(payload)-allowed: raise ValueError('unknown_field')
        for field in ('version','created_at_ms','turn_id','state_version'):
            if type(payload.get(field)) is not int: raise ValueError('integer')
        if payload['version'] != 1 or payload['turn_id'] <= 0 or payload['state_version'] < 1: raise ValueError('version_or_turn')
        if not re.fullmatch(r'[A-Za-z0-9_-]{1,96}', payload.get('snapshot_id','')): raise ValueError('snapshot_id')
        if not re.fullmatch(r'[A-Za-z0-9_-]{1,96}', payload.get('trace_id','')): raise ValueError('trace_id')
        if identity is None or (payload['trace_id'],payload['turn_id']) != identity: raise ValueError('turn_identity')
        now = int(time.time()*1000)
        if payload['created_at_ms'] < 1 or payload['created_at_ms'] > now+30000: raise ValueError('timestamp')
        sections = {}
        for name, fields in FIELDS.items():
            values = payload.get(name,{})
            if not isinstance(values,dict) or set(values)-fields: raise ValueError('section_fields')
            for key,value in values.items():
                enums = ENUMS.get(name,{}).get(key)
                if enums is not None:
                    if not isinstance(value,str) or value not in enums: raise ValueError('enum')
                elif key in {'cancelled','is_speaking'}:
                    if type(value) is not bool: raise ValueError('boolean')
                elif key == 'queue_depth':
                    if type(value) is not int or not 0 <= value <= 32: raise ValueError('queue_depth')
                elif key == 'distance_m':
                    if type(value) not in (int,float) or not math.isfinite(value) or not 0 <= value <= 100000: raise ValueError('distance')
                elif not isinstance(value,str) or len(value)>128 or any(ord(c)<32 for c in value): raise ValueError('string')
            sections[name] = MappingProxyType(dict(values))
        c=sections['conversation']
        if c.get('mode') != mode or c.get('language') != language or c.get('session_id') != session_id: raise ValueError('conversation_identity')
        if sections['turn'].get('cancelled') or sections['turn'].get('lifecycle') in {'cancelled','completed','failed'}: raise ValueError('terminal_turn')
        if 'is_speaking' in sections['speech'] and sections['speech']['is_speaking'] != (sections['speech'].get('state')=='speaking'): raise ValueError('speech_consistency')
        return AgentStateSnapshot(payload['snapshot_id'],payload['created_at_ms'],payload['turn_id'],payload['trace_id'],payload['state_version'],MappingProxyType(sections)), 'valid'
    except (TypeError,ValueError,OverflowError,RecursionError):
        return None, 'rejected'

def semantic_state(snapshot):
    if snapshot is None: return ''
    s=snapshot.sections; lines=[]
    spatial=s['avatar'].get('spatial_mode')
    phrases={'lead':'You are currently leading the user.','side':'Your current spatial intent is accompanying the user side by side.','trail':'You are currently following behind the user.','stay':'You are staying near the user.','approach':'You are approaching the user for conversation.','roam':'You are roaming within the nearby area.'}
    if spatial in phrases: lines.append(phrases[spatial])
    intent=s['avatar'].get('spatial_intent')
    if intent and intent!=spatial: lines.append('Your preferred spatial mode is '+intent+', while the effective nearby mode is '+str(spatial)+'.')
    for side in ('speech','user'):
        if s[side].get('is_speaking'): lines.append('You are currently speaking.' if side=='speech' else 'The user is currently speaking.')
    a=s['avatar']
    if a.get('gesture') not in (None,'none'): lines.append('Your active gesture is '+a['gesture']+'.')
    if a.get('expression') not in (None,'neutral','unknown'): lines.append('Your current expression is '+a['expression']+'.')
    if a.get('locomotion') not in (None,'idle'): lines.append('You are currently '+a['locomotion']+'.')
    e=s['environment']
    if 'distance_m' in e: lines.append(f"You are approximately {e['distance_m']:.1f} metres from the user.")
    if e.get('relative_side') in ('left','right'): lines.append('You are on the user\'s '+e['relative_side']+' relative to the track heading.')
    return '\n'.join(lines)
