# Third-party notices / release exclusions

No blanket external license is assumed. Dependency names are attribution/install references, not redistribution grants. Human ownership review is required for application code before publication.

| Item | Candidate handling | Remaining review |
|---|---|---|
| Game character PMX/MMD, texture, hair, animation/controller assets | Excluded | Exact page prohibits redistribution/commercial use and restricts non-video use; see P3 source review |
| Roxy3 weights, REFEX01, training/reference waveform/transcript | Excluded | Voice/model/reference and synthetic-output publication rights |
| Demo MP4 and thumbnail | Owner-reviewed rendered Demo only; underlying assets excluded | Exact-artifact owner approval; see media/demo-review.md |
| WindsongMeadow/EasyRoads3D/SimpleNaturePack/fonts | Excluded | Each asset's actual terms |
| Unity/URP/InputSystem/TMPro/Newtonsoft/OpenXR | Package references only | Unity/package terms |
| MetaXR/Oculus SDK | No imported source/binaries | Meta distribution conditions |
| UnityMMDTools | Package reference only | Runtime license separate from model/asset rights |
| faster-whisper/CTranslate2/Whisper | Adapter/imports only, no weights or installed library | Code and weights have separate upstream terms |
| GPT-SoVITS | Runtime not vendored | Runtime license does not license a particular voice/reference |
| FastAPI/OpenAI SDK/httpx/Edge TTS and dependencies | Observed versions only | Review upstream terms/provider use separately |

Application adapters can reference omitted SDK types. No generic substitute asset was created. P6 owner explicitly selected MIT for authorized original source; third-party code/assets are not relicensed. See [license scope](LICENSE_SCOPE.md).

## P2 ownership review

[License options](../LICENSE_OPTIONS.md) remains a human decision. Scanned128 selected source files for copyright/license/upstream-source markers; no explicit markers found. This cannot establish ownership or rule out adapted code. Generated script metadata, character integration logic and external SDK interfaces require review. No third-party license has been inferred from absence of a header.

## P3 verified character credits

- Original work/character:Genshin Impact / 原神 — Citlali / 茜特菈莉.
- [Exact MMD source](https://www.aplaybox.com/details/model/gF6QUvbECNUA):【原神】茜特菈莉「星与烟帷的夜语」.
- **Model provided by miHoYo; model modified by 观海** (public Bilibili contact name:观海子), as stated both on the page and in the local model Readme.
- Local model internal name:Citlali_CostumeSummer. PMX2.0 is format, not a verified release revision. A separate converter/rigger and exact archive revision were not verified.
- Terms reference: page description/distribution rules and package readme【一定要看】.txt. No assets or full Readme are copied into this candidate.
- The page prohibits redistribution/commercial use, permits limited modifications, requires a full borrowed-material list and restricts use outside video production. **P4:contacted modifier’s affirmative private reply confirms the described personal noncommercial Unity/VR research and GitHub portfolio-Demo use.** Original conditions remain valid; this is not a miHoYo legal authorization.
- Character rights remain with respective rights holders. No PMX, textures, model parts or source character assets are redistributed here.

## P3 verified voice / software credits

- Community voice model:Roxy3 GPT-SoVITS; [original publication](https://www.bilibili.com/video/BV1HfF6zNEK8/).
- Verified publisher/uploader:**独孤欲雪** ([profile](https://space.bilibili.com/383710712)). Not the original performer, collection creator or software author by inference.
- Visible description records the2026-10-03 Roxy3.0 update. No express public reuse grant found in that description, but P4 verifies the publisher’s affirmative DM reply for the described research/GitHub portfolio use. Original-video reposting is still restricted.
- Software separately:[GPT-SoVITS — RVC-Boss project and contributors](https://github.com/RVC-Boss/GPT-SoVITS); [observed MIT license](https://raw.githubusercontent.com/RVC-Boss/GPT-SoVITS/main/LICENSE), copyright2024 RVC-Boss. This software credit/license is not a grant for Roxy weights, reference/source audio or performer/animation rights. Runtime is not vendored.
- Voices are AI-generated, not new recordings by the original performers; no participation/approval/endorsement is claimed. AI lines are not the original performers' or rights holders' views.
- The Roxy-model voice is paired with a separate Citlali Avatar; it is not presented as official Citlali casting/voice production. No actor name is guessed.
- No weights, references, training datasets, source recordings or publisher videos are distributed.

## Non-affiliation and conditional demo path

This independent project claims no official collaboration, sponsorship or endorsement from miHoYo, original animation producers, performers or other rights holders. [Trilingual disclosure](../THIRD_PARTY_AND_AI_VOICE_NOTICE.md) does not replace source permissions.

Model-file redistribution, recorded technical demo publication and AI-generated-audio publication require separate decisions. The demo is not permanently ruled out: when exact conditions are documented and other assets/privacy are reviewed, it may be submitted for final owner approval. P4 historical status was **REVIEW REQUIRED**, no media copied then. P5 now includes a new local review MP4 and thumbnail, not a public release. [Rights review](ASSET_RIGHTS_REVIEW.md) · [Publication checklist](DEMO_PUBLICATION_CHECKLIST.md).

## P4 authorization update

**MODEL_MODIFIER_PERMISSION: CONFIRMED FOR DESCRIBED USE.** 观海子’s private reply agrees to the described personal noncommercial Unity/VR AI research and GitHub employment-demo request, including credits and no model/texture upload.

**ROXY3_PUBLISHER_PERMISSION: CONFIRMED FOR DESCRIBED USE.** 独孤欲雪’s private reply affirmatively answers use of the voice in personal research AI Agent/GitHub portfolio with attribution.

Both screenshots were locally verified and remain private. [Sanitized evidence](authorization-evidence.json) records scope/date/replies without publishing the chats. These are the contacted parties’ consents, not an authorization from miHoYo’s legal department or the original voice performer. Upstream voice rights remain **NOT INDEPENDENTLY VERIFIED**, without negating the confirmed publisher consent. No commercial-sales, redistribution or unrestricted sublicensing is inferred.

Public Demo:**PENDING FINAL ASSET REVIEW**. Source/model rules, credits, other scene/font/music/privacy checks and final owner approval remain separate. No model, texture, weight, reference or training audio is redistributed.

## P5 rendered-media update

The selected Meadow prefab resolves41 dependencies entirely within AnimeStraightTrack; no dependency on installed SimpleNaturePack/EasyRoads was found in that closure. No BGM or new imported motion was added. Captions/HUD are rasterized; font binaries remain excluded. [New Demo review](../media/demo-review.md) records font sources, exact scope and remaining owner approval. Private author evidence and upstream-rights distinctions remain unchanged.

## P6 owner approval

On2026-10-22 the owner confirmed the exact Demo, source provenance/authority and described-use media scope and selected MIT, Copyright(c)2026 Pan Yu, only for original source they may license. [Scope](LICENSE_SCOPE.md). Private consent originals stay private; the existing upstream-rights/performer/official-endorsement distinctions are retained. No underlying PMX, texture, weight, reference, font or training data is redistributed.
