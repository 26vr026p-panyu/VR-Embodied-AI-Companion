# Public release audit — local candidate only

Date:2026-10-21. **READY FOR HUMAN REVIEW: YES. READY FOR AUTOMATIC PUBLICATION: NO.**

## Selection and original protection

Explicit whitelist copied70 C# and58 Python files plus selected schemas/config, script metadata and a reference package manifest. No whole-project copy. No Unity Library/Temp/Obj, installed Python, model/service environment, Codex history, large binary, raw audio or private dataset was copied. Original Scene, Backend settings and Git histories were not modified. No remote repository was created and no push was executed.

Source hashes were captured before packaging and rechecked afterwards. Private inventory stays outside this candidate. Public source-manifest uses relative module names/hashes, not original machine paths. Text copies normalize UTF-8/newlines; only documented staging path/config replacements differ semantically.

## Included / excluded

Included: application Agent Harness, realtime voice/turn architecture, provider adapters, body/Follow/Gaze/application UI/input source, preliminary Visual Gain, evaluation harness and sanitized counters, architecture and trilingual portfolio text.

Excluded: imported SDKs/source, game Avatar/PMX/textures, scenes/prefabs/Animator/clips, maps/music/fonts, Roxy weights/reference/training audio/transcripts, installed libraries, caches, keys, logs, conversations, raw physical mic WAV and unreviewed media. No substitute assets were generated.

TTS profile is an asset-free **staging example**, not the frozen production config. Other staging-only edits remove internal evaluation absolute paths. No original source was changed to make tests pass.

## Security checks

All staged files are UTF-8 text. Scan checks forbidden folders, model/media extensions, key/token/private-key patterns, literal credentials, private Windows paths and common private LAN literals. Application code may contain Authorization/API-key variable references; these are not credentials. .env.example contains a placeholder only. No private .env was read/copied. No secret value is printed in audit output.

Result: no findings in the defined scan. This is not exhaustive DLP or proof of ownership. Human review of all files and dependency terms remains required. Generated future logs/configs must not be committed.

## Licensing / media

No clear redistribution permission was established for character, voice/reference, map or original footage; all withheld. No automatic license grant applied to selected application code. Owner must confirm provenance and choose a source license. See THIRD_PARTY_NOTICES and media/README. No legal inference from research/noncommercial use.

## Validation and reproducibility

Python syntax and JSON checks ran. v0.1 contracts:67 PASS,1 evidence-dependent FAIL,11 SKIP (historical). P2:68 PASS,11 SKIP after adding authentic sanitized evidence; no assertion changes. No public-tree live service start, Unity compilation, clean dependency installation or Quest rerun. Mermaid diagrams received local structural checks; no complete Mermaid renderer/parser was available, so rendered layout is NOT VERIFIED. Relative document links are checked separately in validation-summary.json.

Classification: **Source-code Showcase + Architecture Reference**. Omitted assets/SDK wiring prevent a turnkey claim. Private prototype PCVR remains PARTIAL, with microphone reliability/barge-in/static outcomes/performance failures disclosed.

## Cross-project audit

No Tavern-specific contamination found in scoped application code/developer-document text. Complete independent prior chats/recent Git diff unavailable; overall **NOT FULLY AUDITABLE**. Current chat contains a cancelled wrong-chat ESC request, not a runtime requirement. No suspicion-based cleanup of history/persona/files was done.

## Human release gates

1. Confirm source authorship/AI-assisted contribution description and choose a license.
2. Review staged source/config and dependency/asset rights; do not restore withheld assets casually.
3. Confirm status wording and known issues; do not turn partial PCVR into PASS.
4. Approve any future media separately and perform the target fresh-environment build/test if claiming runnable code.
5. Explicitly authorize creation/publishing in a later task. This phase stops locally.

P2 latest [security review](SECURITY_REVIEW.md), [license options](../LICENSE_OPTIONS.md), [prerelease report](../PORTFOLIO_PRERELEASE_REPORT.md). Ownership/media remain human gates; READY TO PUBLISH=NO.

P3 source-specific credits/disclosure review supersedes earlier generic uncertainty: [P3 report](../PORTFOLIO_P3_RELEASE_REVIEW.md). Media/ownership gates remain unresolved; no asset permission is inferred from the new notice.

P4 current authorization:[verified private-reply metadata](authorization-evidence.json). Both contacted parties affirm described use; final Demo/ownership/license review remains separate. See [P4 final review](../PORTFOLIO_P4_FINAL_REVIEW.md). No raw screenshot, restricted model or unapproved footage is published.

## P5 current candidate

A newly recorded Demo and thumbnail are now explicitly whitelisted local-review media. All private raw capture/evidence and model assets remain excluded. Publication is blocked pending exact-artifact/source/license approval. [Current report](../PORTFOLIO_VIDEO_AND_RELEASE_REPORT.md).
