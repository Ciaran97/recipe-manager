# Research and Design Decisions

Research date: 2026-11-01. Product authority is spec.md and constitution v1.0.0. Decisions below
are architectural choices inferred from the requirements and cited platform capabilities, not
claims that a feature has been implemented. All technical unknowns are resolved for task planning.

## Platform baseline

**Decision**: .NET/ASP.NET Core/EF Core 10, Npgsql EF provider 10, PostgreSQL 18; Angular 22,
TypeScript 6.0.x, Node 26.10.0 within the supported 26.x range and RxJS 7.x within the supported Angular range.
Pin exact stable patches at bootstrap and check the official compatibility matrix then.
**Rationale**: Match the mandated backend and current compatible frontend, without preview packages.
**Alternatives considered**: Older Angular LTS is possible but unnecessary for a new project;
preview .NET or mismatched EF provider majors add risk.
Sources: [Angular compatibility](https://angular.dev/reference/versions),
[Angular releases](https://angular.dev/reference/releases),
[Npgsql 10](https://www.npgsql.org/efcore/release-notes/10.0.html),
[PostgreSQL versions](https://www.postgresql.org/support/versioning/).

## Authentication and tenant authorization

**Decision**: Identity password accounts, secure HttpOnly same-origin cookie, antiforgery header,
custom first-run/invite endpoints; no generic public registration endpoint. Validate membership
and role in the database per request. Invitations last 48 hours, single use, token hashes only;
admin copies a link (no email dependency). Initial setup requires an operator-generated one-use
setup secret from a mounted file, protecting an uninitialised network installation.
**Rationale**: Meets first-user setup and simple personal credentials without token storage in JS.
Use a singleton installation row lock for setup, invitation row lock for acceptance, and household
row lock for last-administrator checks. Password changes require current password and revoke old
sessions. Recovery is an operator-only Identity reset action; no unsolicited public recovery UI.
**Alternatives considered**: JWT refresh infrastructure, social login and mandatory SMTP expand MVP.
Sources: [Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0),
[antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

## Persistence and concurrency

**Decision**: One EF DbContext and migration assembly, module mappings, composite household FKs,
application-managed bigint revisions. Lock cooking source row before all allocation/balance edits;
lock shopping list before edits or reconciliation. Use consistent sorted lock order. Bounded whole
transaction retries (maximum three) for PostgreSQL serialization/deadlock failures; stale client
revisions return conflict rather than retrying stale intent. No EF InMemory correctness tests.
**Rationale**: Optimistic row tokens alone do not protect sum-of-child-allocation invariants.
**Alternatives considered**: Per-module databases/distributed transactions overcomplicate the monolith;
PostgreSQL xmin is usable but explicit revisions better represent aggregate/API versions.
Sources: [EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions),
[EF concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency),
[Npgsql concurrency](https://www.npgsql.org/efcore/modeling/concurrency.html).

## Exact quantities and units

**Decision (reviewed)**: Ordinary entered decimals and portion values use decimal/numeric(18,6).
Original fractions use bounded bigint components and original text. Remove numeric(78,0) columns.
Use a small pure exact-fraction calculator only for ingredient scaling/consolidation; derived
exact values live as string components in existing snapshot/baseline JSON, not a custom EF type.
Input/result limits and display handling are defined in data-model.md.
**Rationale**: Decimal storage is simpler for ordinary values, but decimal-only calculation cannot
represent 1/3 or arbitrary servings ratios exactly. The approved FR-014 permits rounding only for
display. Three 1/3-gram contributions must sum to exactly one gram. .NET decimal has finite scale;
changing the approved requirement to a bounded approximation needs owner approval.
**Alternatives considered**: Full 78-digit relational arithmetic was unnecessary. Decimal-only
would be reasonable if the product explicitly accepted a numeric tolerance and persisted rounded
results, but that is not the currently approved policy. Binary float remains prohibited.
Source: [System.Decimal representation](https://learn.microsoft.com/en-us/dotnet/api/system.decimal?view=net-10.0).
Display policy: original recipes keep original text/unit; calculated g/ml use up to two decimals,
trim zeros and use midpoint-away-from-zero; item/portion displays use up to six decimals. Use kg/L
when absolute value >=1000. Label non-exact displays approximately and expose exact detail; never
show a positive result as zero (show '<0.01' with detail). Prefer halves/quarters/eighths in original
cup/spoon previews only when exact. Display values are never resubmitted as source values.

## Snapshot and reconciliation semantics

**Decision (reviewed)**: Cooking owns an immutable recipe snapshot until an explicit previewed
refresh. Meal-range shopping always reads this snapshot, never the latest linked recipe. Keep one
shopping baseline and manual-edit flags; preview is a transient DTO and apply recomputes authorized
requirements/choices under list and household revision checks. No Reconciliation table or expiry.
**Rationale**: The baseline is already necessary to explain changes; a proposal persistence layer
is not. A coarse household source revision detects insert/delete/move as well as updates, at the
cost of occasional extra previews after unrelated source edits. All requirement-affecting writers
and apply take the household lock before aggregate locks, so validation/commit is race-safe.
**Alternatives considered**: Stored proposals/digests and generic three-way merge infrastructure
are unnecessary; blindly replacing items or adding deltas to overrides loses user intent.
Known subtotals and unresolved contributions remain separate display rows; purchased states and
manual overrides never silently suppress unresolved requirements.

## Actuals and leftovers

**Decision**: Unconfirmed source uses planned prepared/eaten; confirmed source uses actuals.
Each downstream allocation records consumed and reserved remainder separately. Balance subtracts
each once. Consumption confirmation requires actual production confirmation; the UI can confirm
source and allocation together in one transaction. Optional confirmations never block planning.
**Rationale**: Consumption of unconfirmed food must not imply production silently. Corrections
are mutable values without audit history, with atomic validation against all dependents.
**Alternatives considered**: Automatic leftovers, expiry, inventory ledger and enforced daily
confirmation are outside the agreed product.

## Real-time state

**Decision**: SignalR shopping and household invalidations carrying IDs/revision only; REST refetch on event,
reconnect, focus and every 30 seconds while a list is visible. One in-process connection registry;
on any session revocation stop future sends to that user and abort connections before returning.
Serialize membership authorization/send dispatch against removal; never rely solely on stale hub
claims or group membership. Handlers recheck membership. Notifications are best effort after commit;
periodic REST recovery handles a crash between commit and notification, without adding an outbox.
**Rationale**: Small scale does not warrant a broker; no accepted write depends on message delivery.
**Alternatives considered**: Full-document broadcasts leak content and disrupt local dirty forms;
SignalR groups alone do not implement revocation.
Sources: [SignalR authorization](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0),
[JS reconnect](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0).

## Imports and provider boundaries

**Decision**: Bounded HTTP(S) fetch and schema.org Recipe JSON-LD extraction, including @graph
and multiple recipe selection. No headless browser/paywall bypass. Non-extracted fields remain
explicit for review. AI adapter returns a schema-validated draft, no tools or domain write access.
Initial optional adapter targets an OpenAI-compatible chat-completions HTTP shape (configurable
base URL/model); fake adapter for tests, other providers via the same interface. This is an
interoperability choice, not a requirement to use a paid vendor or an OpenAI SDK.
**Rationale**: Deterministic review is more valuable than arbitrary site scraping complexity.
Source standards come from explicit metadata/user confirmation, never country-name heuristics.
Limits: 15 s website timeout, 2 MiB decoded body, at most 3 redirects, 1000 ingredient/instruction
rows; pasted text 100 KiB, AI 60 s timeout, validated response <=1 MiB. Drafts expire after 24 hours.
Block non-public addresses after DNS resolution on every connection/redirect, pin resolved target
while preserving host/TLS validation, disable ambient proxy/cookies, reject embedded URL credentials;
explicit provider URLs are separately administrator-controlled and may target a local AI host.
**Alternatives considered**: General web crawling, social extraction, arbitrary AI agent tools and
mandatory paid SDK integrations are excluded.

## Contracts and deployment

**Decision**: Versioned REST resource endpoints, generated OpenAPI 3.1 and Angular types, documented
ETags/Problem Details/decimal strings. Compose database → migrator → app, persisted keys/images,
operator TLS and backup/restore guides. PostgreSQL patch images and npm/NuGet versions locked at
implementation. No application deployment performed during planning.
**Rationale**: One web origin and one image for app/SPA simplify hosting and cookie security.
**Alternatives considered**: Separate SPA hosting, startup auto-migrations from multiple hosts,
Kubernetes and Redis introduce no current benefit.
Sources: [ASP.NET OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0),
[Compose ordering](https://docs.docker.com/compose/how-tos/startup-order/),
[Compose services](https://docs.docker.com/reference/compose-file/services/).

## Remaining release administration

The owner must choose the public-source license before distribution. It is not replaced by an
invented license here. Numeric/display limits, invite TTL, source deletion (blocked while referenced),
provider scope and recovery policy above resolve the planning details explicitly delegated by spec.

## Approved native v1 scope and SwiftUI architecture

**Decision**: Mandatory SwiftUI app and Share Extension in v1, developed with each backend/web
slice. Swift 6 strict concurrency, deployment target iOS 18+, current stable Xcode pinned during
bootstrap. URLSession async/await and actor-isolated session/network coordination; generated
OpenAPI models behind an adapter; MainActor presentation state. All calculation decisions remain
server-side. Native deletion/favourites/admin settings and all other domain workflows remain parity
requirements even where the owner's native workflow list is abbreviated.
**Rationale**: The owner explicitly supersedes the prior native deferral. Independent native
presentation does not justify independent business logic or a parallel database.
**Alternatives considered**: Web wrapper, reduced mobile subset or post-v1 native delivery violate
the amended scope. Shared UI framework is not requested. No Figma translation is involved.
Sources: [URLSession](https://developer.apple.com/documentation/foundation/urlsession),
[Swift concurrency](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/concurrency/),
[Swift OpenAPI Generator](https://github.com/apple/swift-openapi-generator).

## Native authentication without another auth subsystem

**Decision**: Reuse Identity cookie sessions for native REST with ephemeral URLSession, controlled
cookie renewal and Keychain persistence of the session secret, plus the existing CSRF bootstrap.
Use WhenUnlockedThisDeviceOnly and do not store passwords, share credentials with extension, or
sync credentials through iCloud. Session/membership/security-stamp validity is checked server-side
on every protected request, not only at a long cookie-validation interval. Logout invalidates all
user sessions via security-stamp rotation; this explicit product security default is documented in
both client UIs. Expired sessions require sign-in; no refresh token database or custom token design.
**Rationale**: Native HTTPS clients can use the same first-party cookie API; Keychain secures the
stored credential. Keeping browser and native request semantics identical avoids duplicate flows.
**Alternatives considered**: Identity bearer/refresh or OAuth authorization-code flow would be viable
if external clients or delegated identity were needed, but those requirements are absent. CORS and
SameSite are browser policies, not a native security boundary: native still uses explicit CSRF,
origin-scoped credentials and standard certificate verification.
Sources: [Apple Keychain](https://developer.apple.com/documentation/security/using-the-keychain-to-manage-user-secrets),
[ASP.NET cookie auth](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0).

## Native synchronization and household-wide changes

**Decision**: Foreground iOS conditional GET `/api/v1/household/sync` every second plus immediate
foreground/reconnect refresh; existing Angular SignalR notifications extend to all shared resource
domains. Household ContentRevision and domain revisions increment transactionally for shared writes.
The user-bound ContentRevision/PreferencesRevision ETag avoids refetching unchanged resources;
personal changes invalidate only that user’s devices. Stop on background/logout and back off failures.
**Rationale**: This is the owner's permitted equivalent synchronization approach. It avoids a
third-party Swift SignalR dependency and is trivial at initial two-member scale. No APNs delivery,
background freshness or offline editing promise is added. Two-second foreground target is verified
by cross-client tests; invalidation notifications never overwrite unsaved local edits.
**Alternatives considered**: Swift SignalR package requires additional compatibility/security
maintenance; custom WebSocket protocol or push backend adds infrastructure. No transport changes
the server's household authorization or authoritative calculation behavior.

## Share Extension handoff within platform constraints

**Decision**: Extension-safe URL/plain-text capture into coordinated App Group files, followed by
normal main-app import review. The extension confirms handoff and instructs users to open the app;
it does not depend on forcibly launching the containing app. The app resumes queued input on
foreground, confirms destination/account and uses handoff registration/status plus existing import endpoints with stable retry keys and durable
commit receipts that outlive draft cleanup.
Only the main app holds authentication. Add App Group entitlements to both targets; queue envelopes
contain no credentials and use protected files, explicit cancellation and bounded retention.
**Rationale**: Apple's extension architecture separates app containers and provides App Groups for
shared data. The capture process should remain small and tolerate termination, missing login or
server unavailability. Review and extraction remain in the normal workflow, never auto-save.
**Alternatives considered**: AI/extraction inside extension or unsupported responder-chain launching
are not needed. A platform-supported manually opened containing app completes the handoff without
adding a background job service or an offline recipe database.
Sources: [Apple shared-container guidance](https://developer.apple.com/library/archive/documentation/General/Conceptual/ExtensibilityPG/ExtensionScenarios.html),
[Apple Share Extension guide](https://developer.apple.com/library/archive/documentation/General/Conceptual/ExtensibilityPG/Share.html),
[extension lifecycle](https://developer.apple.com/library/archive/documentation/General/Conceptual/ExtensibilityPG/ExtensionOverview.html).

## Resolved analysis findings A1/I1/I2/C1/A2/D1

**Decision**: Extend the existing single-host registry with one per-user revocation/dispatch gate
and a common revocation application service for all six causes. Revalidate current stamp/activity/
expiry/membership at connect/invoke/send; serialize revocation with dispatch; use the same service
from host-local maintenance, not independent database edits. No custom crypto or distributed locks.
**Rationale**: An open authenticated socket is not a continuing authorization grant; credential
rotation must invalidate it just like membership removal. Already-dispatched frames cannot be recalled.
**Alternatives considered**: Cookie expiry alone, subscription-only checks and membership-only
revocation do not meet the common boundary. A broker/backplane is unnecessary in the one-host topology.

**Decision**: Durable ImportHandoff metadata/outcome and expired-key tombstones outlive 24-hour
review bodies and retry caches; commit and recipe create are atomic. Seven-day expiry is fixed from
capture, bound to household/user, and never extended by retry. Lookup original committed result before
checking draft expiry. Small metadata is retained for installation lifetime, including deleted-result
identity; no raw text is retained by the receipt.
**Rationale**: A lost commit response must not become a second recipe on a day-six retry. A durable
outcome index is required correctness state, not a speculative import audit/history system.
**Alternatives considered**: Merely extending cache TTL to seven days still needs handling at the
expiry boundary and deleted-draft retries. Receipt status/tombstone makes both decisions explicit.

**Decision**: Compound user-bound sync ETag with personal revision; private PersonalChanged sends;
no household invalidation for another member's favourites/preferences. Explicit catalogue-management
UI/tests on both clients and canonical/alias conflict handling. No ingredient bulk merge in v1.
**Rationale**: Household-only ETags hide personal writes; autocomplete does not replace management.
**Alternatives considered**: Global invalidation is unnecessary and leaks personal activity metadata;
implicit alias merging risks ingredient identity correctness.

**Decision**: Use consumed+remainingReserved consistently in spec/contracts/model; retain q only
for display/comparison. MP-SYNC-01 owns the single update latency protocol, with correctness separated
in SC-013 and four Angular/iOS pairings plus reconnect/foreground measurements.
**Rationale**: Removes ambiguous active-allocation terminology and duplicate criteria without changing
approved calculation or performance targets. Backend domain services remain authoritative for clients.

## Verified bootstrap pins

Local bootstrap selected .NET SDK 10.0.401; Angular framework 22.2.4/CLI and build 22.2.5;
TypeScript 6.0.2; supported Node 26.10.0; Xcode 27.2; Swift OpenAPI generator/runtime/URLSession
1.14.3/1.13.3/1.4.0. Node 26 is supported by Angular 22, replacing the initial Node 24 baseline
without changing the application architecture. Exact package versions/lockfiles are authoritative.
PostgreSQL development/Testcontainers image is pinned by digest and reports 18.6. The older
Angular-template dependency advisories were fixed with compatible patched releases; remaining
lint-only moderate minimatch findings are documented in docs/dependency-review.md.

### Patched .NET baseline during implementation

NuGet rejected SignalR.Common 10.0.12 for high-severity CVE-2026-70159 /
GHSA-334f-vvp4-7g6p. Upgrade the SDK to 10.0.403, ASP.NET Core runtime and Microsoft
10.x packages/tools to 10.0.14, preserving .NET 10 and existing architecture. Official release
metadata identifies this SDK/runtime pair; verify the SDK archive SHA512 before local installation.
The temporary local SDK installation does not replace the machine's system SDK. CI uses global.json.
Earlier bootstrap test evidence records the versions used at that time; it does not override this
patched baseline.

Sources: [Microsoft advisory](https://github.com/advisories/GHSA-334f-vvp4-7g6p),
[official .NET 10 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json).

### Native invitation entry for arbitrary self-hosted origins

HTTPS invitation links work in the web client. Its explicit native-open action copies the invitation
link and opens `recipemanager://invite?origin=...` containing only the server origin, not the bearer
invitation token. Native confirms destination/account changes, then the user explicitly pastes and
reviews the invitation before accepting. This supports arbitrary self-hosted domains without claiming
wildcard Universal Links entitlements. The URL handler also validates received full HTTPS invite
links; no link triggers account creation or network import automatically. Pending input and origin
credentials are cleared only after explicit confirmation.


## Planner implementation contract verification

The pinned Swift generator dropped `oneOf(null, ref)` optional request bodies. OpenAPI now marks
those bodies optional and retains the object reference; typed native DELETE confirmations and
actuals resolutions are compile/encoding-tested. Nullable decimal/revision schema transformation
also handles Nullable<T>, preserving string types instead of untyped or double schemas. API decimal
serialization strips storage padding without rounding or exponent notation; original ingredient
text is independent. Schema tests and both generated client builds verify these decisions.

## Shopping source transaction implementation

Reuse the existing household-locked transaction boundary for generation and reconciliation.
Every recipe, catalogue and planner writer takes this lock before updating source revisions,
so all selected source reads remain consistent at READ COMMITTED. This preserves the original
snapshot requirement without adding a nested repeatable-read transaction or another lock system.
Reconciliation recomputes under household/list locks and checks the coarse source counter, source
vector, calculator policy and list revision before applying explicit row choices.

Nullable enum references need the same OpenAPI normalization as optional object references for the
pinned Swift generator. Inline a nullable string enum including JSON null in its enum values, and
normalize referenced objects recursively before copying them. Otherwise nested override exact
quantities/dimensions and an explicit reconciliation basis disappear from generated native types.
Schema assertions and Swift encoding/decoding tests cover the actual/null basis and unresolved
shopping quantities; both generated clients compile without dropped-schema warnings.

### Native Share Extension storage references (implementation preparation)

Apple's [shared-container guidance](https://developer-rno.apple.com/library/archive/documentation/General/Conceptual/ExtensibilityPG/ExtensionScenarios.html)
permits coordinated shared-file access for app extensions and calls for synchronizing access to
an App Group container. Use coordinated atomic envelope persistence; do not share session
credentials or introduce an offline domain database. The current
[NSFileCoordinator documentation](https://developer.apple.com/documentation/foundation/nsfilecoordinator)
also discusses suspension/cancellation behavior, so failed coordination must leave the previous
durable handoff intact and return visible retry feedback. Extension work remains a local capture
operation followed by explicit main-app import review. Apple's
[extension URL-opening reference](https://developer.apple.com/documentation/foundation/nsextensioncontext/open(_:completionhandler:))
does not justify relying on forced containing-app launch from a Share Extension; retain the
approved open-app continuation flow. These references refine implementation choices without
changing the approved v1 Share Extension scope.

The bounded import-text limit is 100 KiB UTF-8; URLs retain the existing 2,048 UTF-16-unit backend limit. These are transport/input safety bounds, not changes to supported import methods. The device queue uses one coordinated document with at most twenty envelopes, protected staged writes, file/directory synchronization and atomic rename; no local domain database or persistent NSFilePresenter is required. Ordinary coordinated write options apply because this updates one document even when atomic replacement is used internally (as described in the installed Foundation NSFileCoordinator header).


## Import implementation evidence

AngleSharp 1.9.1 is pinned for standalone HTML parsing with no resource loader or script execution.
JSON-LD traversal is local: remote @context/@id values do not trigger requests. Website requests
use HTTP/1.1, manual bounded redirects, strict decoded-body reads and a ConnectCallback that resolves,
validates and pins public addresses while retaining the original host for ordinary TLS validation.
The conservative policy denies private/special/transition ranges, IPv6 scoped addresses and known
metadata targets. Mixed public/private DNS answers fail closed. Real isolated HTTP tests cover
compressed/chunked size enforcement, Host preservation, absent cookies and no image requests.

The compatible AI adapter uses JSON-object Chat Completions and local strict schema validation.
It requests exact source spans rather than authoritative generated quantities. Every ingredient
line must occur in the pasted source; the shared deterministic parser derives quantities/units.
Unknowns remain flagged, and invented source lines or unexpected fields fail safely. Provider
settings are read through an authenticated household contract; decrypted credentials never enter
REST DTOs. A validated fake adapter exercises the same response parser without paid calls.

Source/review JSONB and the existing retry store suffice; no broker or external-call transaction is
added. Retry preflight avoids repeat provider work after draft persistence. Concurrent first calls
may both run extraction, but serialize persistence to one draft; exactly-once external processing is
not promised. Durable recipe commits remain idempotent for the complete handoff lifetime and beyond.

Primary references: [AngleSharp package](https://www.nuget.org/packages/AngleSharp/1.9.1),
[parser basics](https://github.com/AngleSharp/AngleSharp/blob/devel/docs/general/01-Basics.md),
[Schema.org Recipe](https://schema.org/Recipe),
[IANA IPv4 registry](https://www.iana.org/assignments/iana-ipv4-special-registry),
[IANA IPv6 registry](https://www.iana.org/assignments/iana-ipv6-special-registry),
[.NET ConnectCallback](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0),
[official OpenAI Chat Completions reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create).
