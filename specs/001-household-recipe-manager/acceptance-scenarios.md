# Model Review: Automated Acceptance Scenarios

Design-only executable-test specifications. No test code has been implemented or executed.
Use xUnit pure-rule tests and PostgreSQL/Testcontainers API integration tests; amounts below
are exact. Repeat API cases with a second authenticated member and cross-household denial checks.

## AC-01 Partial consumption retains reservation (FR-023–024)

Given actual production 4 and same-meal consumption 2, with allocation q=2,c=null,r=2,
when confirming absolute consumption 1.5 and explicitly retaining r=0.5,
then c=1.5,r=0.5, availability=4-2-1.5-0.5=0; q is not subtracted again.
When confirming absolute consumption 2 with r=0, availability remains zero, not -2.
Repeating a correction with the same values never adds another consumption deduction.
Automate pure invariants and persisted API reload.

## AC-02 Partial consumption releases remainder (FR-024)

Given the same source/allocation, when confirming c=1.5 and releasing r=0,
then availability=0.5, q stays 2 for comparison and the scheduled meal remains visible.
A new 0.5 allocation succeeds, but any additional allocation fails. Confirming that new allocation
changes its reservation to consumption without changing total commitment. Automate pure/API tests.

## AC-03 Release without consumption (FR-022–025)

Given available source capacity 2 and q=2,c=null,r=2, when explicitly releasing the reservation,
then r=0,c=null, available=2. Reload, repeated release and shopping generation must not restore r
from q. Keep scheduled meal visible as unreserved/released. Subsequent explicitly re-reserving 1
leaves available=1; a stale release request fails revision check. Automate rule/API tests.

## AC-04 Correct actuals after partial consumption (FR-024–025)

Given K=2,c=1.5,r=0.5, when correcting production so K=1,
then conflict includes affected meal and available shortfall; no source/allocation changes commit.
Explicitly resolving to a valid consumption/reservation state applies atomically. Clearing actual
consumption requires an explicit new remainder, not automatic restoration of q. Automate transaction
rollback and stale correction tests against PostgreSQL.

## AC-05 Recipe edited after scheduling (FR-026)

Given recipe v1 has 100g rice for two, a cook event snapshot v1 plans four, and a generated list
contains 200g, when live recipe v2 changes to 150g for two and changes instructions/standard,
then event still displays v1 and generating/reconciling its meal-range requirements still yields
200g. A manual selection of live v2 for four yields 300g. Explicit refresh preview/apply adopts v2,
increments event revision, retains portions/leftover links and leaves existing list at 200g.
Separate reconciliation previews 300g. Changing event or recipe after refresh preview rejects
apply without partial refresh. Automate snapshot serialization and API tests.

## AC-06 Unknown plus known ingredient quantity (FR-013–014,017,029–032)

Given recipes contributing 5g salt and `salt to taste`, when generating a list,
then it shows `Salt — 5 g` and a distinct `Salt — to taste (amount unresolved)` row in the category,
with source detail and unresolved count 1. The known amount is not represented as a complete total.
Marking known 5g purchased or overriding it to 10g does not hide/resolve/purchase the unknown row.
Two unknown sources remain inspectable, neither becomes zero nor an invented gram amount.
Reconciliation preserves their explicit user states and detects added/removed unknown contributions.
Automate calculator/DTO tests and two-session browser display checks.

## AC-07 Shopping before and after optional actual confirmation (FR-027–028)

Given cook snapshot uses 100g rice per two servings and planned preparation four with no actuals,
when generating a list, then rice=200g and planning/shopping succeeds without any confirmation.
After actual preparation six is confirmed, existing list stays 200g, normal new generation is 200g,
and explicit actual-basis reconciliation previews 300g. No list changes before acceptance.
A leftover meal contributes zero in every basis. Automate generation/reconciliation API tests.

## AC-08 Concurrent allocations from two members (FR-022,034)

Given two active members and source remaining capacity 2 at revision V, use separate database
connections/transactions with a barrier to concurrently request 1.5 each against V.
Then exactly one succeeds; the other receives stale-revision or capacity conflict. Persisted sum
is 1.5, remaining=0.5, with no orphan scheduled meal from the failed transaction. Retrying 1.5
against the new revision fails capacity; retrying 0.5 succeeds. Repeat with one cross-household
principal and prove denial without any source change. Use real PostgreSQL, not mocked locks.

## AC-09 Stateless preview/apply and preservation (FR-031–033)

Given baseline rice=200g, user override=250g, purchased=true, a manual item and a removed generated
item, preview new requirements rice=300g. Keep-user preserves 250g/purchased/manual/tombstone;
accept-new explicitly changes rice to 300g and changes purchased only if user explicitly chooses.
Server recomputes totals, rejecting forged choices/amounts. Apply updates baseline/list once.
Concurrent shopping edit causes 412; concurrent source edit causes 409; neither partially applies.
Adding/moving/deleting another cook into/out of selected range bumps household source revision and
invalidates preview, including when its ID was absent from previous source vector. Repeating apply
with old ETag fails without duplicate effects. Test rollback and interleavings in PostgreSQL.

## AC-10 Arithmetic simplification guard (FR-010,012–014)

Given quantity 1/3g or quantity 1g scaled from three servings to one, when three identical
contributions consolidate, then result is exactly 1g regardless of order; original fraction text
survives edit/save. AU cup plus US cup gives exactly 486.588ml before display rounding. Decimal
portion 1.25 persists exactly; numeric(78,0) is not required anywhere in relational schema.
Known-plus-unknown never treats unknown as zero. Automate pure properties and persistence round trips.

## Product impact

These scenarios clarify approved behavior; no approved requirement is removed or relaxed.
Decimal-only approximation would require an explicit FR-014 precision/tolerance amendment and
updated acceptance fixtures; it is not adopted. Stateless preview is still a reconciliation proposal
in the specification's user-facing sense, not a new persistent domain requirement. Coarse source
revision invalidation can require a fresh preview after unrelated changes; this preserves safety
without adding queues, distributed locks, proposal storage or a history ledger.

## Mandatory native v1 acceptance additions

All AC-01–10 also run through native workflows against the shared API where applicable. No domain
formula is implemented independently in Swift. These additions are design-only test requirements.

### AC-11 Bidirectional full-workflow parity (FR-045–049,052)

Run every core Story 1–7 fixture using iOS writes and Angular observation, then reverse clients.
Include recipe deletion/archive/favourite, aliases/units, all import review fields, calendar/actuals,
partial/released leftovers, unresolved shopping amounts, reconciliation and admin settings.
Expected saved state/errors match exactly. Swift unit tests isolate view-model state/selection and
format/presentation only. API integration tests send native cookie/CSRF/ETag requests. XCUITest
covers the native critical journey; Playwright and native API observers verify shared results.

### AC-12 Secure native lifecycle and role enforcement (FR-034–035,040–049)

Configure HTTPS server, run setup/invitation/login in native, restore session via Keychain after
termination, and validate current user. Member admin operations and cross-household requests fail.
Removal/password reset/all-session logout revokes native and web access. Expired cookie requires
login, never silent resubmission of password. Account/server switch does not send former credentials
or pending input to another origin. Inspect UserDefaults/App Group/logs for absence of secrets.
Test serialization of concurrent session-renewal responses and no cross-origin redirect leakage.

### AC-13 Foreground synchronization (FR-049)

While native and web are active, changes in either appear in the other within the two-second
reference target for 95% of samples. Native conditional polling returns 304 when unchanged and
runs as one task. Background stops polling; web updates during suspension are fetched on foreground.
Network reconnection resumes without manual refresh, while dirty forms retain stale-revision conflict
handling. Membership revocation returns unauthorized rather than 304. No APNs/offline mode required.

### AC-14 Share Extension URL and text handoff (FR-050–052)

From signed test host export Safari-style URL and ChatGPT-style plain text to Recipe Manager's
Share Extension. Confirm local handoff, terminate containing app and extension, reopen app,
select/confirm installation/account and invoke URL or AI text import. Edit extracted quantities,
uncertainty and source standard, then save. Assert zero recipe before commit, exactly one afterward,
and same recipe visible on web. Extension process performs no extraction or AI call.

### AC-15 Handoff failures and resumption (FR-051–052)

Test cancel, unsupported media, mixed URL/text choice, over-limit input, full queue, signed-out
app, unavailable backend, interrupted file write, repeated open and expired draft. No silent save,
input loss before acknowledged handoff, duplicate commit or destination/account crossover. Expiry
is disclosed, explicit discard clears payload, protected files contain no session credential.
Run repeatable tests with native share-host fixture; record physical-device Safari and ChatGPT
compatibility separately. Native VoiceOver and Dynamic Type must complete import review and save.

## Analysis-remediation acceptance additions

### AC-16 All-cause session revocation and broadcast races (FR-053)

Parameterize logout, password change/reset, membership removal, operator account deactivation and
explicit security-stamp rotation. Establish two valid sockets and REST sessions for the user.
Pause a queued send before dispatch, race it with revocation, and assert no dispatch starts after
the durable revocation boundary. Prior dispatches drain/cancel before that boundary; already-sent
transport frames are not claimed retractable. Connections abort before revocation success, old
cookies cannot invoke/reconnect/use REST, expired tickets fail and valid fresh login with active
membership may reconnect. Check personal and household notifications. Failed revocation returns no
false success; process restart honors persisted state. Operator maintenance uses the same running
host barrier or stops the host, never a silent concurrent Identity edit. Run PostgreSQL and actual
SignalR integration tests with deterministic barriers and no arbitrary timing sleeps.

### AC-17 Durable handoff replay after draft/cache expiry (FR-054)

Register a captured handoff, review and commit it; drop the success response. Advance fake time
six days and run 24-hour draft/cache cleanup. GET status and repeated equivalent commit return
the original recipe ID/revision/time and recipe count stays one. Race two initial commits on
separate database connections: exactly one recipe/immutable receipt. Changed-body commit returns
409 and cannot update the committed recipe. Open expired handoff returns 410 with no new recipe;
Committed after expiry still returns its original outcome. Delete that recipe deliberately and
retry: return committed_recipe_deleted, never recreate. Account/household mismatch denies result
lookup. Backup/restore retains receipt and expired tombstones; expired draft may be recreated only
for an Open unexpired handoff. A bound draft cannot bypass receipt commit through normal endpoints.

### AC-18 Personal cache validation and cross-device sync (FR-055)

User A has Angular and iOS sessions; B has a separate session. Initial unchanged compound validators
produce 304 after auth checks. A changes favourite/profile preference: A's other device gets 200
with incremented own preference revision, B retains unchanged ETag/304 and receives no PersonalChanged.
A's two devices converge without refresh, using personal event/web and conditional poll/native.
Household recipe edit invalidates both users. Invalid/expired/revoked credentials yield 401 before
304. Verify private cache headers and origin/user-partitioned client caches; dirty preference edits
show a stale revision conflict instead of being overwritten. Explicit user binding prevents cache
reuse across login/server/account changes.

### AC-19 Catalogue management parity and safe corrections (FR-011,045)

In Angular and SwiftUI separately, browse/search canonical names and aliases, create an ingredient,
edit/rename it, add/edit/remove alias and assign/change category. Duplicate normalized canonical
or alias names, including concurrent requests, return name_conflict with no partial change. Old
canonical name is retained as alias only by explicit choice. Recipes keep the same IngredientId;
cooking/list snapshots keep original details. Referenced delete returns ingredient_referenced,
unused deletion removes only owned lookups, category deletion moves references to uncategorised.
No implicit ingredient merging or bulk merge workflow. Exercise both roles and tenant isolation.

### AC-20 Released reservation reallocation (FR-023–025)

Source actual 4 prepared/2 eaten has q=2,c=1.5,r=0.5, commitment=2, availability=0. Release r=0:
q remains 2,c remains 1.5, availability=0.5. New meal reserves 0.5, availability=0. Releasing old
r again/reloading does not change consumed or available. Correct source capacity below committed
sum: entire transaction rejects with affected allocations unless explicit valid resolutions apply.
Race reallocation/correction on separate connections and preserve nonnegative availability. Retain
planned intention for comparison but never subtract it as a third amount. Both clients display
planned, consumed, reserved and available using authoritative server responses.

### AC-21 Unified shared-update and recovery benchmark (SC-005,013)

Implement MP-SYNC-01 from spec.md once. Record 100 warmed samples per pairing/mode for all four
Angular/iOS pairings and connected/reconnect/foreground modes, on the documented host/network.
Include recipe, meal and shopping changes plus same-user personal-cache correctness. Every cell's
p95 meets two seconds under SC-005; SC-013 verifies accepted-state convergence, no duplicate writes
and no dirty-edit loss. Timing uses the shared controller's monotonic clock and rendered-revision
acknowledgement. Report cells separately, retain raw samples, and make no background delivery claim.
