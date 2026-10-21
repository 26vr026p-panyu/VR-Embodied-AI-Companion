# Dependency and source selection audit

Explicit whitelist: AgentContextModule, CompanionBehaviorModule, CompanionCapabilityModule, ExerciseAgent application scripts, AvatarMotionModule, CompanionFollowModule, LipSyncModule, EchoCancellationModule, QuestPlatformModule, DemoPresentation, application Scripts and WindsongIntegration/Runtime. Only .cs/.cs.meta and capability JSON were selected, not enclosing asset directories.

Backend whitelist: root application .py (excluding the private transcribe_test helper), tts/**/*.py, capabilities/**/*.py, eval/**/*.py, state schema, capability manifest, ASR/runtime and synthetic-eval config. No installed Python, Lib, Scripts environment, voice, model/service environment, work, outputs, codex, logs or private dataset copied.

Backend TTS config replaced **in staging only** with a generic asset-free example; reference text/audio/weight locations removed. A few internal evaluation defaults were replaced with nonfunctional LOCAL_ONLY_PATH_NOT_DISTRIBUTED placeholders. Callers must provide their own paths. See source-manifest for exact copied-file hashes and staging-only transformations.

Unity imports reference UnityEngine, InputSystem, TMPro, Newtonsoft.Json, Meta/OVR, UnityMMDTools. Their packages/types may not be available in a fresh environment; no stubs were invented to claim compilation. Scene/Prefab/Animator/BlendShape/bone wiring remains a manual dependency.

Manifest ownership/GUID continuity does not license third-party dependencies. Selected integration code provenance still requires human review; no blanket source license was selected.
