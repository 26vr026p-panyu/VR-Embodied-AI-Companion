# Portfolio Packaging Report v0.1

Historical first-packaging snapshot. See [P2 prerelease report](PORTFOLIO_PRERELEASE_REPORT.md) for latest evidence and status; old failure counts below are intentionally retained.

Date:2026-10-21. Local human-review candidate only.

| Field | Result |
|---|---|
| PUBLIC STAGING PATH | ./VR-Embodied-AI-Companion-public (local directory; machine path not published) |
| INCLUDED MODULES | 70 application C# files;58 Python application/eval files; selected schemas, script metadata, asset-free config, sanitized numeric evidence |
| EXCLUDED MODULES / ASSETS | Imported SDKs, original Scenes/Prefabs/Animator/clips, character/map/audio/media/model assets; Library/Temp/Obj; Python environment; Codex history/logs/private datasets/recordings; original .git history |
| COPYING METHOD | Explicit whitelist, per file; NO full Unity/Backend copy |
| LICENSING RISKS | Avatar/voice/map/media permission unconfirmed and assets withheld; source ownership/license selection requires human review |
| SECURITY RISKS | Defined text/path/token/binary scan: no findings; not exhaustive DLP; future local outputs and credentials must remain private |
| SOURCE REPRODUCIBILITY | Source-code Showcase + Architecture Reference; NOT turnkey |
| README STATUS | English + short Japanese prepared; trilingual application material included |
| ARCHITECTURE STATUS | Five Mermaid diagrams structurally checked; full parser/render NOT VERIFIED |
| EVALUATION EVIDENCE | 58 Python syntax PASS;67 contract PASS,1 evidence-dependent FAIL,11 SKIP; no live/Unity/hardware rerun |
| QUEST VALIDATION STATUS | PARTIAL; microphone first-attempt FAIL; physical barge-in3/5; static spatial PARTIAL; PCVR frame budget not met |
| CROSS-PROJECT CONTEXT AUDIT | Scoped source/developer-document scan: no project-specific matches; full chats/Git diff unavailable |
| TAVERN CONTENT CONTAMINATION | NOT FULLY AUDITABLE; no actual source evidence found in scoped scan |
| ORIGINAL UNITY PROJECT | UNCHANGED (monitored application sources/config and Scene; no writes/moves/deletes) |
| ORIGINAL BACKEND | UNCHANGED (monitored application sources/config; no production changes) |
| PORTFOLIO PACKAGING | PARTIAL — review candidate complete; license/reproduction gates remain |
| READY FOR HUMAN REVIEW | YES |
| READY FOR AUTOMATIC PUBLICATION | NO |
| PUBLISHED TO GITHUB | NO |

## Actual verification

137 original source/config/Scene files compared before/after: zero changes. No original Git worktree was found; no Git history was copied or rewritten. Only packaging files under the independent staging/workspace output were written. Original project disk cleanup is deferred to a separate task; no cache or source asset was removed/moved.

Staged Python syntax, JSON parse, relative document link, copied-source hash and file/binary/security checks ran locally. The unit suite's one FAIL is reliability.policy_known_issue: its real private production measurement report was intentionally not bundled. Eleven measured ASR/TTS checks SKIP without their private reports. These statuses are not converted to PASS.

Public config replaces proprietary voice reference/transcript/weight paths with nonfunctional placeholders in staging only. Some evaluation local-path defaults are similarly placeholders. See source-manifest for copied-source transformations; normalized text/newlines may differ bytewise from original. Full public-tree service startup, Unity compilation/Play and clean dependency installation remain NOT TESTED.

Third-party footage exists privately but was not frame-by-frame rights/private-data cleared; no media or fabricated video URL is provided. No replacement avatar/voice or new functionality was developed.

## Entry points

- [English README](README.md) / [Japanese README](README_ja.md)
- [Release audit](docs/PUBLIC_RELEASE_AUDIT.md) / [third-party review](docs/THIRD_PARTY_NOTICES.md)
- [Setup boundaries](docs/setup.md) / [validation summary](docs/validation-summary.json)
- [Quest status](docs/quest-validation-status.md) / [evaluation](docs/evaluation.md)
- [Project summary](portfolio/project-summary.md) / [resume bullets](portfolio/resume-bullets.md)

Human review should confirm ownership/license, exact contribution wording, dependencies and disclosed limits. Do not restore withheld materials or publish without explicit authorization. No Quest fixes, Unified Locomotion, ExerciseInput, Android or new feature work was performed.

Staging payload: approximately 0.76 MiB of text/source files (logical file bytes, not filesystem allocation). No large asset directories copied. Exact final size is reported separately after validation.
