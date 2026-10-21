"""Opt-in final-result guard; never trims audio or changes decoding parameters."""
import math
import os


def enabled():
    # Candidate only. Physical positive/negative acceptance is required first.
    return os.environ.get("ASR_NON_SPEECH_GUARD", "0") == "1"


def reject_non_speech(segments):
    """Reject only when EVERY nonempty segment has both adverse signals.

    Missing/non-finite evidence fails open. A credible segment preserves the
    entire utterance, including low-confidence words; no text blacklist.
    """
    parts = [s for s in segments if getattr(s, "text", "").strip()]
    if not parts:
        return False
    for segment in parts:
        try:
            probability = float(segment.no_speech_prob)
            logprob = float(segment.avg_logprob)
        except (AttributeError, TypeError, ValueError):
            return False
        if not (math.isfinite(probability) and math.isfinite(logprob)):
            return False
        if not (0.0 <= probability <= 1.0):
            return False
        if probability < 0.6 or logprob > -0.5:
            return False
    return True
