# Evaluation boundary

Harness code lives in ../backend/eval; sanitized aggregate counters in evidence/. No private utterances, raw microphone WAV, provider response logs, session history or weights.

Synthetic fixtures do not certify physical microphones, provider quality, visual naturalness or Quest hardware. Integration/live/hardware are opt-in and require new authorized setup. See ../docs/evaluation.md. Never sum PASS counts from unrelated historical versions.

Public command from backend/: python -m eval.runner all --levels unit --unity-project ../unity --output ../.local-review/unit. P2 final full invocation:68 PASS/11 SKIP, exit0. This includes authentic historical numeric evidence replay, not new runtime measurements. The old67 PASS/1 FAIL/11 SKIP remains in evaluation history. Per-case skip reasons are in evidence/public-skip-reasons.json. Full private-system integration/live/hardware scopes are larger than this public subset.
