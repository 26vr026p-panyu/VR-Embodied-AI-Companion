# Evaluation and evidence provenance

## P2 current public test result —2026-10-21

**68 PASS / 11 SKIP /0 FAIL /0 ERROR, exit0.** This is not79 tests all passing, a new live-provider run, or Quest acceptance. [Case results](../eval/evidence/packaging-contract-tests-2026-10-21.json).

The old reliability.policy_known_issue failure remains recorded below. P2 extracted only numeric fields from the existing real Phase4-1B report:12 production utterances,0 true underruns,0 runtime errors, with the original report SHA256 and source-modification timestamp. [Sanitized evidence](../backend/eval/evidence/roxy-known-issue-sanitized.json) contains no text/audio/session identity/private path. The existing record now points to it; original assertions and runtime source are unchanged. It revalidates a narrow historical Desktop resolution, not current Quest speech/barge-in reliability.

### Exact command executed successfully

From the repository's backend directory in the already-provisioned source environment:

```powershell
$env:PYTHONDONTWRITEBYTECODE='1'
python -m eval.runner all --levels unit --unity-project ../unity --output ../.local-review/final-unit
```

The full final invocation returned exit0. No clean dependency installation is claimed. Temporary detailed reports were summarized and removed from staging; do not publish future raw reports blindly.

### Eleven SKIP cases

All11 currently stop at an absent private measurement-report path. They do not directly open a WAV, request a live provider, launch Unity or require a connected Quest during this invocation. Their underlying measurements would require those environments. [Per-case classification](../eval/evidence/public-skip-reasons.json).

| Cases | Missing evidence |
|---|---|
| asr.baseline_quality | Retained physical-WAV quality/hash report |
| asr.candidate_quality | Same-corpus candidate quality report |
| asr.warm_latency | Repeated warm-inference report |
| asr.model_resident | Live model-residency/instance report |
| asr.readiness | Live readiness report |
| tts.ready_state | Actual local-provider readiness report |
| tts.first_pcm_cold | Actual cold first-PCM report |
| tts.first_pcm_warm | Warm first-PCM/discarded-warmup report |
| tts.true_underrun | Full producer/Unity playback evidence |
| tts.buffer_depth | Full stream buffer/drop/EOS report |
| tts.barge_cancel | Unity/local-provider cancellation report, not physical Quest barge-in |

The compact known-issue fixture does not supply all of these fields; no metrics are invented to remove SKIPs. Integration needs isolated HTTP/ffmpeg fixtures, Unity needs original wiring/driver/assets, live tests need configured providers, and hardware tests need actual Quest. These layers are not rerun here.

## v0.1 packaging run — historical record (2026-10-21)

Ran the selected staged sources in the existing Windows Python environment, not a clean dependency installation:

```text
cd backend
python -m eval.runner all --levels unit --unity-project ../unity --output <private-output>
```

**67 PASS / 1 FAIL / 11 SKIP, exit code 1.** See [sanitized case statuses](../eval/evidence/packaging-contract-tests-2026-10-21.json).

- PASS scopes include immutable state, bounded General context, malformed-state degradation, completion/cancel history safety, parser/schema fixtures, capability/argument validation, native chunk assembly/identity/result contracts, semantic intent policy and bounded fallback checks.
- FAIL: reliability.policy_known_issue requires the actual repeated Roxy production measurement report. That private report is deliberately not distributed; a source-resolution record alone cannot satisfy its evidence check. The failure remains visible. No test was rewritten to turn it green.
- SKIP: five measured ASR and six TTS runtime gates need private measurement inputs. Missing evidence is not PASS.
- Earlier first packaging attempt: 67 PASS, 1 ERROR, 11 SKIP because known_issues.json was initially absent. Added a sanitized record, reran, and retained the final FAIL above. Full private reports remain outside staging.
- Python syntax:58/58 passed without importing the production server or loading models.
- Integration HTTP, live DeepSeek, real TTS, public-tree Unity compilation/Play, hardware and clean install were **NOT RUN by packaging**.

## Historical prototype evidence — different runs, not one score

| Evidence | Basis / scope | Public availability |
|---|---|---|
| Phase3 context/state/evaluation/tool/reliability | Development contracts, controlled fixture integration and separate live-provider tests | Selected reusable test code; full raw reports remain private |
| Phase3-4b tool selection/result grounding | Real DeepSeek selection plus Unity result feedback; narrow tool-choice policy | Architecture/source, not a new model-quality claim |
| Phase4-1A medium runtime | Same-boundary retained physical WAV,24 warm samples,9 scored utterances | Sanitized aggregate, not waveform/ground truth |
| Phase4-1B Roxy readiness/underrun | Controlled Desktop repeated playback,12 utterances plus2 real LLM turns; historical narrower resolution | Source known-issue record; no private report/media |
| 2026-10-21 Quest PCVR | Hardware observations,5 barge attempts; seven45s performance groups | Honest status and numeric aggregate, not personal audio |

Do not sum old PASS counts across versions. Unit fixtures use synthetic client responses; they do not prove actual provider behavior, physical voice quality or avatar naturalness. Hardware observations do not substitute for clean-build validation.

## Reproduction and privacy

Runner subprocesses isolate test cases and enforce timeouts. Integration fixtures use a separate loopback service and synthetic tone. Private audio is opt-in and is not copied to reusable datasets. Generated reports can contain paths, trace IDs and content; keep local output private rather than blindly committing it.

The pure unit command does not load Whisper through server import: selected definitions use source-loader AST fixtures. The full Backend intentionally has heavier prerequisites. Missing ffmpeg, Unity driver, reports, authorized voice service or model assets must be reported as unavailable—not guessed successful.

## P3 fresh unit rerun —2026-10-21

Existing unit command rerun after credits/disclosure edits:68 PASS,11 SKIP,exit0. [Sanitized P3 results](../eval/evidence/p3-contract-tests-2026-10-21.json). Same measurement-dependent limits apply; no new live provider, Unity or Quest validation.

## P4 fresh public unit check

Authorization documentation does not change technical acceptance. Existing unit command freshly returned68 PASS/11 SKIP,exit0. [P4 case statuses](../eval/evidence/p4-contract-tests-2026-10-21.json). No new live/Unity/Quest test; unchanged missing-measurement limits.

## P5 fresh public contracts

Recorded-video packaging left runtime source unchanged. Fresh unit run:68 PASS/11 SKIP,exit0. Skipped report/Unity/live gates are not promoted. [Sanitized results](../eval/evidence/p5-contract-tests-2026-10-21.json).

## P6 release contract check —2026-10-22 JST

Fresh68 PASS/11 SKIP,exit0; no application source change. [Exact command/environment and case results](../eval/evidence/p6-contract-tests-2026-10-22.json). This does not verify Unity build, complete Quest acceptance, Android Standalone or exercise efficacy.
