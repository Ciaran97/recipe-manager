# Implementation Plan: Household Recipe Manager MVP

**Branch**: `001-household-recipe-manager` (workflow identifier; no Git branch exists)
| **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-household-recipe-manager/spec.md`

## Summary

Deliver one ASP.NET Core application serving a same-origin Angular SPA, REST API, and
SignalR household notifications, backed by PostgreSQL, plus a first-class SwiftUI iOS app
and Share Extension in the same v1 release. Organise backend modules around
households, ingredients, recipes, meal planning, leftovers, shopping, and imports. Use explicit
in-process application contracts and a single transactional persistence boundary rather than
microservices or distributed messaging. Store original ingredient quantities alongside exact
calculation representations. Snapshot scheduled recipes and shopping baselines, and require
explicit conflict resolution for allocations and reconciliation.

This document records the approved design. Implementation progress and verification are tracked
in tasks.md and validation/; source scaffolding does not establish completed domain workflows.

## Technical Context

**Language/Version**: C# / .NET 10; Swift 6 language mode (strict concurrency), iOS 18+; Angular 22 and its compatible TypeScript/Node versions
verified against the official compatibility table at dependency bootstrap; strict TypeScript.

**Primary Dependencies**: ASP.NET Core 10 Identity, cookie authentication, OpenAPI generation,
SignalR; EF Core 10 and Npgsql EF provider 10; Angular Router, HttpClient, Reactive Forms,
Angular CDK accessibility; decimal storage/portion arithmetic, a small exact ingredient calculator, and string numeric DTOs.
Pin exact supported patches in lockfiles and central package management during implementation.

**Storage**: PostgreSQL 18; one database, one EF DbContext, module-owned tables; protected local
image files and persisted ASP.NET Data Protection keys in named volumes.

**Testing**: Swift Testing for client unit tests, XCTest/XCUITest for native UI and share handoff; xUnit, ASP.NET WebApplicationFactory, Testcontainers PostgreSQL, property-based
calculation tests, Angular component tests through the CLI-supported runner, Playwright and axe.

**Target Platform**: Linux amd64/arm64 Docker host; current and previous stable Chrome, Edge,
Firefox and Safari including mobile Safari/Chrome; native iPhone app targeting iOS 18+ with
adaptive iPad presentation. No offline-first editing. Pin a stable Xcode supporting the deployment
target and current distribution SDK requirements during bootstrap; macOS CI is required.

**Project Type**: Self-hosted web application, modular monolith.

**Performance Goals**: 500 recipes/two active users: p95 usable search/calendar and shared shopping
updates within two seconds; reference host 2 vCPU, 4 GiB RAM, SSD, local network RTT under 50 ms.
Measure warmed application over 100 representative requests/updates, excluding external imports.

**Constraints**: Household isolation, exact deterministic calculations, optional external AI,
no mandatory paid services, explicit leftover scheduling, accessible mobile-first UI.

**Scale/Scope**: Initial household of two; many isolated households may coexist in the database,
but v1 registration only bootstraps one household and invited users join it. Additional household
provisioning and multiple memberships per user are not v1 interfaces. All nine spec stories apply; neither client can be deferred.

## Constitution Check

*GATE: Evaluated before research and after Phase 1 design.*

| Principle | Design evidence | Pre / Post |
| --- | --- | --- |
| I Modular monolith | One host/database; owned module namespaces and application contracts | Pass / Pass |
| II Strong types | Typed commands, quantity/portion values, request validation, Problem Details | Pass / Pass |
| III Isolation/auth | Identity cookies, membership checks, composite tenant FKs, hub revocation | Pass / Pass |
| IV Determinism | Exact fractions, explicit AU/US factors, immutable calculation inputs | Pass / Pass |
| V Verification/migrations | Real PostgreSQL race tests; checked-in EF migrations and recovery review | Pass / Pass |
| Native parity | FR-045–052; full SwiftUI workflows plus Share Extension, no domain duplication | Pass / Pass |
| VI Optional AI | Provider adapter and validated review drafts; disabled by default | Pass / Pass |
| VII Accessibility | Keyboard row editing/calendar actions, CDK, axe and manual checks | Pass / Pass |
| VIII Self-host/publication | Compose, external secrets, persistence/restore, license release gate | Pass / Pass |
| IX Spec-driven scope | FR traceability below; no speculative features or source generation here | Pass / Pass |

No unresolved technical clarifications or constitutional waivers. Owner license selection remains
a public-distribution gate, not a blocker to planning. No constitution/template changes required.

## Project Structure

### Documentation (this feature)

```text
specs/001-household-recipe-manager/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── rest-api.md
│   └── realtime-and-imports.md
└── checklists/requirements.md
```

`tasks.md` is a subsequent `$speckit-tasks` output, not created in this phase.

### Source Code (repository root)

Proposed paths; these directories/files are not created by planning:

```text
RecipeManager.slnx
src/
├── RecipeManager.Api/             # Composition, HTTP endpoints, Identity/hub, SPA static files
├── RecipeManager.Core/
│   ├── Shared/                    # IDs, exact Quantity, clocks; no generic business framework
│   └── Modules/
│       ├── Households/
│       ├── Ingredients/
│       ├── Recipes/
│       ├── MealPlanning/
│       ├── Leftovers/
│       ├── Shopping/
│       └── Imports/               # Each module: domain rules and application use cases/contracts
├── RecipeManager.Infrastructure/  # DbContext, module mappings, migrations, files, HTTP adapters
└── RecipeManager.Web/             # Angular workspace
    └── src/app/
        ├── core/                 # Session, HTTP, generated contracts, connection lifecycle
        ├── shared/               # Small accessible controls and formatters
        └── features/             # setup, auth, recipes, planner, shopping, settings
 tests/
├── RecipeManager.UnitTests/
├── RecipeManager.IntegrationTests/
├── RecipeManager.ArchitectureTests/
└── e2e/
 deploy/                          # Compose, Dockerfile, operational documentation
```

Native paths added to the proposed repository (not created by planning):

```text
ios/
├── RecipeManager.xcodeproj/       # App + Share Extension targets; checked-in shared schemes
├── RecipeManager/
│   ├── App/                      # Composition, environment/server selection, scene lifecycle
│   ├── Core/                     # API, session actor, Keychain, sync coordinator
│   └── Features/                 # Setup/auth, recipes/import review, planner, shopping, settings
├── RecipeManagerShareExtension/  # Extension-safe input capture and pending-handoff UI
├── Packages/
│   ├── RecipeManagerAPI/         # OpenAPI-generated DTOs/client adapter, URLSession transport
│   └── ShareHandoff/             # Extension-safe envelope/storage only, no business math
├── RecipeManagerTests/           # Swift state/format/presentation tests
├── RecipeManagerUITests/         # XCUITest critical workflows and share-sheet host fixture
└── ShareHandoffTests/            # URL/text, cancellation, corruption, account-bound resumption
```

**Structure Decision**: Three backend projects plus Angular and native iOS give compile-time separation of pure
business logic from infrastructure without a project per layer per module. Core modules expose
narrow contracts; architecture checks prohibit references to another module's internals. One
DbContext composes mappings but application code accesses only owned data or explicit module
query/command contracts. No generic repository, mediator framework, event sourcing, broker, Redis,
Kubernetes, CQRS infrastructure, or separate import worker service.

### Backend and calculation design

Minimal API endpoint groups translate DTOs into application use cases. Infrastructure implements
persistence and provider interfaces; business rules stay in Core. Authenticate and resolve a
current membership on every request, never from a trusted client household ID. Cross-module
operations compose within the same scoped transaction. Publish shopping invalidations only after
commit. Calculation and balance policies are detailed in [data-model.md](data-model.md).

Pure calculation pipeline: validate → resolve stored ingredient identity → scale original exact
quantity → convert compatible unit to g/ml/item → group by identity/dimension → sum exactly →
format display. Retain original input text and unit. No fuzzy alias auto-merges, weight/volume
inference, or frontend floating-point totals. Recipe serving previews call the server calculator.

### Frontend architecture

Standalone components, lazy feature routes, typed Reactive Forms, route-local signal stores and
HttpClient services; no global state framework. Generated OpenAPI client/types are checked for
contract drift in CI. Quantities are strings and never parsed through JavaScript Number for
business arithmetic. Forms use separate quantity/unit/name/preparation inputs, autocomplete with
explicit ingredient selection/creation, keyboard add/duplicate/reorder/group actions, and focus
restoration. Mobile calendar uses a day/slot list with explicit move controls; desktop weekly grid
shares the same actions. Drag-and-drop is optional enhancement, never required.

A session store owns membership/role and route guards for usability only; server policies remain
authoritative. Setup is separate from invite acceptance. Invitation tokens are removed from browser
history after exchange and never sent as referrers. Recipe import review is a typed editable draft
with uncertainty and measurement-standard confirmation. Planner shows planned versus actual badges,
source-linked balances, and explicit conflict resolution. Shopping store refreshes committed state
on hub invalidation, preserves dirty local edits, and presents revision-checked baseline/manual-change choices.
Use semantic HTML, CDK focus utilities, visible labels/errors, live-region shopping feedback, and
320 px through desktop layouts. Settings isolate personal preferences from admin controls.

### Native architecture and feature parity

SwiftUI TabView (Recipes, Meal Planner, Shopping Lists, Settings) and NavigationStack with native
Forms, sheets, pickers and date selection; Observation-based feature models on MainActor. Pure
presentation/state reducers use Swift Testing. Keep UIKit limited to Share Extension host/input
bridging where required; no WKWebView wrapper as the native app. Support Dynamic Type, VoiceOver,
semantic colors, reduced motion and accessible add/reorder actions. iPhone is the primary native
layout; iPad uses adaptive native layout, not a separate deferred app.

An API actor uses URLSession async/await, Codable/generated OpenAPI types, structured cancellation,
request deadlines and typed Problem Details. Quantities stay strings/exact server representations;
Swift only formats authoritative display values. Serving previews, leftover availability and
shopping calculations always call the backend. Multipart image upload and paged/filter queries
use the same operations as Angular. Use ETags and revision-checked commands with no blind retries
of non-idempotent writes. Session actor serializes renewal and credential updates; no domain writes
queued offline. Pin Apple Swift OpenAPI Generator/runtime/URLSession transport compatible versions
and validate generated clients alongside Angular in CI; isolate generator details behind a small
adapter so changes do not leak into feature views.

Planner implementation uses account/server-scoped request adapters on both clients. Calendar dates
are household civil dates; date navigation avoids device-timezone and daylight-saving shifts.
Native `MealPlannerView`, `MealEditorView`, `LeftoversView` and `CookingSourceView` use native forms,
date/slot selection and sheets; Angular uses its weekly grid and mobile day list. Portion entry
preserves decimal strings, with a 0.5 presentation stepper. Neither client recomputes availability.
Source reads carry planned/actual quantities and balances under one revision; commands retain opaque
ETags. Conflicted source edits submit explicit dependent resolutions atomically. Refresh shows both
complete saved/latest recipe snapshots and requires an explicit apply action. Unsaved input is kept
on validation/stale-write errors; refreshing concurrency state requires review before applying it.

Reuse ASP.NET Identity's cookie ticket on native: ephemeral URLSession with a controlled in-memory
cookie jar, persisted session cookie only in app-private Keychain (WhenUnlockedThisDeviceOnly),
not UserDefaults/files/iCloud or the extension. Bootstrap CSRF and supply header on unsafe actions
just like web. Capture Set-Cookie renewal/deletion atomically through the session actor. Do not
save account passwords. On launch restore cookie, obtain fresh CSRF and validate /auth/me; on
401/removal clear credentials and sensitive view state, preserving only explicitly pending review
input. Password reset/change invalidates old sessions; explicit logout clears local session and
uses SessionRevocationService to rotate security stamp and terminate active sockets (all-client
sign-out). Password changes/resets, deactivation and removal use that same service. No parallel identity store, JWT refresh
service, OAuth provider or social login is required for this first-party connected client.

Server base URL is user-configurable HTTPS, normalized and partitioned by origin; never follow
cross-origin authenticated redirects or send a cookie to another origin. Changing server/account
clears current credentials/state and asks before binding pending share inputs. TLS trust errors are
shown, never bypassed. Local development permits only explicitly scoped dev trust configuration.
Invitation links can be pasted/opened in-app with server confirmation; universal links are optional
operator configuration because arbitrary self-hosted domains cannot be pre-associated universally.
Setup wizard and Administrator settings are available on native, including AI configuration.

For iOS equivalent real-time synchronization, use a lightweight conditional REST sync poll every
one second while foregrounded (one shared task, not one per screen), ETag/304 when unchanged.
Response carries household content/domain revisions only. Fetch changed visible resources, preserve
unsaved edits and surface revision conflicts. Cancel on background/logout; on active scene/network
recovery immediately validate session and refresh before restarting. Back off failures with jitter,
then resume normal cadence; no APNs/background delivery promise. Angular keeps SignalR, now with
household invalidations as well as shopping events, using the same sync revision fallback. This
meets foreground sharing without a third-party Swift SignalR dependency or custom WebSocket stack;
two-user scale makes the tiny conditional request acceptable. Test the two-second cross-client
criterion on the reference host; measure before adding a different transport.

Share Extension accepts UTType.url and UTType.plainText via NSItemProvider; user confirms URL/text
selection if mixed. It only validates bounds and atomically writes a UUID handoff envelope to an
App Group container using coordinated file access. Envelope records original content/type/time,
selected server/account binding if known, and status; never session credentials. Complete the
extension request only after durable handoff. Show `Saved for import — open Recipe Manager to
review`; do not rely on unsupported launching of the containing app from the Share Extension.
On next app foreground, show pending imports, confirm destination/login, submit the normal URL/text
import, persist HandoffId, draftId and stable retry key, resolve the durable outcome, and resume review. Review, correction and commit
occur in the main app; cancelled/rejected input never creates a recipe. Discard/expiry after seven
days explicitly clears handoff files; warn users of expiry in pending-import UI. Use file protection
and no raw-content logs. A recoverable handoff queue is not an offline recipe database. Extension
is enabled in v1 and signed/packaged with the app; no AI SDK, credentials, extraction or long-running
networking in its limited process.

### Testing strategy and traceability

| Coverage | Requirements | Verification |
| --- | --- | --- |
| Recipes/editor/search | FR-001–012 | Pure validation/scaling, CRUD integration, mobile keyboard E2E |
| Units/identity/import review | FR-013–018,044 | Exact AU/US fixtures, alias boundaries, fake provider/site fixtures |
| Planner/leftovers | FR-019–026,043 | Balance/state/property tests and PostgreSQL concurrent allocations |
| Shopping | FR-027–033 | Consolidation, source snapshots, three-way merge and live E2E |
| Security/roles | FR-034–035,040–042 | Two-household API/hub matrix, bootstrap/invite/removal races |
| Native parity/share | FR-045–052 | Swift unit, XCUITest, share host fixtures and bidirectional API acceptance |
| UX/operations | FR-036–039 | axe/manual keyboard/mobile, migration and clean restore drills |

Unit tests include fractional parsing, no rounding drift, scale composition, order-independent
sums, distinct aliases, incompatible dimensions, and planned/actual balance invariants. Integration
uses PostgreSQL, not SQLite/InMemory substitutes: FK isolation, transaction rollback, stale ETags,
source-row locking, invitation single use, last-admin protection, atomic setup, stale reconciliation,
member removal and active hub access. Contract tests validate required fields/status/errors,
decimal string serialization, OpenAPI compatibility and generated client reproducibility.

Imports use local deterministic HTTP fixtures and a fake AI provider in CI; test redirects, private
IP/DNS rebinding, time/size limits, malicious markup, schema invalidity and unknown measures.
No paid provider or live website is required for passing tests. Playwright exercises the full
critical scenario in two sessions, offline/reconnect recovery, reconciliation and actual corrections.
Run migration creation/pending-model checks and upgrade/restore tests with fixture data. CI runs
format/analyzers/build, unit/integration/architecture/client tests, OpenAPI drift, E2E/accessibility,
and dependency/secret/license checks. Mandatory macOS CI builds both native targets, runs Swift
unit tests, API-backed XCUITest, and simulator share-host tests. Device tests cover real Safari and
ChatGPT URL/text sharing, extension activation, Keychain persistence/signing and app foreground
recovery. Do not make third-party ChatGPT UI automation a deterministic CI dependency: use a signed
share-host fixture for repeatable text/URL tests plus a recorded physical-device compatibility check.
Run both critical-journey directions (iOS writer/web observer; web writer/iOS observer), simultaneous
allocations, stale reconciliation, actual corrections and membership removal. Both client pipelines
must pass before release; native failures are not waived because the web app passes. No requirement for arbitrary coverage percentages.

### Deployment and operational design

Multi-stage build compiles .NET and Angular, packages static assets into the API image. Compose
runs app, PostgreSQL, a one-shot migration job and optional operator-provided HTTPS reverse proxy;
no proxy service dependency is mandatory on an existing TLS host. DB health gates migrator; successful
migration gates app start. No automatic parallel startup migrations. Pin images/patches at bootstrap,
run app non-root, expose only app/proxy, trust forwarded headers only from configured proxies.
Named volumes persist database, images and Data Protection keys. Readiness checks DB connectivity;
liveness checks process health without restarting on a transient upstream AI outage.

Production cookies require HTTPS; local development uses HTTPS dev certs. Same-origin cookies,
antiforgery headers on unsafe REST actions, no bearer tokens in localStorage. DB password and
bootstrap/recovery secrets use Compose secret files; per-household AI credentials are encrypted
with persisted Data Protection keys and never returned in reads/logs. Configure external provider
allowlists/admin trust restrictions. Log IDs, outcomes and durations, not recipes/tokens/pasted text.

Backup includes pg_dump plus images and key material; document pause-writes backup for consistency.
Before upgrade, back up, review migration data impact and recovery path, run one migrator, then app.
Destructive changes require explicit data migration; rollback restores pre-upgrade database/files
and matching prior image rather than assuming down-migrations are safe. Operator password recovery
uses a local container maintenance command that invokes the running host revocation service and
issues a short-lived Identity reset token without
revealing credentials; no public recovery endpoint or mandatory email service. Protect terminal/log
output and rotate sessions after reset. Supply installation, restore, upgrade and contribution docs,
safe .env examples, dependency notices, and a license selection gate before publication.

### Delivery sequence

Each milestone delivers shared backend, Angular and SwiftUI slices together, with both client
acceptance checks. Share capture is introduced early and shipped in v1, never deferred.

| Milestone | Backend | Angular + SwiftUI / Share Extension | Gate |
| --- | --- | --- | --- |
| 1 Foundation | Identity/setup/invite/tenant isolation, sync revisions, contract generation | Both shells, login/setup/invite/settings, native Keychain/foreground tasks, extension capture skeleton | Role/isolation and native auth tests |
| 2 Recipe library | Catalogue, aliases, fractions/units, recipes, snapshots/preview | Full structured editors, browse/search/filter, organisers/images, accessible controls on both | Calculator properties and dual-client CRUD |
| 3 Import capture | Website and optional AI drafts/review/commit | Both review flows; completed URL/text Share Extension handoff and recovery | Extraction fixtures/share tests, no silent save |
| 4 Planning | Calendar, cooking/allocations, optional actuals/corrections/refresh | Native and web weekly planner, explicit leftover/conflict UI | Allocation race tests and parity journeys |
| 5 Shopping | Both generation routes, unresolved rows, edits/reconciliation | Both list/edit/purchase/review UIs; SignalR/web and conditional-sync/iOS | Bidirectional update and manual-edit preservation |
| 6 Release | Compose/migrations, backups/recovery, hardened contracts | Both clients' accessibility/UX polish, signing/install instructions, share/device verification | All FR-001–056/SC-001–014, restore drill, native+web CI |

Native app build/distribution is separate from Compose: compose hosts the shared server and web
assets; Xcode builds the app and extension. Provide operator/developer signing and device-install
instructions plus optional TestFlight/App Store path; no paid external service is required for core
operation, but Apple's signing/distribution policies and developer-account costs are external
platform constraints. Owners select signing team/bundle/App Group IDs at build time; no credentials
or provisioning profiles in source. Public-source publication and App Store publication are distinct.

## Complexity Tracking

No constitutional violations. A bounded exact ingredient calculator addresses fraction determinism; source snapshots
address edit safety; a per-cooking-event lock addresses concurrent over-allocation. These are bounded
requirements, not a general financial ledger, distributed event architecture, or audit-history system.

### Pre-implementation model review

Reviewed against FR-014, FR-022–028, FR-031–035 and constitution I–V/IX. Detailed invariants are
in [data-model.md](data-model.md); executable-test specifications are in
[acceptance-scenarios.md](acceptance-scenarios.md).

- Allocation commitment is consumed plus remaining reservation, never planned plus consumed plus
  reserved. Release reduces only the reservation; absolute consumption corrections replace values.
- Cooking-event snapshots survive all recipe edits; only explicit refresh adopts the latest recipe.
- Known shopping quantities and additional unresolved contributions remain separately visible.
- Replace numeric(78,0) persistence with ordinary decimals and original bounded fractions. Retain
  a small exact calculator because arbitrary division cannot satisfy display-only rounding with
  finite decimal alone. No custom EF BigInteger mapping or generic arithmetic framework.
- Replace persisted reconciliation proposals with stateless preview/apply, list revision and a
  coarse household source revision. Recompute server-side and preserve overrides/tombstones/manual
  rows/purchased states. Requirement-affecting writers and apply share a short household lock.
  Source-change notifications are advisory: delayed delivery must not erase explicit review of an
  unchanged preview. Apply always submits its original fences; an actual server conflict requires
  fresh preview/review. This avoids extra coordination or guessing revisions on either client.
- No distributed coordination, new services, ledger or background reconciliation is introduced.

All six requested acceptance categories plus refresh, mixed unknowns and stale-preview races are
specified for automation. These are test designs, not implemented or executed tests. That review preserved domain scope and acceptance requirements. The subsequent owner-approved
native scope amendment adds FR-045–052 and mandatory parallel-client delivery. Decimal-only approximation would change FR-014 and needs
explicit approval; it is not adopted. Coarse invalidation may request an extra preview after an
unrelated recipe edit, trading minor UX friction for simpler reliable concurrency.

### Native scope amendment governance check

The owner explicitly approved native iOS and Share Extension in v1, satisfying constitution IX's
scope approval rule. Angular remains mandatory as constitution requires; backend, data isolation,
deterministic calculations and modular monolith constraints are unchanged. SwiftUI is an additive
client, not a replacement stack or separate service. No constitutional rule is waived. All native
requirements are mandatory alongside the existing domain acceptance scenarios.

### Six-finding refinement and first-class backend ownership

The existing .NET 10/ASP.NET Core/EF Core/PostgreSQL modular monolith remains the authoritative
implementation, not a frontend proxy. Core modules own validation and business use cases;
Infrastructure owns the one DbContext/migrations/files/provider adapters; Api owns REST/SignalR
transport and composition. Both generated clients consume identical server outcomes. No rule moves
to TypeScript or Swift, and native/Share Extension are mandatory v1 release components.

| Backend module | Complete owned functionality | Dependent client responsibilities |
| --- | --- | --- |
| Households | Atomic setup/Admin; Identity login/logout/password/reset; invitations/roles/removal; account activity, isolation, revocation and protected settings | Both setup/auth/invite/profile/Admin UIs, expiry/revocation feedback |
| Ingredients | Catalogue/name/alias uniqueness, category correction, structured quantity parsing, serving scaling, exact AU/US conversions | Both catalogue management/editor/autocomplete UIs; no independent calculator |
| Recipes | Full CRUD/lifecycle/favourites, organisers, images, frozen cooking snapshots/explicit refresh contracts | Full web/native library/detail/editor/search/filter |
| Imports | Secure website fetch, optional configured AI, drafts/review validation, durable handoff registration/status/atomic commit/result retention | Both review forms; native extension capture and main-app continuation only |
| MealPlanning + Leftovers | Weekly slots/events, planned/actual prepared/eaten, optional confirmation/corrections, explicit reservations/releases, locked conflict resolution | Both native/web planning, actuals and dependency-conflict UIs |
| Shopping | Both source routes, snapshot-based serving/consolidation, unknown rows/categories/manual/purchased state, stateless reconcile, ETags/retry safety | Both generation/list/edit/preview/apply flows |
| Notifications | Authorized household/personal dispatch, common session revocation gate, compound sync ETags and reconnect recovery | Angular SignalR and Swift foreground conditional refresh |
| Operations | All REST/OpenAPI, errors/logs/health, source-controlled migrations, persistent images/keys/DB, Compose/config/secrets/restore | Web deployment plus native build/sign/install documentation |

**A1**: One SessionRevocationService handles logout, password change/reset, deactivation, membership
removal and stamp rotation. All sends and revokes share a per-user gate; validate account/stamp/
expiry/membership at connect, invoke and dispatch; cancel/drain dispatch before durable revoke,
then abort sockets. No new send starts after the boundary. Integrate the gate with the existing
in-process registry, not a new distributed service. Operator maintenance uses a host-local Unix
socket endpoint (0600, mounted maintenance secret, no public listener) to invoke the same service;
when the host is stopped, offline maintenance is allowed because no live sockets exist. Do not
update Identity in a side process while bypassing the running registry. Last-Admin protection still
applies. Add real connection/barrier tests for every cause, expiry and fresh/old reconnection.

**I1**: Add a small tenant/user-bound ImportHandoff receipt with fixed seven-day expiry and immutable
commit outcome. Draft/provider bodies and ordinary retry caches remain 24-hour data; receipt/results
and expired identity tombstones persist for installation lifetime without raw content. Lock/create
receipt and recipe in one transaction, resolve Committed before draft expiry/If-Match checks, and
return original identity on day-six/later replay. Deleted results return 410, not duplicate recipes.
No history ledger, broker, or generic idempotency framework is added. Account switch uses explicit
new handoff identity; receipt is never silently rebound.

**I2**: Sync ETag includes household/user identity, ContentRevision and requesting user's
PreferencesRevision. Personal changes do not bump household content or another user's revision.
Send PersonalChanged only to that user's authorized sessions, and test 200/304/cross-device behavior.

**C1**: Both UIs explicitly browse/search/create/edit/rename catalogue entries, add/edit/remove
aliases and assign/change categories. Uniqueness is enforced in one canonical+alias namespace;
409 conflicts and referenced-delete errors are visible on both clients. Rename preserves IDs and
recipe references; snapshots stay frozen. Bulk merging is outside the approved v1 specification.

**A2**: Commitment=confirmedConsumed+remainingReserved. Planned quantity is historical intention,
never an extra subtraction. Absolute corrections replace values; release changes only remaining
reservation. Existing household/source locks validate the whole result, including concurrent changes.

**D1**: MP-SYNC-01 in spec.md is the only shared-update performance protocol. SC-005 owns p95<=2s
for four pairings and connected/reconnect/foreground modes; SC-013 checks convergence/dirty-edit
safety and same-user preference correctness. Record each pairing/mode separately, not a blended
average. Buildable tests cover all FR-001–056 and SC-001–014; revised tasks and coverage matrix
provide explicit backend-before-client prerequisites and common contract ownership.


### Implemented import coordination

Backend URL/text extraction now returns typed candidates with preserved source input and explicit
uncertainties. Review retains those candidates alongside the corrected RecipeWrite in the existing
JSONB field. Ingredient matching is a household-scoped Ingredients contract; recipe creation and
reference validation remain a Recipes contract. The import module owns the common transaction and
immutable receipt. External HTTP/AI work occurs between authenticated preflight and persistence
transactions. AI quantities are derived from verified source spans with the same deterministic
parser used for website ingredient text. No provider output executes tools or writes domain data.

The existing encrypted creation retry store handles ordinary extraction/commit retries; durable
handoff receipts handle Share Extension lifetime/recovery. Concurrent first extractions may do
external work more than once, but persist one idempotent draft and at most one committed recipe.
No processing broker or additional distributed locking is introduced. These are implementation
refinements; the approved product scope, optional AI and mandatory Angular/iOS clients are unchanged.
