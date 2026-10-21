# Portfolio prerelease report — Phase4-P2

Date:2026-10-21. Local review only. No remote repository, release, commit reset or push.

## Outcome

| Item | Status |
|---|---|
| PORTFOLIO REVIEW & POLISH | PARTIAL — candidate improved; release gates remain |
| README EN | PASS — recruitment-oriented opening, code-linked highlights, current-system diagram |
| README JA | PASS — engineering, research, status and setup boundaries synchronized |
| ARCHITECTURE | PARTIAL — required diagram types covered, structural checks only; Render NOT VERIFIED |
| PUBLIC EVALUATION | PARTIAL —68 PASS,11 SKIP,exit0; not all-system acceptance |
| SECURITY | PASS within defined all-file scan; not exhaustive DLP |
| LICENSE | HUMAN DECISION REQUIRED |
| DEMO | EXCLUDED from staging; public interaction demo pending asset clearance |
| PUBLIC REPRODUCIBILITY | SOURCE SHOWCASE ONLY (public Python contract subset runnable in provisioned environment) |
| ORIGINAL UNITY | UNCHANGED, monitored files |
| ORIGINAL BACKEND | UNCHANGED, monitored files |
| READY FOR HUMAN REVIEW | YES |
| READY TO PUBLISH | NO |
| PUBLISHED | NO |

## Minimum changes made

- Retained correct detailed EN content; replaced only the introduction/navigation with value proposition, six real-module highlights, compact current architecture and Demo/status summary before the long validation table.
- Expanded JA README to cover Agent Harness, Tool Calling, realtime voice, embodiment, current/future research, limitations and dependencies. Visual Gain remains explicit, not an efficacy claim.
- Added Evaluation/Reliability current-path overview. Current system and Future/Proposed exercise diagrams are clearly distinguished. Existing local Mermaid package/CLI unavailable; no dependency installed and no diagram render claimed.
- Kept adverse Quest results: microphone reliability FAIL; barge-in3/5; static spatial PARTIAL; moving formation simulated; Combined p95 27.85ms and Conversation max~1014ms. Android NOT STARTED.
- Made the historical warm JA ASR result explicit:3204.1→2619.3ms,18.25%,CER5.70→4.82%,retained WAV/specific desktop environment. Not Quest E2E.
- Added license options, scoped source ownership review, media inventory and all-file security review.

## Evaluation: evidence repaired, not assertions

v0.1 result67 PASS/1 FAIL/11 SKIP remains documented. The failed reliability.policy_known_issue expected an actual measurement file. P2 extracted numeric-only authentic historical evidence:12 utterances,0 true underruns,0 runtime errors. Updated only the staging record's evidence pointer. No C#/Python runtime source, test assertion or expected result was changed.

The final full existing unit invocation returned68 PASS/11 SKIP,exit0. The11 skips are individually classified as missing private measurement reports:5 ASR,6 TTS. They do not themselves mean a connected Quest or a raw WAV is required by this invocation; those are underlying measurement dependencies. No clean install, Backend live start, public-tree Unity compilation/Play, real-provider or hardware rerun was performed.

See [evaluation](docs/evaluation.md), [case statuses](eval/evidence/packaging-contract-tests-2026-10-21.json) and [skip reasons](eval/evidence/public-skip-reasons.json).

## Media / ownership / security

Read-only inventory:13 individual presentation/screenshot candidates and79 derived frame groups within five known roots.0 approved. Rights/private-data review was not performed frame-by-frame; no unapproved file copied. No separate clearly labelled Quest recording found within that scope, not a whole-disk conclusion.

128 selected source files have no explicit license/upstream marker detected; this does not prove ownership. SDK interfaces/character integration/generated metadata and possible adapted snippets remain human provenance checks. No MIT grant/copyright owner invented. [LICENSE_OPTIONS](LICENSE_OPTIONS.md) compares choices using primary references.

Scoped Tavern gameplay/lore/instruction scan found no evidence in runtime/application content. Full prior chats/recent source Git diff remain NOT FULLY AUDITABLE. No history or generic Agent code was deleted.

## Changed and new files

Exact file lists and original/staged source integrity are recorded in [prerelease-validation.json](docs/prerelease-validation.json). Only staging documentation, review/evidence JSON, ignore rules and a known-issue evidence pointer changed. Original projects were read-only; no cleanup, feature fix or production configuration change.

## Remaining human decisions

1. Confirm ownership/adapted-code provenance and select a license/scope.
2. Approve media separately or keep it withheld; no new character/voice/recording feature required here.
3. Accept source-showcase limitations or authorize a separate clean-environment/build verification task.
4. Review disclosed Quest issues without mistaking this packaging review for acceptance.
5. Explicitly authorize a later publish task if release gates are resolved.

[Security summary](security_scan_summary.md) · [Media inventory](docs/MEDIA_REVIEW.md) · [Setup](docs/setup.md)

This phase stops locally. No Quest repairs, new Locomotion/ExerciseInput, runtime behavior changes or publication.

### Exact modified files

- .gitignore
- PORTFOLIO_PACKAGING_REPORT.md
- README.md
- README_ja.md
- backend/README.md
- docs/architecture.md
- docs/evaluation.md
- docs/limitations.md
- docs/performance.md
- docs/PUBLIC_RELEASE_AUDIT.md
- docs/quest-validation-status.md
- docs/reliability.md
- docs/research-vision.md
- docs/setup.md
- docs/source-manifest.json
- docs/THIRD_PARTY_NOTICES.md
- docs/validation-summary.json
- eval/README.md
- media/README.md
- unity/README.md
- eval/evidence/packaging-contract-tests-2026-10-21.json
- backend/eval/known_issues.json

### Exact new files

- LICENSE_OPTIONS.md
- PORTFOLIO_PRERELEASE_REPORT.md
- docs/media-review.json
- docs/MEDIA_REVIEW.md
- docs/PRERELEASE_AUDIT.md
- docs/SECURITY_REVIEW.md
- eval/evidence/public-skip-reasons.json
- backend/eval/evidence/roxy-known-issue-sanitized.json
- docs/prerelease-validation.json
- security_scan_summary.md

No pre-existing candidate files were removed. C#/Python source files unchanged:128/128. Only staging evidence/config-data pointer, documentation, manifests and ignore rules changed.
