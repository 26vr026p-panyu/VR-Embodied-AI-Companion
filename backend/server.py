import os
import json
import tempfile

from fastapi.responses import Response
from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from asr_language_policy import normalize_mode
import asr_input_guard
from starlette.concurrency import run_in_threadpool
from turn_lifecycle import registry as turn_registry, check_turn
from whisper_runtime import WhisperRuntime, WhisperRuntimeConfig
from pydantic import BaseModel, Field
from fastapi import Query
from tts.models import SpeechRequest, SpeechProviderError
from lip_sync_planner import LipSyncPlanRequest, make_plan
from tts.router import TTSProviderRouter, request_identity
from typing import Literal, Optional, Any
from agent_context import build_agent_messages
from companion_persona import build_system_prompt, build_user_context, RESCUE_INSTRUCTION
from behavior_contract import normalize_reply, none_behavior
from openai import OpenAI
from dotenv import load_dotenv
from latency_trace import LatencyTraceMiddleware, emit as trace_event, span as trace_span


# =========================================================
# Environment
# =========================================================

load_dotenv()

app = FastAPI()
app.add_middleware(LatencyTraceMiddleware)
tts_router = TTSProviderRouter()

conversation_sessions = {}


# =========================================================
# Whisper
# =========================================================

print("Loading Whisper model...")

asr_runtime = WhisperRuntime(WhisperRuntimeConfig.read(
    os.path.join(os.path.dirname(__file__), "asr_runtime_config.json")
))
whisper_model = asr_runtime.load()

@app.get("/asr/health")
def asr_health():
    # Import/model load must complete before Uvicorn accepts requests.
    return asr_runtime.status()

print("Whisper model loaded.")


# =========================================================
# DeepSeek
# =========================================================

client = OpenAI(
    api_key=os.environ.get("DEEPSEEK_API_KEY"),
    base_url="https://api.deepseek.com"
)


# =========================================================
# Data Model
# =========================================================

class ExerciseState(BaseModel):
    elapsedTime: Optional[float] = None
    stepRate: Optional[float] = None
    targetStepRate: Optional[float] = None
    rpe: Optional[float] = None
    visualGain: Optional[float] = None

    mode: Literal["exercise", "general"] = "exercise"
    userMessage: str = ""
    language: str = ""

    sessionId: str = "default"
    stateSnapshot: Any = None


# =========================================================
# Root
# =========================================================

@app.get("/")
def root():
    return {
        "status": "Exercise LLM Agent Server is running"
    }


# =========================================================
# Intervention Gate
# =========================================================

def check_intervention(state: ExerciseState):

    message = state.userMessage.lower()

    warning_keywords = [

        # 中文
        "疼",
        "痛",
        "头晕",
        "晕",
        "恶心",
        "想吐",
        "胸闷",
        "喘不过气",
        "呼吸困难",
        "不舒服",

        # 日文
        "痛い",
        "痛み",
        "めまい",
        "眩暈",
        "気持ち悪い",
        "吐き気",
        "息苦しい",
        "呼吸が苦しい",
        "具合が悪い"
    ]


    # RPE过高
    if state.rpe is not None and state.rpe >= 17:
        return True, "high_rpe"


    # 明显身体不适
    for keyword in warning_keywords:

        if keyword in message:
            return True, f"symptom:{keyword}"


    return False, "none"


# =========================================================
# JSON Parser
# =========================================================

def parse_llm_json(content: str):

    if content is None:
        raise ValueError(
            "LLM content is None."
        )


    content = content.strip()


    if not content:
        raise ValueError(
            "LLM content is empty."
        )


    # -----------------------------------------
    # 去掉 Markdown code block
    # -----------------------------------------

    if content.startswith("```"):

        content = content.strip("`").strip()

        if content.startswith("json"):
            content = content[4:].strip()


    # -----------------------------------------
    # 正常 JSON
    # -----------------------------------------

    try:

        result = json.loads(content)

        if not isinstance(result, dict):
            raise ValueError(
                "LLM JSON response is not an object."
            )

        return result

    except json.JSONDecodeError:
        pass


    # -----------------------------------------
    # 如果前后夹杂其他文字
    # 尝试抽取 {...}
    # -----------------------------------------

    start = content.find("{")
    end = content.rfind("}")


    if (
        start != -1
        and end != -1
        and end > start
    ):

        json_part = content[
            start:end + 1
        ]

        result = json.loads(
            json_part
        )

        if not isinstance(result, dict):
            raise ValueError(
                "Extracted JSON is not an object."
            )

        return result


    raise ValueError(
        "Could not parse LLM response as JSON: "
        + repr(content)
    )


# =========================================================
# Token / Debug Log
# =========================================================

def print_llm_usage(
    response,
    label: str
):

    finish_reason = None

    try:
        finish_reason = (
            response
            .choices[0]
            .finish_reason
        )
    except Exception:
        pass


    print(
        f"{label} finish reason:",
        finish_reason
    )


    usage = getattr(
        response,
        "usage",
        None
    )


    if usage is None:
        return


    prompt_tokens = getattr(
        usage,
        "prompt_tokens",
        None
    )

    completion_tokens = getattr(
        usage,
        "completion_tokens",
        None
    )

    total_tokens = getattr(
        usage,
        "total_tokens",
        None
    )


    print(
        f"{label} token usage | "
        f"input: {prompt_tokens} | "
        f"output: {completion_tokens} | "
        f"total: {total_tokens}"
    )


# =========================================================
# 单次 LLM 请求
# =========================================================

def request_llm(
    messages,
    use_json_mode: bool,
    label: str
):

    request_kwargs = {
        "model": "deepseek-v4-flash",
        "messages": messages,
        "extra_body": {
            "thinking": {
                "type": "disabled"
            }
        },
        "max_tokens": 600
    }


    # 第一轮使用 JSON Mode
    if use_json_mode:

        request_kwargs[
            "response_format"
        ] = {
            "type": "json_object"
        }


    check_turn("deepseek_before")
    with trace_span("deepseek", attempt="primary" if use_json_mode else "rescue"):
        response = (
            client
            .chat
            .completions
            .create(
                **request_kwargs
            )
        )


    check_turn("deepseek_after")
    content = (
        response
        .choices[0]
        .message
        .content
        or ""
    )


    print(
        f"===== {label} ====="
    )

    print(
        repr(content)
    )

    print(
        "============================"
    )


    print_llm_usage(
        response,
        label
    )


    return content


# =========================================================
# LLM
#
# 最多两次请求：
#
# 1. JSON Mode
# 2. 普通模式 Rescue
#
# 空白 / 请求失败 / JSON解析失败
# 都会触发 Rescue
# =========================================================

def call_agent_llm(messages):

    from reliability import FailureRecord,FailureCategory,monitor,Health
    from latency_trace import current_turn_key
    import time
    primary_error = None
    reliability_failure = None
    reliability_decision = None


    # =====================================================
    # Attempt 1
    # 正常 JSON Mode
    # =====================================================

    try:

        content = request_llm(
            messages=messages,
            use_json_mode=True,
            label="PRIMARY LLM RESPONSE"
        )


        if not content.strip():

            raise ValueError(
                "Primary LLM returned empty content."
            )


        # 第一轮严格要求 JSON
        result = parse_llm_json(
            content
        )
        result = normalize_reply(result)


        print(
            "Primary LLM generation succeeded."
        )


        return normalize_reply(result)


    except HTTPException:
        raise
    except Exception as e:

        primary_error = e
        check_turn("reliability_rescue_guard")
        key=current_turn_key() or ('untraced',0)
        reliability_failure=FailureRecord(key[0],key[1],'complete_agent',FailureCategory.Validation if isinstance(e,ValueError) else FailureCategory.LLM,'primary_attempt_failed')
        reliability_decision=monitor.failure(reliability_failure)
        monitor.set_health('LLM',Health.Degraded)


        print(
            "Primary LLM attempt failed:",
            repr(e)
        )


    # =====================================================
    # Attempt 2
    # Rescue
    # =====================================================

    print(
        "Trying rescue LLM generation..."
    )


    rescue_messages = [
        dict(message)
        for message in messages
    ]


    if (
        rescue_messages
        and rescue_messages[0].get("role")
        == "system"
    ):

        rescue_messages[0]["content"] += "\n\n" + RESCUE_INSTRUCTION


    try:

        check_turn("reliability_rescue_start")
        monitor.event('retry_started',reliability_failure,reliability_decision,retry_count=1)
        rescue_began=time.perf_counter()
        content = request_llm(
            messages=rescue_messages,
            use_json_mode=False,
            label="RESCUE LLM RESPONSE"
        )


        content = content.strip()


        if not content:

            raise ValueError(
                "Rescue LLM returned empty content."
            )


        try:
            result = parse_llm_json(content)
        except Exception:
            if content.startswith(("{", "[", "```")):
                raise ValueError("Rescue returned malformed structured content")
            print("Rescue returned plain text. Using it directly as speech.")
            result = {"speech": content}
        # Validation errors here fail safely; they must not speak raw JSON.
        result=normalize_reply(result,rescue=True)
        monitor.event('retry_succeeded',reliability_failure,reliability_decision,retry_count=1,added_latency_ms=(time.perf_counter()-rescue_began)*1000)
        monitor.set_health('LLM',Health.Healthy)
        return result


    except HTTPException:
        raise
    except Exception as rescue_error:
        monitor.event('retry_failed',reliability_failure,reliability_decision,retry_count=1)
        monitor.failure(FailureRecord(key[0],key[1],'json_rescue',FailureCategory.LLM,'rescue_failed',retry_count=1))
        monitor.set_health('LLM',Health.Unavailable)

        print(
            "Rescue LLM attempt failed:",
            repr(rescue_error)
        )


        raise ValueError(
            "Both primary and rescue "
            "LLM generations failed. "
            f"Primary error: {repr(primary_error)} | "
            f"Rescue error: {repr(rescue_error)}"
        )

# =========================================================
# Agent
# =========================================================

@app.post("/agent")
def agent(
    state: ExerciseState
):

    with trace_span("processing"):
        turn_key = check_turn("agent_request_start")
        intervention_required, intervention_reason = (
            check_intervention(state) if state.mode == "exercise" else (False, "general_mode")
        )


        print(
            f"Intervention required: "
            f"{intervention_required} | "
            f"Reason: {intervention_reason}"
        )


        # =====================================================
        # Conversation History
        # =====================================================

        if (
            state.sessionId
            not in conversation_sessions
        ):

            conversation_sessions[
                state.sessionId
            ] = []


        history = (
            conversation_sessions[
                state.sessionId
            ]
        )


        # =====================================================
        # System Prompt
        # =====================================================

        messages = build_agent_messages(state, history, turn_key, intervention_required, intervention_reason)


        # =====================================================
        # Generate
        # =====================================================

        generation_success = False
        behavior = none_behavior()


        try:

            # 这里已经包含：
            #
            # JSON Mode
            # ↓ 失败
            # Rescue
            #
            # 最多两次API调用
            result = call_agent_llm(
                messages
            )


            result = normalize_reply(result)
            trace_event("validation_finished")
            check_turn("validation_after")
            action = result["action"]
            speech = result["speech"]
            behavior = result["behavior"]

            generation_success = True


        except HTTPException:
            raise
        except Exception as e:

            print(
                "Agent generation error:",
                repr(e)
            )


            action = "CHAT"


            # 用户听到自然一些
            # PowerShell里仍然能看到真正错误
            if state.language == "zh":

                speech = (
                    "抱歉，我刚刚好像卡了一下。"
                    "你可以再说一次吗？"
                )

            else:

                speech = (
                    "ごめん、ちょっと反応が"
                    "止まっちゃったみたい。"
                    "もう一度話してもらえる？"
                )


        # =====================================================
        # Save Conversation History
        # =====================================================

        check_turn("history_commit_before")
        if state.userMessage.strip() and generation_success:
            if turn_key is not None:
                # Managed requests commit only after Unity acknowledges completed playback.
                turn_registry.prepare_reply(turn_key, state.sessionId, state.userMessage, speech)
            else:
                # Preserve legacy callers without lifecycle headers.
                history.append({"role": "user", "content": state.userMessage})
                history.append({"role": "assistant", "content": speech})

        # =====================================================
        # 保留最近10轮
        # user + assistant = 20 messages
        # =====================================================

        if len(history) > 20:
            del history[:-20]


        print(
            f"Session: {state.sessionId} | "
            f"History messages: {len(history)}"
        )


        # =====================================================
        # Return to Unity
        # =====================================================

        check_turn("agent_response_before")
        return {
            "action": action,
            "speech": speech,
            "behavior": behavior
        }


# =========================================================
# Whisper STT
# =========================================================

@app.post("/transcribe")
async def transcribe_audio(
    file: UploadFile = File(...),
    language_mode: Literal["ja", "zh", "auto"] = Form("auto")
):

    with trace_span("processing"):
        check_turn("asr_request_start")
        if not asr_runtime.ready:
            raise HTTPException(status_code=503, detail="asr_not_ready")
        trace_event("asr_language_selected",language_mode=language_mode)
        suffix = os.path.splitext(
            file.filename
        )[1]


        if not suffix:
            suffix = ".wav"


        temp_path = None


        try:

            # -------------------------------------
            # 临时文件
            # -------------------------------------

            with tempfile.NamedTemporaryFile(
                delete=False,
                suffix=suffix
            ) as temp_file:

                audio_bytes = await file.read()

                temp_file.write(
                    audio_bytes
                )

                temp_path = (
                    temp_file.name
                )


            # -------------------------------------
            # Whisper
            # -------------------------------------

            def run_whisper():
                with trace_span("whisper"):
                    segments, info = (
                        whisper_model.transcribe(
                            temp_path,
                            language=None if language_mode=="auto" else language_mode,
                            **asr_runtime.config.decode_options(),
                            initial_prompt=(
                                "Unity, VR, Quest, Quest 2, Wattbike, "
                                "RPE, Visual Gain, LLM, DeepSeek, AI Agent, "
                                "cadence, power, heart rate, "
                                "人工知能, 実験, 研究, 運動, "
                                "ワットバイク, ケイデンス"
                            )
                        )
                    )
        
        
                    text_parts = []
        
        
                    decoded_segments = []
                    for segment in segments:
                        decoded_segments.append(segment)
        
                        text_parts.append(
                            segment.text
                        )
        
        
                    full_text = (
                        "".join(
                            text_parts
                        )
                        .strip()
                    )
                    if asr_input_guard.enabled() and asr_input_guard.reject_non_speech(decoded_segments):
                        trace_event("asr_non_speech_rejected", reason="all_segments_high_no_speech_low_confidence")
                        full_text = ""
                return full_text, info

            full_text, info = await run_in_threadpool(run_whisper)
            check_turn("whisper_after")
            check_turn("asr_response_before")


            return {
                "text": full_text,
                "language": info.language,
                "language_probability":
                    info.language_probability
            }


        finally:

            # -------------------------------------
            # 临时音频立刻删除
            # -------------------------------------

            if (
                temp_path
                and os.path.exists(
                    temp_path
                )
            ):

                os.remove(
                    temp_path
                )


# =========================================================
# TTS
# =========================================================

@app.get("/tts")
async def tts(
    text: str,
    language: str = "ja",
    voice_id: str = "",
    style: str = "neutral",
    speed: float = Query(default=1.0, ge=0.5, le=2.0)
):
    with trace_span("processing"):
        check_turn("tts_provider_before")
        if not text.strip():
            return Response(content=b"", status_code=400)
        trace_id, turn_id = request_identity()
        request = SpeechRequest(text, "zh" if language == "zh" else "ja",
                                voice_id, style, speed, trace_id, turn_id)
        try:
            audio, fallback, reason = await tts_router.synthesize(request)
        except SpeechProviderError:
            check_turn("tts_failure_response")
            return Response(content=b"", status_code=503)
        check_turn("tts_response_before")
        headers = {"X-TTS-Provider": audio.provider, "X-TTS-Voice-Id": audio.voice_id,
                   "X-TTS-Model": audio.model, "X-TTS-Fallback": str(fallback).lower(),
                   "X-TTS-Style": audio.style, "X-TTS-Profile": audio.profile_id, "X-TTS-Style-Status": audio.style_status}
        if reason:
            headers["X-TTS-Fallback-Reason"] = reason
        return Response(content=audio.audio_bytes, media_type="audio/mpeg", headers=headers)

class TurnIdentity(BaseModel):
    trace_id: str = Field(min_length=1, max_length=100, pattern=r"^[A-Za-z0-9_-]+$")
    turn_id: int = Field(ge=1, le=999999999999999)

class TurnCancellation(TurnIdentity):
    reason: str = Field(default="client_cancel", max_length=80)

@app.post("/turn/cancel")
async def cancel_turn(request: TurnCancellation):
    return turn_registry.cancel((request.trace_id, request.turn_id), request.reason)

@app.post("/turn/complete")
async def complete_turn(request: TurnIdentity):
    return turn_registry.complete((request.trace_id, request.turn_id), conversation_sessions)


@app.post("/lipsync/plan")
async def lip_sync_plan(request: LipSyncPlanRequest):
    return await make_plan(request)

# Independent ASR transport; existing complete-WAV route retained.
from asr_streaming import install_streaming_asr
install_streaming_asr(app, whisper_model)

# Independent sentence streaming; stable /agent remains unchanged.
import sys as _stream_sys
from agent_streaming import install as _install_agent_stream
_install_agent_stream(app, _stream_sys.modules[__name__])

from tts_stream_endpoint import install as _install_tts_stream
_install_tts_stream(app, tts_router)
