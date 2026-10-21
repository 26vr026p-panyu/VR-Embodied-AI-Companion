# Phase 4-P5 — video and release review

**P6 owner decision2026-10-22:** exact Demo/scope, source authority and described-use media review approved; MIT confirmed for authorized original source. The checkpoint findings below remain historical, including its then-pending approval statements. Current publication is tracked by the P6 report.


2026-10-21. **Local final-review candidate; not published.** Source and Demo are one proposed release. Nothing is uploaded before the owner previews and approves the exact scope.

| Required item | Result |
|---|---|
| VIDEO RECORDING | PASS — new continuous Unity recording, not historical v3 |
| VIDEO DURATION | 66.87s;1280×720;15fps;H.264/AAC |
| USER INPUT SUBTITLES | PASS — actual Japanese preset text, input method disclosed |
| AI OUTPUT SUBTITLES | PASS — actual new speech, synchronized to playback events |
| NATIVE TOOL BEHAVIOR EVIDENCE | PASS — real auto-selected Lead and Wave; Unity validation, Orchestrator acceptance, returned ToolResult and LLM continuation |
| VISUAL GAIN DEMO | NOT SHOWN — unavailable motion source shown honestly; no sensor or AI Gain claim |
| MEDIA RIGHTS REVIEW | REVIEW REQUIRED — contacted parties' consent confirmed; exact-artifact/provenance approval remains, upstream voice rights not independently verified |
| SOURCE LICENSE REVIEW | REVIEW REQUIRED — source authority and root license remain owner decisions |
| PUBLIC TEST | PARTIAL — fresh68 PASS/11 SKIP,exit0; skipped gates remain unverified |
| SECURITY SCAN | PASS within documented file/content/binary whitelist scope; not blanket DLP clearance |
| ORIGINAL UNITY | UNCHANGED —137 monitored original files across Unity/Backend; temporary Driver added then removed; no Scene save |
| ORIGINAL BACKEND | UNCHANGED — monitored source/config; ordinary runtime logs are separate |
| VIDEO READY | YES for owner preview; not automatic legal/audiovisual approval |
| GITHUB RELEASE CANDIDATE | BLOCKED pending owner exact-video/scope approval and source/license decision |
| PUBLISHED | NO |

## Preview and exact files

[![New Demo](media/demo-thumbnail.png)](media/VR_Embodied_AI_Companion_Demo_JA.mp4)

- [MP4](media/VR_Embodied_AI_Companion_Demo_JA.mp4)
- [Technical/media review](media/demo-review.md)
- [Sanitized per-turn evidence](docs/demo-evidence.json)
- [Repository file list and hashes](RELEASE_CANDIDATE_FILES.json)
- [Final validation](docs/p5-validation.json)
- [Release notes](docs/RELEASE_NOTES_v0.1_CANDIDATE.md)

MP4 size is under7MiB, so the current candidate uses an ordinary relative `media/` link instead of inventing a public URL or uploading a Release Asset. README English/Japanese first-screen sections show the actual local thumbnail/video link. This does not create a public GitHub address. Raw frames, audio stems, private traces and initial incomplete take are outside staging.

## What is actually demonstrated

New Main_WindsongIntegration Desktop session: Idle → Japanese conversation → sustained Side walking → explicit Lead request and real native tool/result loop → Run → stop/face user → explicit Wave request and real native tool/result loop → Idle. Camera stays with the virtual user; no per-case teleports or third-person cuts. Internal recording at720p/15fps was chosen for stability; no Unity window/layout operations were used.

All four replies use the real Backend/DeepSeek and current production streaming Roxy3. Per-turn provider selection is `gpt_sovits / roxy3`, playback completes, no fallback/underrun event is recorded. Existing neutral/REFEX01 profile remains unchanged. VoiceAnchor source audio and lip movement are live. Sound is captured at the source filter, not the complete 3D/HRTF output mixer.

Lead and Wave were selected by DeepSeek with `tool_choice=auto`; temporary Driver did not submit Debug behaviors, force these tool choices or fabricate a ToolResult. The tool result reports dispatch acceptance, not already-completed animation. Side was the existing formation; the accompanying dialogue did not generate another Side call.

Preset Japanese text and virtual movement are explicitly disclosed. No microphone ASR, true exercise input, physical sensor or new Quest/HRTF validation is implied. Visual Gain is retained in the project/research documentation but not shown as active in this footage. User→playback waits remain approximately3.0–4.8s in the complete take; no waiting cuts, fake Listening state or action/speech speedup was used.

## Review findings preserved

- Whole-timeline samples, consecutive Wave frames and captioned frames reviewed. Front-facing Wave, Lead movement and live character animation are visible. User and AI captions remain separate and the subtitle panel avoids the HUD.
- Existing Wave dispatch occurs about2.67s before its spoken acknowledgement. It is disclosed, not hidden or repaired by editing. No production timing change was made.
- Source audio is finite/nonempty, all four utterances finish, no stream underrun recorded. Telemetry does not certify perceptual lip alignment. Full subjective voice, synchronization and visual acceptance is the owner's remaining preview step.
- No private names, local paths, raw JSON or developer buttons seen in sampled frames. First incomplete take was retained privately and not packaged.
- Old PCVR/microphone reliability and performance limitations remain in README. This new Desktop video does not clear them.

## Rights and source review

Contacted modifier观海子/观海 and Roxy3 publisher独孤欲雪 have confirmed the described noncommercial research/portfolio use. Their private replies remain private and unchanged; no official miHoYo legal permission or original performer endorsement is inferred. Full credits and AI-generated/unofficial disclosure are visible/included.

P5 checked the actual Meadow prefab closure:41 dependencies, all project AnimeStraightTrack; generator and README match the environment. Installed SimpleNaturePack/EasyRoads packages are not dependencies of that selected prefab. Do not keep treating their presence alone as proof that this visible map is imported from them. This resolves that technical attribution question but is not independent proof of authorship.

No BGM or new imported motion added. Existing procedural motion remains unchanged. HUD references Noto Sans JP/Liberation Sans; overlay uses Windows Yu Gothic as raster pixels, no fonts redistributed. Official terms/source links and exact review limits are in [demo-review](media/demo-review.md). No source asset, model weight, reference waveform, private recording or authorization screenshot is staged.

Before publication, the owner still needs to confirm authority for selected application/generated source and exact media scope, choose the code license or explicitly approve a visible portfolio without a permissive reuse grant, and approve this exact video. [License options](LICENSE_OPTIONS.md) are unchanged. No copyright holder or root LICENSE was invented.

## Cleanup and stop

Temporary recording Driver and its owned metadata were hash-checked and moved out of Assets. Exit checks:Scene dirty=false,Missing Script=0,Adapter/core references complete. Final Refresh compiled without C# errors. No additional Play, Quest repair, production change, disk cleanup, git init, remote, push or upload. Waiting for owner review.
