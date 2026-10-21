"""Standard-library-only voice reference profiles; no model dependencies."""
from dataclasses import dataclass
from pathlib import Path
import json

@dataclass(frozen=True)
class VoiceProfile:
    voice_id: str
    style: str
    reference_audio: str
    prompt_text: str
    prompt_language: str
    seed: int = 42
    speed: float = 1.0
    profile_id: str = 'neutral'
    status: str = 'production'
    prompt_verified: bool = True
    prompt_source: str = 'REFEX01 verified production baseline'
    suspected_typo: bool = False
    selection_note: str = ''

class VoiceProfileBank:
    def __init__(self, voices, bank_path=None):
        self.neutral = {vid:VoiceProfile(vid,'neutral',v['reference_audio'],v['prompt_text'],v['prompt_language'],v.get('seed',42),v.get('speed_factor',1)) for vid,v in voices.items()}
        path=Path(bank_path) if bank_path else Path(__file__).resolve().parents[1]/'VR_ExerciseAgent_TTS/voice_style_bank.json'
        self.experimental = [VoiceProfile(**v) for v in json.loads(path.read_text(encoding='utf8'))['profiles']] if path.exists() else []
        self.by_id={(p.voice_id,p.profile_id):p for p in list(self.neutral.values())+self.experimental}
    def select(self, voice_id, style):
        baseline=self.neutral[voice_id]
        if style=='neutral':return baseline,None
        # Experimental references are available only by explicit profile ID, never production style.
        return baseline, ('voice_emotion_deferred' if style in {'cheerful','calm','concerned','encouraging','experimental_calm'} else 'unknown_style')
    def get(self, voice_id, profile_id):
        return self.by_id[(voice_id,profile_id)]
