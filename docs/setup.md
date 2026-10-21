# Setup and reproducibility

## A. Source reading and pure contracts

P2 successfully executed the existing unit command in the provisioned Windows environment:68 PASS,11 SKIP, exit0. [Exact tested command and skipped dependencies](evaluation.md). The pip/venv recipe below is manual preparation guidance, **not a clean-install result**.

This is not a ready-to-open Unity project. Original environment: Unity6000.4.11f1; InputSystem1.19.0; URP17.4.0; OpenXR1.16.1; MetaXR203.0.0; UnityMMDTools0.5.1. Packages/manifest.reference.json records references, not permission to redistribute SDKs.

Python source environment uses3.13-era syntax. requirements.observed.txt records installed versions, **not a clean-tested portable lockfile**. Provision a separate environment and review dependency terms. Do not install GPT-SoVITS into Main Backend.

```powershell
# Repository root; dependency provisioning is manual and not claimed clean-tested.
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r backend/requirements.observed.txt
cd backend
python -m eval.runner all --levels unit --unity-project ../unity --output ../local-eval
```

Contract fixtures use scripted clients and selected production AST definitions, not real providers. Evidence-dependent ASR/TTS cases may SKIP without private reports. Exit0 is not all gates PASS. Do not run eval/unity_setup.py against a production project by accident: the private temporary Unity Driver/Scene are not distributed. Integration audio fixture needs ffmpeg at its documented local location; it is not bundled.

## B. Backend manual prerequisites — not live-verified here

1. Supply your own DeepSeek credential privately; .env.example contains no secret. Never put credentials in Unity.
2. tts/config.json is an **asset-free example**, identical to config.example.json. Configure authorized weights/reference/transcript and an independently provisioned voice service. roxy3 is a compatibility wire ID, not a distributed voice.
3. GPT provider expects /tts, /tts/stream, provider/profile headers and rate/length-framed PCM. The isolated runtime/service is omitted; do not assume stock upstream API compatibility.
4. Provision Whisper weights/cache; server.py loads the resident model during import. Wait for /asr/health.
5. Local launch after configuration: python -m uvicorn server:app --host 127.0.0.1 --port 8000 --no-use-colors.
6. No public authentication/TLS readiness. Do not expose prototype ports.

## C. Unity reference-only integration

Acquire SDKs under their terms, create your own Avatar/Scene/Animator/BlendShape bindings, install TMPro/Newtonsoft/InputSystem/XR. UMTAnimatorPoseAdapter depends on UnityMMDTools; Meta/OVR APIs also occur. No SDK source is copied. Bone/clip/controller/Scene wiring is absent, so this is not drop-in playable.

BackendEndpointConfig leaves Quest LAN empty; no private LAN address is supplied. No new locomotion implementation or Android build.

| Scope | Classification |
|---|---|
| Source reading | Included |
| Python syntax and deterministic contracts | Tested in existing environment; see evaluation |
| Clean dependency install | NOT TESTED |
| Full public-tree Backend live start | NOT TESTED; service/assets/credential required |
| Public-tree Unity compile/Play | NOT TESTED; omitted Scene/assets/SDK wiring |
| Private prototype regressions | Historical evidence, not rerun by packaging |
| Android | NOT STARTED |
