"""Optional, fail-open latency observation. No payloads, auth headers or audio."""
import contextvars
import json
import os
import queue
import re
import threading
import time
import uuid
from contextlib import contextmanager
from pathlib import Path

_origin = time.perf_counter()
_context = contextvars.ContextVar("latency_context", default=None)
_queue = queue.Queue(maxsize=8192)
_started = False
_lock = threading.Lock()
_fields = {"queue_wait_ms", "chunk_interval_ms", "chunk_index", "approximate_input_tokens", "output_tokens", "snapshot_version", "snapshot_age_ms", "context_chars", "recent_turn_count", "included_sections", "duration_ms", "status", "status_code", "response_length", "text_length", "language", "attempt", "audio_duration", "stage", "reason", "operation", "provider", "voice_id", "model", "synthesis_duration_ms", "audio_duration_ms", "rtf", "fallback", "requested_provider", "actual_provider", "fallback_reason", "capture_id", "sample_rate", "sequence_id", "sample_count", "revision", "decode_queue_ms", "backlog_audio_ms", "final"}

def _writer():
    # Trace errors must never propagate into the request pipeline.
    target = Path(os.environ.get("LATENCY_TRACE_DIR", str(Path(__file__).parent / "latency_traces")))
    path = target / (time.strftime("backend-%Y%m%d-%H%M%S", time.gmtime()) + "-" + str(os.getpid()) + ".jsonl")
    while True:
        row = _queue.get()
        try:
            target.mkdir(parents=True, exist_ok=True)
            with path.open("a", encoding="utf-8") as f:
                f.write(row + "\n")
        except Exception:
            pass
        finally:
            _queue.task_done()

def emit(event, **metadata):
    global _started
    try:
        ctx = _context.get()
        if ctx is None:
            return
        row = dict(ctx, component="backend", event=event,
                   utc_ms=time.time_ns() // 1_000_000,
                   mono_ms=(time.perf_counter() - _origin) * 1000)
        row.update({k: v for k, v in metadata.items() if k in _fields})
        with _lock:
            if not _started:
                threading.Thread(target=_writer, daemon=True, name="latency-jsonl").start()
                _started = True
        _queue.put_nowait(json.dumps(row, ensure_ascii=True, allow_nan=False))
    except Exception:
        pass

@contextmanager
def span(name, **metadata):
    start = time.perf_counter()
    emit(name + "_started", **metadata)
    status = "ok"
    try:
        yield
    except BaseException:
        status = "error"
        raise
    finally:
        emit(name + "_finished", duration_ms=(time.perf_counter() - start) * 1000,
             status=status, **metadata)

class LatencyTraceMiddleware:
    """ASGI send observation: response_sent means final body handed to ASGI, not client receipt."""
    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope.get("type") != "http" or scope.get("path") not in {"/agent", "/agent/stream", "/tts", "/transcribe", "/turn/cancel", "/turn/complete", "/lipsync/plan"}:
            return await self.app(scope, receive, send)
        token = None
        try:
            headers = dict(scope.get("headers", []))
            raw_id = headers.get(b"x-trace-id", b"").decode("ascii", "ignore")
            trace_id = raw_id if re.fullmatch(r"[A-Za-z0-9_-]{1,100}", raw_id) else "backend-" + uuid.uuid4().hex
            raw_turn = headers.get(b"x-turn-id", b"").decode("ascii", "ignore")
            turn_id = int(raw_turn) if re.fullmatch(r"[0-9]{1,15}", raw_turn) else None
            token = _context.set(dict(trace_id=trace_id, turn_id=turn_id,
                                      request_id=uuid.uuid4().hex, endpoint=scope["path"],
                                      clock_id="backend-" + str(os.getpid()),
                                      lifecycle_managed=bool(re.fullmatch(r"[A-Za-z0-9_-]{1,100}", raw_id) and turn_id is not None)))
        except Exception:
            pass
        emit("request_received")
        status = None
        async def traced_send(message):
            nonlocal status
            if message["type"] == "http.response.start":
                status = message["status"]
            await send(message)
            if message["type"] == "http.response.body" and not message.get("more_body", False):
                emit("response_sent", status_code=status)
        try:
            await self.app(scope, receive, traced_send)
        finally:
            if token is not None:
                _context.reset(token)

def flush():
    """For tests / orderly process shutdown only; never on request hot path."""
    _queue.join()


def current_turn_key():
    ctx = _context.get()
    return (ctx["trace_id"], ctx["turn_id"]) if ctx and ctx.get("lifecycle_managed") else None

@contextmanager
def websocket_context(trace_id, turn_id, stream_id, endpoint="/asr/stream"):
    token = _context.set(dict(trace_id=trace_id,turn_id=turn_id,request_id=stream_id,
        endpoint=endpoint,clock_id="backend-"+str(os.getpid()),lifecycle_managed=True))
    try: yield
    finally: _context.reset(token)
