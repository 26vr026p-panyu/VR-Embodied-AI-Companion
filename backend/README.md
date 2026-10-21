# Selected Backend

Application Python, state/capability schemas, provider adapters and evaluation harness are included. Python installation/site-packages, model environments, weights, logs, conversation history, mic audio and Codex history are not.

tts/config.json is an asset-free example, not production config. See ../docs/setup.md. Syntax/AST tests avoid production model load and real credentials. Public-tree live deployment is NOT TESTED.

## Reading / testing entry points

server.py installs endpoints and loads Whisper at import (not a lightweight unit entry). Read agent_state.py → agent_context.py → native_tools.py → capabilities/registry.py for the decision/result path; agent_streaming.py and tts/ own output flow. The production voice service/environment and assets are absent.

From this directory: python -m eval.runner all --levels unit --unity-project ../unity --output ../.local-review/unit. P2 confirmed exit0 with68 PASS/11 SKIP in the existing environment. Only the public contract/available-evidence subset runs independently of production services.
