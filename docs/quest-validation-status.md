# Quest PCVR validation — 2026-10-21

**Overall: PARTIAL.** Packaging is not a new hardware acceptance run.

## Verified within scope

- Quest 2 Link/Air Link HMD/controller tracking, stereo/head-follow observation.
- Core semantic input, Development Nod/Wave, observed Presentation blocking and Gain toggle.
- Gaze, LipSync, Wave and spatial audio direction/distance functional observations.
- Moving Lead/Side/Trail using **temporary simulated Rig movement** and physical controllers, not exercise sensors/physical running.
- Basic room-scale: user confirmed stable view, no orbit/side-switch from head turns. Not precise gain calibration.

## Not accepted

- First-attempt speech detection: missed normal/soft voice; repeated attempts needed. Noise/breath/typing caused a false Turn.
- Physical barge-in: **3 confirmed successes / 5 attempts**. First two failed/unconfirmed; later success does not erase failure.
- Static spatial outcomes: Lead/Side Accepted while effective Stay/no relocation. Spoken promise differs from visible result.
- Lead→Trail visibly turns around; Side→Trail can simply slow. Recorded, not repaired.
- Wave dispatch precedes playback **3.32s**; not a measured animation-first-frame interval.
- PCVR Combined frame p95 **27.85ms**, Conversation max **≈1014ms**, 72Hz reference **13.89ms**.
- VR HUD/ergonomics and spatial transition smoothness remain deferred.
- Android Standalone **NOT STARTED**.

## Evidence boundary

Private source reports: phase4-2a-fix/quest-pcvr-retest-results.json, quest-barge-in-results.json, quest-formation-results.json and quest-performance-updated.json. Only sanitized aggregate counters are bundled. No raw WAV, personal scripts/transcripts or full logs.

N01–N10 were **not ten first-attempt successes**. Failures/retries and later rechecks remain failures of reliability. No aggregate CER is claimed for spontaneous speech/user wording mistakes. PCM continuity alone does not establish reliable speech onset.

Private Scene cleanup: not saved, hash unchanged, Missing Script=0, references checked, temporary drivers removed. This is not compilation/Play of the stripped public tree.

Portfolio wording: **Desktop-validated prototype with partial Quest PCVR hardware validation.** P2 changes no hardware result and performs no new Quest test.
