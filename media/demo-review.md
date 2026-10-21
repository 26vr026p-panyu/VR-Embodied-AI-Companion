# Demo technical review — P5 local candidate

**P6 owner decision2026-10-22:** exact Demo/scope, source authority and described-use media review approved; MIT confirmed for authorized original source. The checkpoint findings below remain historical, including its then-pending approval statements. Current publication is tracked by the P6 report.


New recording: 2026-10-21. **Not published; owner audiovisual approval required.**

## Artifact and real runtime

`VR_Embodied_AI_Companion_Demo_JA.mp4`: 66.87 seconds, 1280×720, H.264,15fps,48kHz stereo AAC. Capture/output target is15fps; no30fps or headset-performance claim. One continuous Unity Play in Main_WindsongIntegration, same Avatar/road and Desktop player camera. No historical v3 footage/audio. No waiting cuts or speech/action speedup in the final take. Initial incomplete attempt is excluded.

Internal URP frame output and timestamped **current VoiceAnchor AudioSource filter samples** are muxed together. This is live playback from the same take, not old exported replies. It is source-level audio, not a recording of the complete spatial mixer or Quest/HRTF validation.

All four turns selected `gpt_sovits / roxy3`, completed streaming playback and recorded zero underruns/fallbacks. Existing neutral/REFEX01,seed42 remains unchanged. LipSync activity peak0.8 supports, but does not replace, visual/owner review.

## Honest input and timing

Preset Japanese text is sent through the real Unity Agent. Physical microphone/ASR is not exercised; no user voice track is fabricated. Persistent labels disclose Japanese text input and virtual movement test. Processing caption starts at the actual text send event; AI subtitles start at playback. No artificial Listening/endpoint state or fixed fake thinking delay is inserted. User/AI captions match actual submitted/played Japanese, including short-response instructions.

The temporary driver only stages the initial position, controls the virtual player path/camera and requests stationary facing through the existing Follow interface. It submits **no Debug gestures or spatial requests**. Side is the existing formation; the Side confirmation dialogue did not generate another native spatial call. Do not present it as a new Side tool execution.

## Native Tool Calling actually recorded

| Request | Tool | Actual chain |
|---|---|---|
| 少し前を走ってくれる？短く返事して。 | spatial.lead | DeepSeek auto selection → Unity identity/argument/state validation → Orchestrator Agent request Accepted → ToolResult HTTP200 → Backend Accepted result → LLM continuation speech |
| 私に向かって手を振ってくれる？短く返事して。 | gesture.wave | Same real chain, auto selection; no local Debug or fabricated result |

Accepted means dispatch accepted, not physical effect already completed. Actual Lead movement and front-facing Wave were checked in sampled/consecutive frames. [Sanitized per-turn evidence](../docs/demo-evidence.json).

## Visual/audio review and limitations

- Reviewed samples across the whole take, consecutive Wave frames and final caption frames. Character, world, separate Walk/Run, Lead and Wave visibly run. Wave faces the user. This is sampled visual review, not every-pixel/gait certification.
- Subtitle layout was corrected to sit to the right of the lower-left HUD. No debug buttons, JSON, private names, paths or desktop chrome were seen in reviewed frames.
- Wave begins approximately2.67s before speech. This existing behavior is preserved, not edited into false simultaneity. Final resting Avatar is smaller than during Side. Full subjective audiovisual approval is still for the owner.
- HUD truthfully shows unavailable motion source. **Visual Gain NOT SHOWN**; no AI Gain tool, exercise sensor, efficacy, physical ASR or new Quest acceptance claim.
- Four utterances finish; decoded samples are finite and source peak≈0.204. No added music, EQ or pitch editing. This is not independent subjective voice/sync certification.

## Rights and provenance

Character: Genshin Impact / Citlali / 茜特菈莉. Model provided by miHoYo; modified by 观海 (contact观海子). AI voice: Roxy3, publisher独孤欲雪; GPT-SoVITS by RVC-Boss/contributors. Contacted parties' described-use permissions are confirmed in [authorization evidence](../docs/authorization-evidence.json); screenshots stay private. Unofficial technical demo; no original performer endorsement or blanket upstream rights assertion.

Meadow prefab closure contains41 dependencies, all in project AnimeStraightTrack, matching the local mesh/material generator and original-environment README. No SimpleNaturePack/EasyRoads dependency exists in that selected closure. Their installation alone does not establish use in this footage. This technical provenance is not independent proof of authorship.

No music or imported motion was added; current procedural motion is unchanged. HUD references resolve to Noto Sans JP and Liberation Sans. Captions use Windows Yu Gothic burned into pixels; no font binaries are packaged. [Microsoft font FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq) distinguishes bitmap video captions from font redistribution. [Noto Sans JP OFL](https://raw.githubusercontent.com/google/fonts/main/ofl/notosansjp/OFL.txt) and [Liberation OFL](https://raw.githubusercontent.com/liberationfonts/liberation-fonts/main/LICENSE) document upstream terms; exact local font binaries were not independently hash-matched.

**MEDIA RIGHTS REVIEW: REVIEW REQUIRED** for owner's exact-artifact/provenance sign-off, not because contacted authors have not replied. No PMX/textures/weights/reference audio/training data/private microphone recording/screenshots are packaged. Source ownership and root license still require owner decision. Source and this Demo must be approved and released together.

## Cleanup

Owned temporary Driver moved out of Assets after hash verification. Scene dirty=false,Missing Script=0,Adapter references complete at exit; cleanup compile has no C# errors.137 monitored original source/config/Scene files unchanged. No Unity resizing, maximize/restore, Dock change or panel drag. No GitHub upload.
