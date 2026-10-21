"""Provider-independent viseme plan; Japanese frontend stays in isolated TTS env."""
import json
from pathlib import Path
import httpx
from fastapi import HTTPException
from pydantic import BaseModel, Field
from latency_trace import span
from turn_lifecycle import check_turn

class LipSyncPlanRequest(BaseModel):
    text: str = Field(min_length=1, max_length=4000)
    language: str = "ja"

async def make_plan(request: LipSyncPlanRequest):
    check_turn("lipsync_plan_start")
    if request.language == "zh":
        return {"language":"zh", "source":"amplitude_fallback", "timing":"approximate", "units":[{"phoneme":"unknown","viseme":"aa","kind":"amplitude_fallback"}]}
    if request.language != "ja":
        raise HTTPException(422,"Unsupported lip sync language")
    config=json.loads(Path(__file__).with_name("tts").joinpath("config.json").read_text(encoding="utf-8"))
    url=config["voices"][config["default_voice_id"]]["service_url"].rstrip("/")+"/lipsync/phonemes"
    with span("lipsync_plan"):
        try:
            async with httpx.AsyncClient(timeout=20,trust_env=False) as client:
                response=await client.post(url,json={"text":request.text,"language":"ja"})
                response.raise_for_status()
                phonemes=response.json()["phonemes"]
        except (httpx.HTTPError,KeyError,ValueError) as exc:
            raise HTTPException(503,"lipsync_frontend_unavailable") from exc
        check_turn("lipsync_plan_result")
        units=[]
        vowels={"a":"aa","i":"ih","u":"ou","e":"ee","o":"oh","I":"ih","U":"ou"}
        for p in phonemes:
            if p in vowels:viseme,kind=vowels[p],"vowel"
            elif p in ("pau","sil","cl"):viseme,kind="closed","closure"
            elif p=="N":viseme,kind="neutral","nasal"
            else:viseme,kind="anticipate","consonant"
            units.append({"phoneme":p,"viseme":viseme,"kind":kind})
        return {"language":"ja","source":"pyopenjtalk-plus","timing":"approximate_not_forced_alignment","units":units}
