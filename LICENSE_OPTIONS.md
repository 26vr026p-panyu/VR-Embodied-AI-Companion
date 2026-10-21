# Confirmed P6 decision

On2026-10-22 the owner explicitly selected MIT, Copyright (c)2026 Pan Yu. [LICENSE](LICENSE) · [Scope](docs/LICENSE_SCOPE.md). The options below are historical decision guidance, not the current unresolved status.

# License options — human decision required

No LICENSE file, copyright holder name or blanket grant is added by this task. This is a decision guide for the owner, not a completed authorization.

## Options for code you actually own

| Option | Intent | Obligations / decision |
|---|---|---|
| MIT | Simple permissive reuse, including commercial reuse | Preserve copyright/license notices. Owner must confirm authorship and accept broad reuse. |
| Apache-2.0 | Permissive reuse with explicit contributor patent terms | Preserve license/notices, mark modifications and retain applicable NOTICE material. More obligations than a short MIT notice. |
| No public license yet | Keep candidate private while deciding | Not an open-source license. Public GitHub view/fork terms do not amount to unrestricted reuse permission. |

MIT and Apache-2.0 do not automatically cover third-party contributions or assets. A choice applies only to material you are authorized to license. No automatic preference is selected here.

## Scope and ownership checklist

- 70 application C# and58 Python files were selected; a scan found no explicit license/copyright/upstream-source markers in those128 files. **Absence of a header is not proof of independent authorship or ownership.**
- Review any copied/adapted GitHub fragments against their actual source license. Preserve attribution, original notices and required source/NOTICE material; do not relicense incompatible material by adding a root LICENSE.
- First-party adapters can reference omitted SDKs without establishing permission to redistribute SDK fragments. Review Meta/Unity/UnityMMDTools and dependencies separately.
- Bone/BlendShape/character-related integration logic and generated metadata still require provenance review. AI-assisted generation does not establish that every line is novel or solely owned.
- Confirm actual authors/contributors and scope before inserting any copyright name/year. None is invented.

## Outside any proposed repository-code license

Excluded Avatar/PMX/textures/animations, map/music/fonts, Roxy/Whisper/model weights, reference/training audio, personal microphone data/conversations and unapproved videos/screenshots remain outside the candidate and any proposed code license. Dependency/API terms remain separate. A runtime code license does not authorize a voice identity or a reference waveform.

## Primary references checked on2026-10-21

- [MIT terms and summary](https://choosealicense.com/licenses/mit/)
- [Apache License2.0](https://www.apache.org/licenses/LICENSE-2.0)
- [GitHub guidance on no license](https://choosealicense.com/no-permission/)

The owner must decide the final scope/license and resolve unknown provenance. READY TO PUBLISH remains NO while ownership or media permissions remain unresolved.

## P3 recommendation and decision order

For application code the owner confirms they may license, MIT is a practical low-overhead option for a source-showcase portfolio. Apache-2.0 is an alternative when explicit contributor patent terms and its notice/modification requirements match the owner's intent. This is a conditional recommendation, **not a license selection or claim that all code is independently owned**.

Before adding a root LICENSE:(1)confirm actual owners/adapted-code provenance,(2)separate third-party code under its own terms,(3)choose the code-only license/scope,(4)preserve required notices,(5)insert only verified copyright attribution. No root LICENSE is created now.

MMD provider/modifier credits and the source's video-only/noncommercial/no-redistribution terms are separate from source licensing. Roxy publisher identity and GPT-SoVITS's MIT software terms do not establish performer/model/audio rights. Neither a repository MIT/Apache choice nor the [AI-voice notice](THIRD_PARTY_AND_AI_VOICE_NOTICE.md) grants permission for excluded assets or a demo. [Source-specific review](docs/ASSET_RIGHTS_REVIEW.md).

## P4 final source-only recommendation

For selected application code the owner confirms they may release, **MIT is the suggested default** for simple reuse in a portfolio source showcase. It permits broad reuse including commercial reuse of that code; do not choose it if the intended code restriction is noncommercial-only. Apache-2.0 is the alternative if its express contributor patent terms and notice/modification requirements are desired. Temporarily applying no open-source license retains the current decision boundary; public GitHub platform viewing/forking is not a general reuse grant.

This recommendation does not select a license, establish sole ownership or include third-party code/assets. The now-verified MMD-modifier and Roxy-publisher described-use consents do **not** license PMX/texture/weight/reference/training redistribution or automatically license unknown code fragments. A source-only repository can be reviewed separately from video publication.

Owner decisions still needed:(1)confirm selected code provenance/authority to publish,(2)choose MIT,Apache-2.0 or no license for now,(3)confirm license scope/contributor names and any required third-party notices. No root LICENSE is created until that decision.
