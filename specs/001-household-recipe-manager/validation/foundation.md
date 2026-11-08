# Foundation verification (in progress)

T010: 16 shared-domain boundary tests first failed against throwing stubs, then passed after
implementation. UUID/revision validation, required/bounded names, optional notes, HTTP URL limits,
ISO inclusive date ranges and IANA household-date handling are covered. Formatting passes.

T011: initial Identity/household/membership/invitation schema and source-controlled EF migration.
Before migration, the real PostgreSQL setup-row test failed on the missing relation; afterward all
five persistence tests passed: singleton setup seed, unique normalized email, tenant-aware invitation
creator FK, one membership/user in v1, UTC expiry roundtrip. EF reports no pending model changes.
Generated migration namespace/encoding were normalized to the repository formatting rules. Recovery
notes are in docs/migrations/initial-households.md. Identity framework rows remain in Infrastructure;
Core has no EF/ASP.NET dependency. Authentication/isolation request guards are verified below; production composition remains T022.

This does not establish completed foundation, authentication, product workflows or release readiness.

T012: 10 authentication integration cases plus five persistence regression cases pass against
PostgreSQL. Signed household/role hints cannot override current membership. Account inactivity,
membership removal, stamp rotation and expired tickets reject stale cookies; member/admin positive
controls and CSRF rejection/acceptance pass. Household query filters fail closed without a resolved
scope. Cookies are HTTPS-only, HttpOnly and SameSite Strict, with a 14-day absolute ticket lifetime
(no sliding extension). Explicit session/SignalR revocation remains T029, not established here.
Formatting passes.

T013: 11 PostgreSQL transaction tests pass (47 backend tests total). Tests cover ordered aggregate
locks, commit/rollback, expired/foreign/stale-stamp/admin checks after the household lock, concurrent
revocation-state revalidation, and actual SQLSTATE 40001/40P01 retries with a three-attempt cap.
A non-transient constraint failure and stale EF concurrency intent are not retried. Callbacks must
reload state each attempt, use this scoped context, and perform no external I/O or notifications.
No independently managed nested transactions or preloaded command state are accepted. Revision
recording and post-commit invalidation are the next foundation task.

T014: four revision integration cases first failed against stubs, then passed. Shared/domain/source
counters increment once per successful transaction. Personal revision is own-user-only and does not
change household counters; shopping-only changes cannot mark requirement sources dirty. Notification
records become drainable only after commit and rollback discards pending records. The recorder is
request-scoped with no durable outbox; conditional sync recovers missed delivery. All 51 backend
tests pass. Range-changing commands must call the same coordinator when implemented.

T015–T016: string decimal/revision/large-integer JSON, strong If-Match (428/412), bounded cursors,
25/default–100/max page limits, safe RFC Problem Details and transactional 24-hour creation retries
are implemented. Retry tests cover same-route replay, changed-payload rejection, route scoping, exact
response preservation, expiry and rollback. Migration CreationRetries stores response text to avoid
JSONB normalization and preserves tenant/user FKs. Contract tests cover lossless quantities/revisions,
calendar limits, safe unexpected errors and household 404s; existing authentication and database tests
cover CSRF/401/403 and cross-tenant FKs. All 58 backend tests pass (16 unit, five architecture,
37 integration). Endpoint composition/generation are subsequent tasks; helpers alone do not establish
all product endpoint coverage.

T017: implemented CSRF bootstrap metadata generates OpenAPI 3.1.1 plus pinned TypeScript types and
all nine generated Swift client/type files. A second clean scratch regeneration reports no drift;
the Swift package compiles. CI checks TypeScript on Linux and both languages on macOS. Separate
TypeScript 5 tooling avoids overriding Angular's TypeScript 6 peer requirements. Web lint, format
and two scaffold tests pass; all 58 backend tests remain green. docs/api-contracts.md explains
regeneration and the difference between current endpoint coverage and the full planned inventory.

T018: Angular provides a lazy four-tab responsive shell, skip navigation, focus indicators and
role-only presentation guard. Shared HttpClient adapter binds same-origin API paths, bootstraps CSRF
for writes, carries ETags/retry keys, handles 304 as cache validation and clears session transport
on 401. It rejects a waiting write after a session epoch change. Six adapter tests plus two shell
tests pass; production build/lint/format pass. Feature pages remain shell entry points pending story
tasks, not completed recipe/planner/shopping/settings functionality.

T019–T020: seven native session/network tests pass on the iPhone simulator. They cover HTTPS origin
validation, expiry cleanup, logout versus late renewal, out-of-order cookie renewal, cross-origin
credential separation, CSRF header+cookie/string-body transport, 401 cleanup, redirects and task
cancellation. Native uses ephemeral URLSession, rejects redirects, inherits normal TLS validation,
and persists only origin-bound session credentials in app-private WhenUnlockedThisDeviceOnly
Keychain items. Cookie jar and CSRF remain isolated from Share Extension storage. Tests first
failed on absent types; after implementation a URLProtocol stream-body fixture issue was corrected.
The generated Swift CSRF DTO is used by networking. No live paid provider or application login
endpoint was needed for these transport tests. Device Keychain lifecycle/entitlements remain part
of the signed-device release checks, not claimed from simulator transport tests.

T021: nine native state/transport tests and the launch UI test pass after the final shell change.
SwiftUI has four native navigation tabs, protected rendering, HTTPS server selection, dirty-input
confirmation/cancel and string quantity inputs. Server/account changes clear origin credentials;
saved-session restore validates through the API. Native feature workflows and sign-in screens remain
story tasks. Angular now also rejects old-session responses and clears personal session state on
a current-session 401 (nine web tests pass).

T022 / Phase 2 checkpoint: real host composition registers Identity/current membership, CSRF,
validation/errors, PostgreSQL, transaction/revision/retry services and protected persistent keys.
Secret-file and encrypted-key/recreation tests pass; macOS uses supported temporary OS key loading
because EphemeralKeySet is unsupported there. Structured errors log codes/trace IDs without raw
exceptions; EF parameter/detail and routine URL logging are disabled. Local helper scripts preserve
existing secrets and the development migration command applies both source-controlled migrations.
OpenAPI regeneration passes with no database/secrets. Final foundation gate: 62 backend tests
(16 unit, five architecture, 41 integration), nine Angular tests, nine native unit/state/network
tests and one native launch UI test pass. Production deployment and all product story acceptance
remain outstanding. No application workflow is claimed complete from this foundation gate.
