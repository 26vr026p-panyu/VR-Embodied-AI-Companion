# Performance evidence

## PCVR baseline: complete measurement, budget not met

2026-10-21: Windows Editor + Quest 2 Link/Air Link, seven foreground/XR-valid 45-second intervals. Temporary Rig motion and preset Japanese text into real DeepSeek/Roxy; mic/ASR disabled. Not Android or physical exercise profiling.

See eval/evidence/pcvr-performance-2026-10-21.json for frame median/p95/**max**, main-thread, GC and memory counters. Combined p95=27.85ms; Conversation max≈1014ms. 72Hz reference≈13.89ms. All measured p95 frame intervals exceed that reference. No native headset refresh acceptance follows.

Raw GPU FrameTiming values are not qualified as full stereo/compositor cost. Compositor drops, ASW, thermals and standalone behavior were not measured. Spike cause is not established.

## Historical medium/int8 A/B (separate experiment)

The sanitized ASR aggregate contains 24 warm samples and nine scored utterances, same retained-physical-WAV boundaries on a Ryzen 7 9800X3D-class CPU. Baseline beam/best-of=5 vs selected=1/1, model unchanged: 3204.1→2619.3ms warm median (**18.25% reduction**), CER5.70%→4.82% on that private corpus. Do not pool this comparison with the later single profiling sample≈2638ms or infer universal accuracy.

Cold load, first inference and warm timing are separate. Model remains resident and /asr/health distinguishes readiness from process-alive.

## Reproduction limits

Raw speech/ground truth/models are excluded; external readers cannot recompute private-corpus CER from aggregate JSON. Contract checks can run independently. Live/physical quality needs separately provisioned authorized assets and new measurements. Historical PASS counts are not added across versions.

This 18.25% comparison is warm Japanese inference on retained physical WAV under the stated desktop environment, **not Quest E2E improvement**. Roxy startup/streaming claims here are confined to the historical sanitized known-issue measurement; no new warm-ready numbers were measured by P2.
