"""Process-local logical cancellation and playback-confirmed history commit."""
import threading
from dataclasses import dataclass
from fastapi import HTTPException
from latency_trace import emit, current_turn_key

@dataclass
class Entry:
    state: str = "active"
    reason: str = ""
    pending: tuple | None = None

class TurnCancellationRegistry:
    def __init__(self):
        self._lock = threading.RLock()
        self._entries = {}

    def register(self, key):
        if key is None:
            return
        with self._lock:
            self._entries.setdefault(key, Entry())

    def check(self, key, stage):
        if key is None:
            return
        with self._lock:
            entry = self._entries.setdefault(key, Entry())
            if entry.state != "active":
                emit("backend_result_discarded", stage=stage, reason="turn_" + entry.state)
                raise HTTPException(status_code=409, detail="turn_" + entry.state)

    def cancel(self, key, reason):
        with self._lock:
            entry = self._entries.setdefault(key, Entry())
            if entry.state == "completed":
                return {"status": "completed", "cancelled": False}
            if entry.state != "cancelled":
                entry.state = "cancelled"
                entry.reason = reason
                entry.pending = None
            emit("backend_cancel_registered", reason=reason)
            return {"status": "cancelled", "cancelled": True}

    def prepare_reply(self, key, session, user, speech):
        with self._lock:
            self.check(key, "history_prepare")
            entry = self._entries[key]
            entry.pending = (session, user, speech)
            emit("history_reply_pending")

    def complete(self, key, sessions):
        # Lock covers state check, history append, and terminal transition.
        # Cancel wins => no commit. Complete wins => cancellation cannot undo an already completed turn.
        with self._lock:
            entry = self._entries.get(key)
            if entry is None:
                raise HTTPException(status_code=409, detail="unknown_turn")
            if entry.state == "cancelled":
                emit("backend_result_discarded", stage="history_commit", reason="turn_cancelled")
                raise HTTPException(status_code=409, detail="turn_cancelled")
            if entry.state == "completed":
                return {"status": "completed", "committed": False, "already_completed": True}
            committed = False
            if entry.pending is not None:
                session, user, speech = entry.pending
                history = sessions.setdefault(session, [])
                history.extend([{"role": "user", "content": user}, {"role": "assistant", "content": speech}])
                del history[:-20]
                entry.pending = None
                committed = True
                emit("history_committed")
            entry.state = "completed"
            return {"status": "completed", "committed": committed}

    def snapshot(self, key):
        """For deterministic tests; not exposed as a production endpoint."""
        with self._lock:
            entry = self._entries.get(key)
            return None if entry is None else {"state": entry.state, "pending": entry.pending is not None}

registry = TurnCancellationRegistry()

def check_turn(stage):
    key = current_turn_key()
    registry.register(key)
    registry.check(key, stage)
    return key
