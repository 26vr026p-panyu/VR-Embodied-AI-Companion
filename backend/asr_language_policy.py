"""Per-turn ASR language identity. Explicit modes never inspect output script."""
import re
VALID_MODES=('ja','zh','auto')
def normalize_mode(mode,default='auto'):
    mode=default if mode in (None,'') else mode
    if mode not in VALID_MODES:raise ValueError('unsupported_language_mode')
    return mode

def reply_language(mode,text,recognized=None):
    mode=normalize_mode(mode)
    if mode!='auto':return mode,'explicit_turn_language'
    if recognized and recognized!='auto':return recognized,'recognizer'
    # Legacy metadata only; never overrides explicit identity or constrains decoding.
    if re.search(r'[\u3040-\u30ff]',text):return 'ja','auto_script_heuristic'
    if re.search(r'[\u3400-\u4dbf\u4e00-\u9fff]',text):return 'zh','auto_script_heuristic'
    return 'en','auto_script_heuristic'
