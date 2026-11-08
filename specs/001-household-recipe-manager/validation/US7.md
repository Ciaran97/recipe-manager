# Self-hosting and recovery — implementation in progress

T124–T128 are implemented and reference-host operational checks pass. Physical-device and accessibility release gates
remain open; this record distinguishes those gates from the verified container drills.

## Automated operational coverage

All 348 backend tests pass: 140 unit, five architecture and 203 integration cases, without skips.
Ten operational cases cover prior-migration upgrades retaining an existing recipe, no model drift,
repeatable explicit migrations, separate liveness/readiness, safe missing configuration, public SPA
assets, protected namespace errors, optional-body DELETE routing, trusted proxy/host reconstruction,
real pg_dump/pg_restore and encrypted key-ring recovery. The restore compares original snapshots,
actual cooking quantities, partially consumed/released reservations, manual/purchased shopping data,
valid PNG files, committed receipts and expired identity tombstones. Its source database remains
unchanged and the restored database is a separate disposable target.

Adding a broad SPA endpoint first caused DELETE optional-body routes to disappear, then an explicit
method-constrained fallback changed absent registration from 404 to 405. Focused regressions caught
both. Final SPA handling is middleware for unmatched GET/HEAD browser pages, with protected namespaces
excluded; the complete regression and deployed route checks now pass.

## Built and exercised image

The pinned multi-stage image builds Angular and ASP.NET and packages one runtime. Both linux/arm64
and linux/amd64 builds pass. Runtime UID is verified as 1654. The root filesystem is read-only; the
app uses persistent images/keys and private tmpfs for its maintenance socket. PostgreSQL has no host
port. The initializer refuses overwrite/active-network reuse, generates secrets without printing
values and uses file mounts readable by the non-root app through a private host directory.

The production Compose drill uses fresh owned projects `rm-validation-a91398bb` and
`rm-validation-0336230e`, loopback host ports and the optional Caddy TLS overlay. It does not modify
the existing development project or its volumes. The application/migrator/proxy ordering completes
successfully and readiness becomes healthy. HTTPS is verified normally using the proxy's exported
public CA; no TLS bypass is used.

Through deployed HTTPS, the fixture creates an administrator/household and manual recipes, schedules
four prepared/two eaten Monday with two explicit Tuesday leftovers and another Wednesday recipe,
and generates exactly 500 g rice. Known rice does not hide an additional unresolved rice amount;
salt to taste remains visible. Purchased state and a manual bread item are retained. AI stays
disabled and no paid/provider call is made.

Linux Skia image upload/re-encoding/private retrieval passes. An authenticated SignalR household
notification travels through the TLS proxy. A saved encrypted login session, protected disabled-AI
provider configuration, image hash, recipe/leftover/list data and real-time updates survive app/proxy
restart. The corrected image is applied with writers stopped, its one-shot migrator reruns safely,
and deployed absent-registration 404/optional-body DELETE checks pass.

## Paused-write restore drill

The fixture stops proxy/app writes, dumps PostgreSQL and copies images, key ring, protected secrets,
TLS data and TLS configuration. It restores into new database/file volumes with correct ownership,
then starts the same application image. The pre-backup session is accepted before a fresh login;
encrypted provider settings decrypt, the same image bytes return, planned leftovers and manual/
purchased shopping edits remain, and TLS SignalR updates work. Source and target projects are
stopped afterwards, with all original/source and target volumes preserved for review. No existing
volume is deleted.

The executable fixture is `scripts/test-deployment.mjs` plus `scripts/test-deployment-restore.sh`.
The restore guard permits only owned `rm-validation-*` projects. Private artifacts remain at the
paths emitted by that run; they include synthetic credentials/keys and must not be published.
Operator procedures are in docs/self-hosting.md, docs/backup-restore.md and docs/ios-installation.md.

## Gates still open

- Reference-host performance distributions remain open. Clean two-vCPU/four-GiB Linux operational
  validation now passes; operational success is not performance evidence.
- Physical Safari/ChatGPT and VoiceOver acceptance. Signed installation, trusted LAN access and
  core planner/recipe/shopping journeys now pass; see validation/native-parity.md.
- Complete 320px/desktop and native accessibility acceptance, including minimum/current OS.
- Final staged cross-client critical journey, workload distributions and publication checks.

The database-only development manifest is retained as compose.dev.yaml with its original project
identity. Development commands explicitly use `-p recipe-manager-dev` so a production `.env` cannot
redirect them into production volumes.

## Accessibility and release automation increment (2026-11-03)

The 18 web screen/viewport combinations now pass the automated WCAG A/AA and horizontal-reflow
checks in validation/accessibility.md, including a long unbroken recipe title that first reproduced
320px overflow. This does not close physical/native accessibility acceptance.

The signed native build includes ThirdPartyNotices.txt with the pinned Swift runtime dependency
licenses/notices. A fresh arm64 `recipe-manager:notice-validation` image builds with the responsive
fix and browser notice copy; isolated container checks verify non-empty browser and .NET runtime
license files. Existing deployment projects/volumes are not replaced by that validation image.

scripts/validate-release.sh provides separate backend/web, native-unit and signed-native-acceptance
entry points. .github/workflows/release-validation.yml dispatches acceptance on an explicitly trusted
self-hosted macOS/Docker runner. Workflow YAML parses and shell syntax passes; combined entry points
and hosted jobs have not yet been executed. Standard native CI now explicitly selects unit/handoff
tests, preventing skipped UI fixture tests from being mistaken for UI acceptance.

The owner-facing release checklist in docs/release-checklist.md retains staged cross-client,
reference-host performance, physical-device, license/contact and dependency-review gates. T134 and
T136 remain open until their full evidence is complete.

## Physical development installation — 2026-11-04

The owner-supplied team matches the budget application's existing signing configuration. Owner
identifiers are held in ignored `ios/Local.xcconfig`; public project defaults remain reusable.
Automatic development signing succeeds with the owner's new locally created certificate. Both
embedded profiles cover the designated iPhone; app/extension signatures, team identifiers and their
shared App Group were checked. Deep/strict code-signature verification passes.

A real iPhone running iOS 27.0.1 installs the application and embedded Share Extension and passes
LaunchTests (11.265 s). Its captured initial server-selection screen was visually reviewed. This
establishes signed physical installation/launch, not complete device workflow or accessibility
acceptance. No existing certificate was revoked by the agent.

At the owner's request, a separate `rm-device-*` LAN Compose deployment now has fresh PostgreSQL,
images, encryption keys, generated secrets and a uniquely named internal CA. TLS is verified with
that CA. Its HTTP bootstrap serves only the public root/profile; application, API and secret paths
return 404 over HTTP. Database ports stay private and the direct application port stays loopback.
Existing deployment volumes were not reused or deleted. The phone owner installed/trusted its CA.
The first device workflow run could not resolve the Mac's `.local` hostname; the same isolated stack
was changed to its LAN IP with a matching certificate signed by the existing trusted CA. Retesting
is in progress without disabling certificate validation. See docs/lan-device-testing.md.

## Physical TLS proxy validator regression — 2026-11-04

Physical shopping and leftover updates exposed Caddy rewriting strong API ETags with an encoding
suffix (for example, `:1` became `:1-gzip`). The backend correctly rejected those altered opaque
validators with 412. Simulator fixtures did not use this proxy and therefore did not expose it.

`deploy/Caddyfile` and the isolated LAN generator now exclude `/api` and `/api/*` from response
compression; web asset compression remains enabled. `scripts/test-deployment.mjs` now advertises
browser/native compression capabilities on every API request and rejects encoded API responses,
so deployment checks exercise this transport constraint. No client strips or reconstructs ETags,
and backend concurrency validation is unchanged.

The owned LAN Caddy configuration validates. A normally trusted HTTPS regression verified equal
validators for identity and compression-capable reads, then successfully purchased an item with
the returned validator. Only its newly created disposable regression list was removed afterward.
Physical core workflow reruns are in progress; this transport regression is not itself full device
acceptance. The trade-off is uncompressed API JSON in v1 to preserve simple opaque validators.

## Clean reference Linux VM — 2026-11-04

T127 passes on a newly created, separate Lima 2.2.1 VZ VM with no host filesystem mounts or
changes to the existing Docker context. It runs Ubuntu 26.04.1 LTS ARM64 with exactly two CPUs,
4 GiB allocated RAM (4,086,222,848 bytes guest-visible), no swap and an SSD-backed virtual disk.
Docker 29.9.3 / Compose 5.7.0 run inside that VM. The existing Mac/Docker deployment is separate.
The official Lima archive and Ubuntu cloud image were checksum verified; all VM/project state is
private and no original/source/restore volume was deleted.

`scripts/test-reference-deployment.sh` provides the guarded Linux allocation check and clean
setup/restart/upgrade/restore sequence. The VM received the previously validated image and current
unknown-unit/import-quality image. Normal TLS, setup, no-AI manual recipes, explicit leftovers,
exact consolidation/visible unresolved requirements, purchased/manual edits, PNG re-encoding,
protected provider settings, session continuity and authenticated SignalR through the TLS proxy
pass. A real old-to-new application image upgrade retains data and sessions. Paused-write pg_dump
plus images/keys/TLS/secrets restore into a new project passes the same checks. The source and
restored installations are stopped with all volumes preserved.

The first SignalR probe exposed missing copied Node runtime dependencies; the test-tool package
closure was repaired and the same installation resumed without resetting its data. The completed
reference-host drill passes; performance distributions are a separate T133 gate and are not inferred
from operational success. The copied Node test runtime is pinned to 26.10.0.

## T130 operational and signed-installation completion

T130 is complete on its stated deployment/installation scope: clean two-vCPU/four-GiB Linux installation, restart, real image upgrade and independent paused-write restore pass; setup, restored access/session continuity and no-paid-service core operation are exercised. The real iPhone previously accepted the automatically signed app and Share Extension with the shared App Group, and physical planner, shopping and recipe-editing journeys passed. Owner-verified Safari capture reached editable review and saved a recipe. The latest generic iOS app/extension build also signs successfully; it has not been installed while the owner is asleep and the phone is locked.

Task dependencies now distinguish operational/signed-installation evidence from T129 accessibility and T101 physical text-share/VoiceOver acceptance. Those requirements remain mandatory and open, as do minimum-OS, expanded parity and full performance release gates. This dependency clarification does not reduce product scope or assert full device acceptance. The reference source installation was subsequently restarted for the separate performance workload; the restored installation and all volumes remain preserved.

## Home-server documentation — 2026-11-05

README now provides separate LAN HTTPS, existing Caddy/nginx reverse-proxy and Cloudflare Tunnel installation paths. The tunnel guide retains normal origin certificate validation through the existing Caddy hop, documents exact host/header/CA settings, private tunnel credentials, a dedicated Linux service, and API/hub cache/compression exclusions. It does not claim a live Cloudflare zone or tunnel has been configured.

The README's Caddy configuration, nginx configuration and cloudflared ingress/service flags pass offline validation in disposable network-disabled containers. The validator uses only synthetic certificates/credentials and records exact image digests in the tunnel guide. Base, bundled-TLS and loopback tunnel Compose variants also render successfully with synthetic configuration; PostgreSQL remains unpublished and the tunnel origin's published ports remain on loopback. Local documentation links resolve.
