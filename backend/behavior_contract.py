"""Pure validation shared by primary output and tests. Rescue is always speech-only."""
import math

ACTIONS = frozenset(("CHAT", "KEEP", "ENCOURAGE", "REST"))
GESTURES = frozenset(("none", "nod", "wave"))
EXPRESSIONS = frozenset(("none", "neutral", "happy", "concerned", "surprised", "relaxed", "shy"))
SPATIAL_INTENTS = frozenset(("none", "lead", "side", "trail", "nearby"))

def none_behavior():
    return dict(gesture="none", expression="none", expression_intensity=0.0,
                expression_hold=0.0, spatial_intent="none")

def enum_value(value, allowed):
    return isinstance(value, str) and value in allowed

def bounded_number(value, low, high):
    if isinstance(value, bool) or not isinstance(value, (float, int)) or not math.isfinite(value):
        raise ValueError("Behavior number must be finite and numeric")
    return max(low, min(high, float(value)))

def validate_behavior(value):
    if value is None:
        return none_behavior()
    if not isinstance(value, dict):
        return none_behavior()
    gesture = value.get("gesture", "none")
    expression = value.get("expression", "none")
    spatial = value.get("spatial_intent", "none")
    if not (enum_value(gesture, GESTURES) and enum_value(expression, EXPRESSIONS)
            and enum_value(spatial, SPATIAL_INTENTS)):
        return none_behavior()
    try:
        intensity = bounded_number(value.get("expression_intensity", 0.0), 0, 1)
        hold = bounded_number(value.get("expression_hold", 3.0), 0, 10)
    except (ValueError, OverflowError):
        return none_behavior()
    if expression in ("none", "neutral"):
        intensity = hold = 0.0
    return dict(gesture=gesture, expression=expression, expression_intensity=intensity,
                expression_hold=hold, spatial_intent=spatial)

def normalize_reply(result, rescue=False):
    if not isinstance(result, dict):
        raise ValueError("Reply must be a JSON object")
    speech = result.get("speech")
    if not isinstance(speech, str) or not speech.strip():
        raise ValueError("Reply speech must be a non-empty string")
    action = result.get("action", "CHAT")
    action_valid = enum_value(action, ACTIONS)
    return {"action": "CHAT" if rescue or not action_valid else action,
            "speech": speech.strip(),
            "behavior": none_behavior() if rescue or not action_valid else validate_behavior(result.get("behavior"))}
