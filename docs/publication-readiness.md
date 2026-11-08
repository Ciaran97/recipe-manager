# Publication readiness and owner decisions

Recipe Manager is not yet declared ready for public distribution. Core functionality and local
acceptance are documented separately from release gates. No project LICENSE file is added until the
owner selects the license and contribution terms required by constitution 1.0.0.

## Decisions required

| Decision | Current state | Required before publication |
| --- | --- | --- |
| Project license | Pending owner selection | Record the chosen license and add its exact approved text |
| Contribution terms | Pending owner confirmation | Align contributor instructions with the selected project license; no invented CLA/DCO policy |
| Private security reporting route | Pending owner address/channel | Replace the pending route in SECURITY.md with a real approved contact |
| Public repository/release identity | Not supplied | Set real URLs, issue templates and release/version policy without inventing an organization |
| Signing/distribution | Owner team verified, automatic development signing/profiles and shared App Group verified; designated physical iPhone installation/launch and planner/recipe/shopping workflows pass | Complete physical workflow/accessibility/sharing acceptance and select a distribution channel |

## Reviewable preparation

CONTRIBUTING.md defines specification-driven changes, small tested increments and mandatory web/iOS
parity. SECURITY.md documents boundaries, safe reporting and operator recovery. THIRD_PARTY_NOTICES.md
identifies locked runtime/tool dependencies and full-notice obligations. Secrets and private fixture
artifacts are ignored; production images use explicit source copies and preserve upstream notices.
No source repository is initialized or public artifact published by these local changes.

## Gates to retain

- Complete staged cross-client critical journey and accessibility evidence, including physical
  Safari/ChatGPT, VoiceOver, minimum/current iOS and real signing/installation.
- Run the reference 2-vCPU/4-GiB workload and required synchronization sample distributions.
- Run all required Linux/macOS workflows and retain signed native/Share Extension acceptance.
- Recheck dependency advisories and exact upstream/native codec notices against locked artifacts.
- Confirm backup/restore, protected keys/files, release checklist and supported-version policy.
- Obtain owner license/contact decisions and review asset/source provenance before distribution.

Do not infer legal approval, physical-device acceptance or benchmark results from a green local unit
suite. The existing tasks and validation records remain the source of completed-versus-open status.
