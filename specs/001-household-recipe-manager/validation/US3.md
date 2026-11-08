# Shopping verification — independent acceptance complete

T070–T085 are complete: generation, edit state, immutable snapshots, stateless reconciliation,
production notifications and generated contracts have independent backend acceptance coverage.
The Angular shopping workflow passes independent browser acceptance. Native shopping UI acceptance and the added catalogue correction/snapshot regression pass; cross-client live observation closes after US8 synchronization.

| Gate | Current evidence |
| --- | --- |
| Complete backend | 231 tests pass: 81 unit, five architecture, 145 PostgreSQL/API integration; no skips or exclusions |
| Calculation/edit state | Twelve unit tests pass: compatible units, aliases, distinct identities, AU/US factors, exact thirds, no leftover contributions, planned versus actual basis, separate unknown quantities, immutable baselines and explicit reconciliation choices |
| PostgreSQL schema | Three persistence tests pass: upgrade preservation, no model drift, retained unknown rows, overrides/purchases/tombstones, tenant FKs and restrictive ingredient references |
| Personal sync | Three integration tests pass: same-user cross-device invalidation, another member's unchanged 304, user-only committed invalidations, shared invalidation, private user-bound ETags, logout and expiry before conditional evaluation |
| Shopping API contracts | Seven pass: equivalent generation routes, unresolved rows, optional actual-basis reconciliation, stale insertion/removal/list rejection, preserved manual/purchased states, concurrent edits, retry recovery, household isolation and retained recipe/ingredient references |
| Production SignalR | Ten pass: subscription filtering, private updates to both own devices, list deletion, all six revocation causes, fresh/old-cookie reconnection, foreign Origin rejection, idle expiry and recovery of missed updates through sync |
| Generated contracts | OpenAPI, TypeScript and Swift regenerate without warnings; 43 native and 36 Angular tests pass; nullable basis/dimension/nested-override regressions pass |
| Client UI/live sync | Angular shopping and two-session purchase/draft-preservation journey passes; native shopping acceptance and catalogue/snapshot coverage pass; cross-client live shopping gate remains open |

The common calculator consumes immutable recipe snapshots for both selection routes, scales exact
quantities by prepared servings and uses frozen canonical factors. Known requirements group by
canonical ingredient identity/dimension. Unknown requirements retain stable source/ingredient-row
keys, original text and preparation notes as independent rows. A known subtotal exposes an
additional-unresolved flag; it cannot absorb or hide an unknown requirement.

Snapshot items keep generated baseline JSON separate from override/manual values and purchased/
removed flags. Unknown overrides explicitly retain null effective quantities rather than falling
back to a known baseline. The migration has no numeric(78,0) columns, proposal table, broker or
dependency index. Reconciliation validates all row choices before mutation and recomputes under
the common household/list lock. Increased purchased requirements require an explicit purchase-state
decision. Shared and personal notifications pass through the existing per-user revocation fence;
post-commit delivery failures do not retry or roll back accepted mutations.


Angular shopping acceptance (T081): category-grouped known and independent unresolved rows, planned meal-range generation before actual confirmation, editable quantities, manual items, purchased state and explicit reconciliation are implemented. The isolated Chromium journey passes in 3.7 seconds (13.7 seconds including fixture startup). It verifies an explicit leftover allocation adds no shopping requirements, a 12.5 g purchased override survives a baseline increase from 10 g to 15 g, an unresolved “to taste” row remains unpurchased and visible, and a manual item survives reconciliation. A separate authenticated session changes a purchased state; the original page reflects it within the two-second assertion and preserves a dirty manual draft during a later remote edit.

Angular state/sync checks: 48 tests pass, including conditional 304, compound personal-preference revision changes, late-event suppression after logout, reconnect/resubscribe, coalesced reads and preservation of accepted action notices during background recovery. Native shopping forms, state tests and UI acceptance pass. T082/T085 are complete. T083/T084 are complete: production hub security/recovery tests and the shopping two-session journey pass; final recipe, planner and shopping Chromium regressions also pass after wiring household/personal invalidation. Angular production build, lint and formatting checks pass. Native shopping unit tests and UI acceptance pass.

Native shopping acceptance passes against the isolated authenticated household fixture: planned four portions produce 10 g before actual cooking confirmation, the explicit two-portion leftover adds no requirement, purchase/12.5 g override/manual item persist through a 15 g baseline review, and the unresolved original row remains separate and unpurchased. The final native-to-Angular profile observer also passes in 996 ms. All 48 Swift unit tests pass, including stale shopping writes retaining entered fields and the original opaque ETag. The additional catalogue correction/reference regression passes in both clients: ingredient renaming preserves the saved shopping label and referenced deletion is rejected.

## Acceptance mapping

| Scenario | Automated evidence |
| --- | --- |
| AC-06 — known plus unresolved | Shopping calculator and API fixtures retain independent unknown rows; Chromium and SwiftUI purchase/edit journeys keep the original unknown row visible and unpurchased beside a purchased known override |
| AC-07 — optional actual confirmation | API fixtures verify planned generation before confirmation, unchanged saved snapshots after confirmation, all-source confirmation before actual-basis review and explicit application; both client journeys generate from unconfirmed planned cooking |
| AC-08 — concurrent leftovers | US2 PostgreSQL concurrency fixtures verify a single successful allocation against one source version, failed-write rollback, retry capacity and tenant denial; shopping calculation fixtures independently exclude leftovers |
| AC-09 — preview/apply preservation | Unit/API fixtures cover keep-user/accept-new choices, explicit purchase decisions for increases, manual items, tombstones, stale list/source vectors, inserted/removed sources and transactional rejection; both client journeys retain an edited purchased quantity and manual item through review/apply |

These independent story checks do not replace the final MP-SYNC-01 reference-deployment
performance matrix or the US8 native foreground/reconnect observer gate.

Final native fixture passes authentication and shopping with no skips, including the catalogue/snapshot extension. Its final native-to-Angular profile observer passes in 1.0 seconds. T070–T085 are checked. Native automatic shopping observation remains the explicitly named US8 integration gate; no release-wide performance or parity claim is made here.

US8 integration gate closed: the combined signed fixture passes full shopping acceptance with native foreground synchronization enabled, plus genuine Angular/native bidirectional shopping observation and dirty-draft/background-return recovery. No skips. See US8.md for the remaining final release-wide performance/parity gates.

## Reconciliation review race — 2026-11-05

Angular now displays the backend's actual default reconciliation choice when options are rendered dynamically. A delayed source hint preserves the user's explicit review of the existing preview; apply still submits its exact source/list revisions and the backend rejects genuine conflicts transactionally. A successful apply clears only hints represented by its operation; notifications arriving during apply remain visible. Two state regressions cover harmless delayed hints and genuinely stale rejected revisions, preserving purchased state. Angular now passes 63 tests. The real shopping acceptance passes (3.7 s), and the full critical import/organise/plan/leftovers/shop/reconcile journey passes (6.8 s). Browser tests await the real save closure and preview response rather than interacting with a prior in-flight form. No API, migration, quantity or permission rule changed.

The validated source was packaged as `recipe-manager:reconciliation-validation` and applied to the
existing owned LAN fixture on 2026-11-05. Normal HTTPS setup status and deployment health pass,
and the database, image, key and proxy volume attachments remain identical. A private PostgreSQL
backup was taken before upgrade. This deployment check does not replace the remaining full
cross-client performance qualification.
