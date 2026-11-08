# Household implementation verification (in progress)

T023 defines API scenarios for initial setup closure, 48-hour single-use invitations, Administrator/
Member settings, own profile, encrypted/write-only provider keys, all-client logout and password
change. All four cases fail at missing product endpoints, as expected before implementation.

T024 defines separate-client PostgreSQL races for first setup, invitation acceptance and duplicate
member removal with immediate access revocation. The first two baseline cases fail at missing setup;
the added removal race remains pending the same endpoints.

T025 defines a real protected test SignalR hub and pinned client, six-cause revocation matrix,
reconnection, expiry and queued/in-flight dispatch barriers. Seven cases fail against temporary
revocation/registry stubs; expired-cookie connection rejection already passes. These are deliberate
red test baselines, not completed household functionality. Production hub replay is required at T078.

NuGet's high-severity SignalR audit prompted the patched SDK/runtime/package baseline documented
in research.md and docs/dependency-review.md. No audit suppression was used. Full previous foundation
regression on the patched runtime is part of this story's completion gate.

T026: direct service integration verifies invalid setup secret leaves no account and concurrent
creation yields one household/Admin under the seeded installation lock. Module initializers join
that same transaction when their default tables are added. Endpoints are still T030.

T027/T029: invitation service issues hashed 48-hour tokens, accepts once under household/invitation
locks, creates separate Member accounts, checks membership revisions and preserves shared data on
removal. Service acceptance tests cover concurrent single use, invalid token, removal and last-Admin
protection. Common revocation was completed before dependent removal/password command completion.
The protected-hub suite now passes 11 cases covering all six causes, queued/started sends, bounded
stall cancellation, mid-session expiry, stale reconnect and valid new credentials. A non-cooperative
transport is aborted and kept attached to the fence; revocation cannot commit until its task drains.
A bounded failure returns session_drain_pending and can be retried. No unfinished send is dropped
from the fence. Twelve membership/revocation integration cases pass together on .NET 10.0.14.

These service tests do not yet establish public endpoint/client wizard or operator socket coverage.

T028/T030: profile/settings/password services and the complete household endpoint set are live.
Public operator recovery remains excluded; credential/setup/invite routes are throttled. Full backend
gate passes 86 tests (16 unit, five architecture, 65 integration). API tests cover lost invitation
response replay with encrypted cache data, 32-member cursor traversal without duplication, consistent
401/429 errors, setup/invite/removal races and all-session logout/password change. Personal profile
updates leave household/source counters unchanged. AI key preservation/clearing and write-only GET
projections are verified. Standard Data Protection protects cached invitation-link responses; the
permanent invitation stores only its hash.

OpenAPI includes cookie security, CSRF/If-Match/conditional headers, string revisions, safe Problem
Details and write-only provider keys. Valid nullable-object inlining preserves optional AI settings
for the pinned Swift generator; the generated client compiles. Further schemas/endpoints remain
regenerated after each endpoint task. Native and Angular household screens are still pending.

T031/T034: a real child API process test verifies Unix socket mode 0600, mounted-secret enforcement,
TCP denial even with the secret/forwarded headers, and denial before malformed body binding. The
operator CLI launches separately but never starts a second host or touches Identity directly. Its
stdin password-reset request reaches the running host, changes the password through the common
fence and prints no password. Stamp rotation succeeds and sole-Admin deactivation is rejected.
The short-lived Identity reset token is generated/consumed server-side; no public token/reset route
is exposed. docs/recovery.md documents live-host recovery, private runtime directory, stale sockets
and safe stop/restore/restart. This dependent operator task was completed alongside T031 before the
independent client-screen tasks, preserving IDs.

T032: Angular setup/login/invite and own-profile/password plus role-appropriate household/member/
invitation/optional-AI settings are implemented against the shared API. Seventeen web unit tests
pass, including CSRF refresh after sign-in, current/old session failure handling, logout network
failure, invitation origin binding, Member control hiding, exact opaque ETags and key preservation.
Production compilation succeeds (288.45 kB initial bundle). Invitation tokens leave history and
referrers; passwords/keys are never persisted in JavaScript. HTTPS development proxy verifies backend
TLS with the local development CA, rather than disabling verification. Full browser/native journey
acceptance remains T035 after the native screens are added.

T033: native SwiftUI setup/login/invitation, profile/password, Admin/member settings, members,
48-hour invitation creation/share/revocation, AI settings and all-session logout use the same API.
Sixteen native state/network/presentation tests and the launch UI test pass. Tests include login
generation changes, paired CSRF cookie/token handling, correct server-date/string-revision decoding,
key preservation, stale-edit retention and origin-confirmed invitation opening. Native backend
calculations remain entirely in the shared backend. Account/server changes reset protected view
identity and credentials; provider keys/passwords are not retained in feature state after submission.

T035 browser gate: real Chromium journey passes in 3.1 seconds against a fresh Testcontainers
PostgreSQL database and generated local HTTPS fixture. It performs setup, invite acceptance in a
second context, Member control/API restriction, access removal and all-client logout. It never uses
the development database. Native full journey remains in progress using a disposable simulator.

T035 regression: full backend passes 87 tests (16 unit, five architecture, 66 integration), with
no skips. The operator functional test now allows 15 seconds for a request including password
hashing under concurrent build load, rather than an unrelated two-second timeout. The shared-update
performance criterion is unchanged. .NET formatting and Angular lint/format checks pass. Browser
acceptance is included in Linux CI; no hosted run has been claimed. See docs/testing.md for isolated
fixtures and the additional Docker-backed macOS native gate.

Native acceptance diagnosed an unsigned simulator build's Keychain failure. Simulator tests use
ad hoc signing and a real Keychain round-trip test now complements fake-store session/state tests.
The signed full journey reaches the setup form; its submission investigation remains open. T035 is
not marked complete until the actual native journey and Angular cross-client assertion pass.

Native follow-up: all 18 unit/state/presentation tests pass, including real simulator Keychain
create/update/read/delete and both NSURL/NSString invitation provider paths. The full UI journey
proved setup, authenticated settings, invitation creation, URL sharing, logout and invitation-mode
entry. It exposed that String-only Paste did not accept the share sheet's URL item; the explicit
Paste action now supports both URL and plain text, reports load failure and reads no ambient
clipboard. Native auth validation errors remain visible above the scrolling form. The acceptance
harness handles the OS strong-password sheet and its Copy cell using observed accessibility types.
Full member registration/profile/cross-client completion remains the open T035 gate.

T035 native gate now PASSES: the actual SwiftUI journey completed in 127.393 seconds with no skips.
It creates the administrator/household, issues and copies an invitation through the native share
sheet, signs out all sessions, pastes the URL, registers a separate Member, edits the Member profile
and verifies Administrator controls are absent. A subsequent real Chromium check against the same
fixture sees "Updated on iOS" through Angular (969 ms test, 1.6 seconds runner). The fixture used
its own disposable PostgreSQL container and simulator; neither development data nor system trust
was changed. Native Keychain and paste regression coverage remains 18 passing tests. Production
hub security coverage is still extended at T078 as explicitly required by the task dependency.

Final browser gate rerun passes against a new installation with an explicit assertion that setup
is required: 3.3 seconds test / 12.0 seconds runner. T035 is complete. Future domain endpoints and
production hubs extend the existing tenant/revocation matrix; this does not claim those endpoints
already exist. Phase 3 independent acceptance is closed on both clients.

## Physical shopping regression — 2026-11-04

The designated iPhone passed `ShoppingUITests` in 112.981 s against the isolated production-mode
LAN deployment after correcting Caddy's API ETag encoding. The test retains existing household
content and scopes its own ingredient/list identities. It verifies planned requirements before
actual cooking, known and unresolved requirements, purchase state, manual items, explicit
reconciliation preserving a quantity override, and stable snapshot labels after catalogue edits.
The combined invocation also contained an unrelated ambiguous recipe-title test failure; only the
shopping case is recorded as passed here. This is functional evidence, not MP-SYNC-01 performance.
