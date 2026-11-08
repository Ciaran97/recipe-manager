# Quickstart and Validation Guide (implementation in progress)

Core backend, Angular and native workflows have incremental acceptance evidence in validation/.
The deployable image and production Compose stack pass local TLS/restart/upgrade/restore checks;
final cross-client, physical iOS, accessibility and reference-host gates remain open.

## Prerequisites and development

.NET SDK 10.0.403 (global.json); Node 26.10.0 (.nvmrc) with locked Angular 22/TypeScript 6; Docker Engine/Desktop
with Compose v2 or later (locally verified Engine 29.8.1 / Compose 5.5.1); HTTPS development certificate; 2 vCPU/4 GiB reference Linux host for measurements.
Use the checked-in deployment initializer and local secret files; do not place real secrets in Git.
From repository root:

```sh
dotnet restore RecipeManager.slnx --locked-mode
npm ci --prefix src/RecipeManager.Web
npm ci --prefix scripts/contracts
scripts/prepare-development.sh
docker compose -p recipe-manager-dev -f deploy/compose.dev.yaml up -d db
scripts/run-api-development.sh migrate
scripts/run-api-development.sh run
scripts/run-web-development.sh
```

The Angular HTTPS dev proxy forwards `/api`, `/hubs`, and `/openapi` to HTTPS API, including WebSockets,
so login and CSRF behave like one origin. Application development secrets use .NET user-secrets,
production secrets mounted files. Never use public-registration endpoints after initial setup.

## Automated checks

```sh
dotnet build RecipeManager.slnx --no-restore
dotnet test RecipeManager.slnx --no-build
npm test --prefix src/RecipeManager.Web -- --watch=false
npm run build --prefix src/RecipeManager.Web
npm run check:contracts --prefix src/RecipeManager.Web

# Isolated browser journeys (one fresh installation per feature):
npm --prefix tests/e2e test -- planner.spec.ts
```

Docker must be available for Testcontainers. Do not substitute a mock database for PostgreSQL race tests. Contract check regenerates
OpenAPI/types to a temporary directory and compares against checked-in artifacts.

The native planner journey runs with `scripts/test-ios-households.sh PlannerUITests` on macOS
with the pinned Xcode and simulator runtime. It first completes setup/invitation through the native
UI, then exercises scheduling, partial consumption, reservation release and explicit snapshot refresh.
Run browser and native fixture commands sequentially: both reserve the same local HTTPS ports and
refuse to reuse an existing installation. See `docs/testing.md` for private fixture artifact handling.

## Validation scenarios

1. **Bootstrap and invitation**: initialise with operator setup token, create Admin and household;
   issue 48-hour invitation, join as Member with separate credentials. Verify second public signup,
   reused/expired invite, Member admin actions and second-household joining are rejected. Concurrent
   bootstrap accepts only one first registration. Remove member during active shopping connection;
   subsequent REST access/subscriptions/updates are denied while shared records remain intact.
2. **Ingredients**: enter fractions/mixed fractions and separate preparation notes; duplicate,
   group/reorder rows; preview servings without mutation. Verify 500g+250g=750g, 150g+0.2kg=350g;
   canonical red capsicum+red pepper aliases consolidate, beef mince/strips do not. Same ingredient
   with AU cup+US cup yields exact 486.588 ml before display rounding; original text/unit remains.
   Reject 1/0 and ambiguous standard; unknown amounts remain flagged, grams/cups are not guessed.
3. **Critical workflow**: paste test ChatGPT text using fake AI adapter; review missing/uncertain
   quantities and measurement standard; save in collection with tags. Schedule four prepared/two
   eaten Monday and explicitly reserve two for Tuesday; cook another recipe Wednesday. Generate
   Monday–Wednesday list, verify exact planned totals and no Tuesday ingredients, edit and purchase
   items, and observe them in a second session without refresh. Also run without AI configured:
   manual recipes, URL extraction, calendar and shopping continue working.
4. **Actuals and conflict**: confirm Monday actual five prepared/1.5 eaten with two reserved;
   availability becomes 1.5. Confirm Tuesday two consumed; availability stays 1.5, not negative.
   Correct preparation below committed/reserved total: require resolution and retain valid state.
   Exercise 0.5 increments and direct 1.25 input; batch-cook with zero eaten. Partial consumption
   explicitly retains/releases remainder. Unconfirmed planning and shopping still work.
5. **Snapshot reconciliation**: change source recipe/event after list edit; list remains unchanged.
   Preview old/new/manual states and explicitly keep/accept/remove differences. Manual items,
   overrides, tombstones and purchased states survive unless selected otherwise. Actuals affect
   requirements only after an explicit actual-basis reconciliation. Edit source/list between
   preview and apply; stale preview is rejected. Repeated apply with an old revision cannot duplicate effects.
6. **Concurrency and isolation**: run two simultaneous allocations against last portion; one can
   succeed and the other conflicts without over-allocation. Test stale ETags/list edits. Seed two
   households in test fixtures and deny cross-tenant IDs, searches, images, imports and hub access.
7. **Imports and accessibility**: URL fixtures cover valid/partial/multiple/no recipe, timeout,
   private-address redirects and invalid images. Fake AI covers invalid schema, uncertainty and
   outage. Complete critical journey with keyboard only and at 320px mobile/desktop sizes; run axe
   plus screen-reader/focus review. Do not use live paid providers as a release-test dependency.

## Production, migrations, backup and restore

Production startup (creates fresh secrets only on first installation; see docs/self-hosting.md):

```sh
python3 scripts/configure-self-hosting.py https://recipes.home.arpa
docker compose --env-file deploy/.env -f deploy/compose.yaml -f deploy/compose.tls.yaml up -d --build --wait
docker compose --env-file deploy/.env -f deploy/compose.yaml -f deploy/compose.tls.yaml ps -a
docker compose --env-file deploy/.env -f deploy/compose.yaml logs migrator
```

Expect healthy PostgreSQL, successful one-shot migration, then ready app serving SPA/API/hub.
Only app/proxy is exposed; database stays private. Verify process liveness and DB-backed readiness,
then repeat create/save and restart to prove persistence. Blank/placeholder secrets must fail
startup clearly. Keys must survive app replacement or login/AI credential recovery may fail.

Backup drill: pause writes; use pg_dump in custom format, copy image volume and protected key ring,
record app image/schema version and securely store configuration. Restore into clean isolated
volumes using pg_restore and matching images/keys; verify counts, representative recipe values,
allocations and shopping edits, then invite/login access. Document concrete commands/paths with
implemented Compose service names in operational docs. Do not delete original volumes for testing.

Upgrade drill: take verified backup, review checked-in migration/recovery notes, stop old app,
run one migrator, start new app and validate. Failed migration must block app startup. For rollback,
restore verified pre-upgrade DB/files/keys plus matching prior image; never assume dropping new
columns is lossless. Password recovery uses operator-local maintenance reset, not public signup.

## Acceptance completion

Record real command outputs and SC-001–014 results, framework/package locks, reference hardware,
95th-percentile timings from 100 samples, accessibility review, and restore evidence. Resolve owner
license selection and dependency notices before public distribution. Do not mark design-only
commands as passing tests until implementation exists.

## Model-review regression suite

Automate [acceptance-scenarios.md](acceptance-scenarios.md) with pure rule tests and real
PostgreSQL integration/API tests as indicated. These scenarios supplement, not replace, the
approved feature acceptance scenarios. Backend rule, concurrency, snapshot, unknown-quantity and
reconciliation cases now pass with real PostgreSQL; see validation/acceptance-report.md for scope.

## Mandatory native v1 setup and validation

Use macOS with Xcode 27.2 / Apple Swift 6.4.2 (Swift language mode 6). The local runtime is iOS
27.0 with iPhone 18 Pro fixtures. The deployment target remains iOS 18.0; attempts to download its
ARM-only and universal simulator runtimes return unavailable, so minimum-OS testing remains open.
Choose an installed simulator UUID and export RECIPE_MANAGER_SIMULATOR_ID before these commands.
The following build/unit operations have passing local evidence:

```sh
xcodebuild -resolvePackageDependencies -project ios/RecipeManager.xcodeproj -scheme RecipeManager -onlyUsePackageVersionsFromResolvedFile
xcodebuild -project ios/RecipeManager.xcodeproj -scheme RecipeManager -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
xcodebuild -project ios/RecipeManager.xcodeproj -scheme RecipeManager -destination "platform=iOS Simulator,id=$RECIPE_MANAGER_SIMULATOR_ID" CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- test -only-testing:RecipeManagerTests -only-testing:ShareHandoffTests
scripts/test-ios-households.sh CriticalJourneyUITests
```

The RecipeManager scheme embeds the Share Extension. The explicit unit selection avoids treating
skipped fixture-dependent UI tests as acceptance. The signed isolated script runs real UI/extension
journeys against a fresh migrated PostgreSQL/HTTPS backend and deterministic AI fixture. It uses
owned simulators and refuses occupied fixture ports. No production account or paid provider is
needed. Keep private fixture artifacts out of source/publication; see docs/testing.md.

For physical installation choose a real signing team, unique bundle IDs and matching App Group in
local configuration. Do not commit credentials/profiles. See ios/README.md and docs/ios-installation.md.
Compose serves the shared backend/web application; it does not build or host the native binary.

Set the app's server URL to a reachable HTTPS backend. Simulator may use the development host;
physical devices need a LAN/VPN-resolvable hostname and trusted certificate. Install/trust the local
development CA explicitly where appropriate; never disable TLS verification or use a production
ATS arbitrary-loads exception. Expose API only through the existing trusted reverse proxy. Operator
must provide network reachability; the app does not add a cloud relay or require APNs.

Run every Story 1–7 workflow and SC-001–014 in Angular and SwiftUI, including admin setup/invites,
full recipe CRUD/favourites/organisers/catalogue, quantities/scaling, both imports/review, planner,
actuals/leftovers, list generation/manual edits/reconciliation and settings. Test iOS as writer with
web observer, then reverse, checking foreground visibility within two seconds. Background native,
edit from web, foreground and verify catch-up without lost dirty edits. Remove member/change password
while native is active and confirm access loss. Restore Keychain session after app termination,
then test expiry, all-session logout, account/server switch and no credential leakage in files/logs.

Share tests: signed fixture host exports UTType.url and UTType.plainText, opens system share sheet,
chooses Recipe Manager, and verifies protected App Group handoff. Main app continuation routes into
normal editable import review. Cover closed app, signed-out state, unavailable backend, unsupported
input, mixed representations, oversized content, cancel/discard, queue limit/expiry, termination,
account/server mismatch and duplicate retries. Check extension never performs AI/extraction and no
recipe exists before save. Supplement CI fixtures with physical-device Safari URL and ChatGPT text
sharing compatibility checks; do not require controlling a third-party app UI for deterministic CI.
VoiceOver/Dynamic Type checks and native date/navigation/sheet interactions are release gates.

For release, archive app+extension with documented signing/entitlements, validate App Group access
on device and provide developer/device-install instructions. TestFlight/App Store distribution may
be chosen separately and follows Apple's platform/account policies; neither prototype-only delivery
nor deferring native is acceptable for v1. The public-source license gate remains unchanged.

## Six-finding regression validation

Backend AC-16–20 regressions pass with real PostgreSQL, actual SignalR clients and controlled
clocks. Signed/native and Angular evidence is recorded per story; the complete Story 1–7 parity
matrix and AC-21 reference-host measurements remain open. The 336-test backend suite runs without
Angular or SwiftUI, and both clients use those same authoritative APIs.

- Session revocation: every cause, active/invoking/queued broadcasts, expiry, old reconnect rejection
  and fresh-login success. Operator reset/deactivation while running uses the protected host-local
  maintenance socket; offline direct maintenance requires the API stopped. Verify secret not logged.
- Durable imports: lost successful response, six-day replay after 24-hour cleanup, concurrent commit,
  fixed-expiry 410, committed outcome after expiry, deleted-result 410, owner denial and restore.
- Personal sync: A's Angular/iOS validator changes after own preference/favourite; B remains 304;
  household changes invalidate both; authentication is evaluated before 304/private-cache handling.
- Catalogue: both UIs explicitly create/edit/rename canonical ingredients, manage aliases/categories,
  surface conflicts/references and preserve frozen snapshot details. No bulk merge is expected.
- Leftovers: commitment=c+r only, release does not change consumption/planned amounts; freed amount
  can be reserved once by another meal, with PostgreSQL transaction/race protection.
- MP-SYNC-01 replaces duplicate sync benchmark instructions: 100 samples per four-pairing/three-mode
  cell, p95<=2s, recovery/convergence and dirty-edit correctness. Retain search/calendar timing checks.

Backup includes durable handoff outcomes/tombstones; 24-hour cleanup removes raw draft/provider bodies
and ordinary retry cache only. Never delete the receipt index with draft cleanup. Native local input
expires at seven days while committed server results remain recoverable. Record SC-001–014 and
AC-01–21 evidence in the existing feature validation directory when tests actually run.

### Shopping workflow checks

Run the isolated Chromium shopping journey with the documented .NET/Node toolchains available:

```sh
npm --prefix tests/e2e test -- shopping.spec.ts
```

It covers planned meal-range generation before actual cooking confirmation, explicit leftovers
without additional shopping requirements, independent unknown amounts, editable snapshot quantities,
manual additions, purchased state, reviewed reconciliation and two authenticated browser sessions.
The receiving browser must reflect the other session's purchase update within two seconds while
preserving any dirty form on a later remote edit. This is incremental evidence; the complete
MP-SYNC-01 cross-client performance gate remains part of final verification.

Run native shopping through the signed simulator fixture:

```sh
scripts/test-ios-households.sh ShoppingUITests
```

The fixture first completes real administrator setup and member invitation/authentication, then
runs the native shopping journey against that household's API/database. Browser and native
fixtures reserve the same local ports; run them sequentially. The fixture owns and removes its
test simulator/database and does not reuse a developer installation. Native live observation is
verified after the US8 foreground synchronisation implementation.

### Native foreground and cross-client observers

```sh
scripts/test-ios-households.sh CrossClientUITests NativeSyncUITests ShoppingUITests
```

This uses the same owned fixture and real authentication prerequisite. Selecting
`CrossClientUITests` additionally starts a Playwright Angular observer/writer and a temporary
HTTPS control service on loopback port 7291. The controller requires a fresh per-run secret,
limits requests to bounded test commands and is removed with the fixture. It adds no production
endpoint, domain mock or database table. Angular and SwiftUI make real shopping writes against
the shared API; the other client must render the accepted state. The native-only observer also
checks retained dirty text and immediate foreground recovery. Keep ports 4201, 7285 and 7291 free.
The attached monotonic-clock fixture sample is incremental acceptance evidence, not the full
MP-SYNC-01 reference-deployment/p95 matrix.


### Signed Share Extension capture

```sh
scripts/test-ios-households.sh ShareHandoffUITests
```

The owned fixture installs a separate nonshipping `RecipeManagerShareHost` app. It signs out and
terminates Recipe Manager, shares URL/text/mixed payloads through the system share sheet, and
verifies durable App Group capture plus cancellation. This tests capture without external recipe
extraction or AI. The fixture must have an explicit matching `RecipeManagerAppGroup` plist value;
an arbitrary `INFOPLIST_KEY_...` setting is not sufficient. Production app/extension signing uses
`ios/Project.xcconfig` and an ignored `ios/Local.xcconfig`; copy the values from
`ios/Signing.xcconfig.example` and register the same App Group for both targets. Simulator capture
acceptance does not replace the physical Safari/ChatGPT checks in T101.


### Backend import regression

Run `dotnet test tests/RecipeManager.IntegrationTests --filter FullyQualifiedName~Imports` and
`dotnet test tests/RecipeManager.UnitTests --filter FullyQualifiedName~ImporterTests` with the pinned
.NET SDK and Docker available. Tests use synthetic HTML/JSON, isolated HTTP fixtures and a validated
fake AI provider. They do not require a paid subscription, provider key or public website access.

The authenticated API now supports URL/text extraction, editable private drafts, explicit reviewed
commits and durable Share Extension handoff registration/status/commit. Website imports work with AI
disabled. Optional AI is enabled by an Administrator in household settings with a compatible API
base URL (including its base path, commonly `/v1`), model and optional protected API key. The adapter
appends `/chat/completions`. Imported data creates no recipe until explicit reviewed commit.
In Angular, choose Recipes → Import recipe. In iOS, use the Recipes import menu; pending shared
captures appear in the recipe library and require explicit account/server confirmation. Both
clients use the same full recipe editor for import review, require missing source standards and
units to be corrected, and disclose provider processing before extraction. No provider credentials
are sent to either client. Run the signed isolated journey with
`scripts/test-ios-households.sh ImportReviewUITests` and
`scripts/test-ios-households.sh ShareImportUITests` in separate fresh fixtures; set
`PLAYWRIGHT_BROWSERS_PATH` if Chromium was installed in a custom cache directory. Physical
Safari/ChatGPT and VoiceOver acceptance remains a separate device gate.

### Verified recovery, accessibility and release commands

The browser lost-success-response journey passes with
`npm --prefix tests/e2e test -- import-retry.spec.ts`. The signed native equivalent passes with
`scripts/test-ios-households.sh ImportRetryUITests`. These deliberately drop one real successful
commit response, remove the temporary draft and disable AI before retrying the durable receipt.
The browser's six-day advance is client-clock only; server-time delayed/expiry behavior is separately
covered by controlled integration tests. Neither test requires paid AI.

`npm --prefix tests/e2e test -- accessibility.spec.ts` passes nine screens at 320px and 1280px with
zero automated WCAG A/AA violations and no horizontal overflow, plus keyboard skip-link and manual
recipe entry navigation. See validation/accessibility.md for the exact scope and remaining manual/
physical-device checks. Automated scans do not establish full accessibility acceptance.

The latest complete backend suite has 348 passing tests, including importer quality regressions; the final focused isolation/
revocation rerun has two passing tests and no skips. The current full native unit/handoff baseline
has 95 passing tests (76 application and 19 handoff tests). SDK 10.0.403, ASP.NET 10.0.14, Node 26.10.0, Angular 22.2.4, CLI 22.2.5,
TypeScript 6.0.2 and Xcode 27.2 are the locally verified versions. Use the checked-in locks.

New orchestration entry points are `scripts/validate-release.sh --backend-web`, `--native` (set
`RECIPE_MANAGER_SIMULATOR_ID`), `--native-parity` and `--native-acceptance`. The original thirteen-suite
`--native-parity` orchestration passed without skips on iOS 27.0; three added suites remain under validation; see validation/native-parity.md
for its scope and additional operation-level assertions. Shell syntax and workflow YAML have been
checked; other combined release modes and hosted workflows have not yet passed end to end.
Individual underlying checks are documented above; do not infer hosted CI success. See
[the release checklist](../../docs/release-checklist.md) for remaining gates and
[publication decisions](../../docs/publication-readiness.md) for the unresolved owner choices.

The continuous Angular acceptance journey also passes with
`npm --prefix tests/e2e test -- critical-journey.spec.ts`. It verifies import review, organisers,
four planned portions/two eaten, two explicit leftovers, 500 g consolidated rice, an unchanged
cooking snapshot after recipe edit, and explicit refresh/reconciliation to a 550 g baseline while
retaining a 600 g manual override, purchased state, bread and unresolved salt. This is the web
baseline; full native/share/bidirectional continuous acceptance remains open.


### Continuous cross-client critical acceptance

`scripts/test-ios-households.sh CriticalJourneyUITests` passes both native-first and web-first
critical journeys against one shared HTTPS backend/PostgreSQL fixture. It installs the real signed
Share Extension and a nonshipping controlled share host, then drives complementary Angular and
SwiftUI writes/observers. Exact 500 g generation and 550 g reconciled baselines agree while a 600 g
manual override, purchased state, bread and unresolved salt remain. No skips or paid provider.
See validation/acceptance-report.md for the complete scope and remaining story/device/performance gates.

`RECIPE_MANAGER_E2E_TEXT_SIZE=accessibility-extra-extra-extra-large scripts/test-ios-households.sh DynamicTypeUITests`
passes native setup/invitation/profile and measured title growth/calendar targets at the real largest
system text size. Normal title height is 20.333 points versus 125.333 at the largest size. This does
not close the remaining unfiltered audit warnings, VoiceOver or minimum/current OS gates.

The locally verified compiler is Apple Swift 6.4.2 in Xcode 27.2 (Swift language mode 6).

## Current complete backend/web recheck — 2026-11-05

`scripts/validate-release.sh --backend-web` passes end to end: 15 measurement/controller regressions, locked restore, generated OpenAPI/TypeScript comparison, build/format, 348 backend tests, Angular lint/format/63 tests/production build, configured advisory thresholds and every independent browser acceptance fixture. Separate native parity/accessibility/minimum-OS/performance and publication gates remain open. The [README](../../README.md) and [Cloudflare guide](../../docs/cloudflare-tunnel.md) now provide validated first-install proxy/tunnel instructions.
