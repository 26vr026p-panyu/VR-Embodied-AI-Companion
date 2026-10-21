# Voice pipeline

```mermaid
flowchart LR
  M[Mic ring] --> P[400ms pre-roll]
  P --> V[VAD soft 350ms hard 700ms]
  V --> W[Complete Turn WAV]
  W --> A[Faster-whisper medium int8 forced JA]
  A --> S[Conditional supplement 500ms]
  S --> L[Streaming DeepSeek]
  L --> Q[Sentence readiness]
  Q --> R[SpeechArbiter]
  R --> T[Streaming TTS]
  T --> G[Isolated GPT-SoVITS service]
  G --> B[PCM buffer]
  B --> O[VoiceAnchor single AudioSource]
  O --> I[Lip Sync playback timeline]
```

Production JA ASR is **complete-WAV**, not native online. WebSocket partial/stable/final ASR and alternate models are experimental: physical Japanese quality did not pass. Explicit language identity follows the Turn, not noisy text-script guesses.

DeepSeek sentences feed SpeechArbiter. StreamingTtsSession parses framed PCM from Backend. GPT-SoVITS dependencies stay outside Main Backend. Runtime, weights, reference waveform/transcript are excluded. Roxy3/REFEX01 neutral identity describes private production configuration, not redistribution permission.

Cancellation can stop old audio, close mouth and discard stale results; **latest physical Quest barge-in was only 3/5**. Correct downstream cancellation is distinct from reliable speech detection. AEC reference/suppression code is experimental; production selected PCM is raw. No production WebRTC AEC3.

One head/face VoiceAnchor AudioSource uses spatialBlend=1 and Doppler=0, with LipSync bound to that same TTS playback. Placement/rolloff are Scene wiring, absent here. Direction/distance function was observed on Quest; perceptual direction transition remains deferred.
