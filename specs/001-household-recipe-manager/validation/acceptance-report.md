# Acceptance coverage and remaining gates

T132's continuous automated journeys pass in both directions. This is not release approval:
complete story parity, accessibility, physical-device, reference-host performance and publication
gates remain open. The standalone Angular journey and ten standalone browser acceptance tests
also pass across the final validation batches. See accessibility.md for the unfiltered native audit.

## Product success criteria

| Criterion | Evidence and current limit |
| --- | --- |
| SC-001 critical scenario | Shared domain and independent client stories exercised; continuous browser and signed native-first/web-first cross-client journeys pass; independent two-person observed acceptance pending |
| SC-002 deterministic calculations | Backend unit/integration suites pass; originals, AU/US measures, aliases and unresolved amounts covered |
| SC-003 allocation safety | Real PostgreSQL races, partial consumption, release and corrections pass; source locks and aggregate revisions enforced |
| SC-004 search/calendar performance | Functional search/calendar pass; 500-recipe reference-host p95 measurement pending |
| SC-005 synchronization performance | Real bidirectional shopping observer and foreground correctness checks pass; four-pairing 100-sample distributions pending |
| SC-006 reconciliation preservation | Backend, independent clients and both continuous cross-client reconciliation journeys pass |
| SC-007 household isolation | Full isolation/revocation integration suite passes, including cached validators, imports, images and hub races |
| SC-008 observed household usability | Automated ingredient/planner workflows pass; two-person observed usability acceptance pending |
| SC-009 deployment recovery | Actual TLS Compose restart/upgrade/separate-volume restore passes; clean reference-host acceptance pending |
| SC-010 accessibility | Eighteen web screen/width automated checks and keyboard entry pass; complete keyboard/screen-reader/native acceptance pending |
| SC-011 core client parity | Both clients and continuous bidirectional critical journey pass; complete Story 1–7 parity matrix remains open |
| SC-012 share capture/review | Signed controlled host URL/text and review/save journeys pass; physical Safari/ChatGPT pending |
| SC-013 recovery | Incremental reconnect/foreground/auth/dirty-draft checks pass; MP-SYNC-01 full measurement pending |
| SC-014 native accessibility | Largest-text authentication/title/calendar subset passes; full audit, critical journey, VoiceOver and minimum/current OS gates remain open |

## Acceptance scenarios

| Scenario | Existing automated evidence | Remaining end-to-end limit |
| --- | --- | --- |
| AC-01 partial consumption/reservation | LeftoverResolutionTests, planner web/native journeys | Continuous bidirectional critical sequence now passes; exhaustive story matrix remains separate |
| AC-02 partial consumption/release | LeftoverResolutionTests, planner journeys | Continuous bidirectional critical sequence now passes; exhaustive story matrix remains separate |
| AC-03 release without consumption | Leftover resolution/concurrency suites | Continuous bidirectional critical sequence now passes; exhaustive story matrix remains separate |
| AC-04 corrected actuals | LeftoverResolutionTests, planner journeys | Continuous bidirectional critical sequence now passes; exhaustive story matrix remains separate |
| AC-05 recipe edit/snapshot | RecipeSnapshotPersistenceTests, planner journeys | Both continuous cross-client snapshot refresh journeys pass |
| AC-06 known/unknown quantities | ShoppingContractTests, shopping journeys, deployed workflow | Both continuous cross-client review journeys pass |
| AC-07 planned shopping/optional actuals | Shopping integration/deployed workflow | Continuous cross-client confirmation |
| AC-08 concurrent allocation | ConcurrentAllocationTests with real PostgreSQL | Domain safety proven; final client conflict UX matrix |
| AC-09 preview/apply preservation | ShoppingContractTests, web/native shopping journeys | Both continuous cross-client reconciliation journeys pass |
| AC-10 arithmetic guard | Exact quantity/unit/scaling backend suites | Shared backend remains authoritative |
| AC-11 full-workflow parity | Independent client stories and genuine bidirectional shopping | Continuous critical journeys pass; full Story 1–7 matrix remains T092 |
| AC-12 secure native lifecycle | Native origin/startup/session tests, FullIsolationTests | Final signed device/OS matrix |
| AC-13 foreground sync | NativeSyncUITests, CrossClientUITests, API/unit recovery suites | Full MP-SYNC-01 matrix |
| AC-14 URL/text share | Signed ShareHandoffUITests and ShareImportUITests | Physical Safari/ChatGPT |
| AC-15 handoff recovery | Handoff/import tests and signed ImportRetryUITests | Physical host/device acceptance |
| AC-16 revocation/races | SessionRevocationTests and FullIsolationTests | Release security recheck |
| AC-17 durable retry | HandoffCommitTests, browser/signed lost-response recovery | Full handoff lifetime enforced by server-time integration tests |
| AC-18 personal validators | PersonalRevisionTests and native/web sync/state tests | Four-pairing workload measurements |
| AC-19 catalogue parity | Catalogue API/web/signed-native acceptance | Final core parity gate |
| AC-20 released reallocation | Real allocation tests and planner UI scenarios | Both continuous critical journeys pass |
| AC-21 benchmark | Incremental correctness samples only | Entire 100-sample/pairing/mode/reference-host measurement pending |

The backend baseline is 336 passing tests; the latest focused FullIsolationTests rerun passes both
cases with no skips. Angular has 59 passing unit tests plus separately recorded browser journeys.
Native unit/handoff baseline is 91 tests, with signed story journeys recorded in the story reports.
Counts are suite evidence, not a count of complete product success criteria.

See US1–US9.md, security.md, accessibility.md and docs/release-checklist.md. Do not close a task
because this table contains a test name; retain actual run evidence and every named acceptance gate.

## Continuous browser journey (2026-11-04)

`npm --prefix tests/e2e test -- critical-journey.spec.ts` passes with a deterministic isolated AI
provider and real HTTPS API/PostgreSQL. Paste/review corrects the source standard and quantities,
then saves collection/tag assignments. Monday prepares four/eats two; Tuesday explicitly reserves
two leftovers; Wednesday cooks a second recipe. Before any actual confirmation, shopping contains
exactly 500 g consolidated rice plus independently visible unresolved salt. The user purchases and
overrides rice to 600 g and adds bread. Editing the recipe retains the original 150 g cooking
snapshot until explicit refresh. Refresh changes the baseline to 550 g; reviewed reconciliation
preserves the 600 g override, purchased state, bread and unknown requirement.

This run uses Angular throughout. It does not close the required native Share Extension or
bidirectional continuous journey. An immediate checkbox assertion in the initial test was corrected
to await the accepted asynchronous server state, matching the existing shopping acceptance tests.

## Signed continuous cross-client evidence — 2026-11-04

`scripts/test-ios-households.sh CriticalJourneyUITests` passes with no skips. Native authentication
passes in 137.1 seconds, native-first in 345.7 seconds and web-first in 282.5 seconds. The complementary
Angular controller passes both cases in 13.2 minutes, and the final Angular profile observer passes
in 806 ms. These are correctness journey durations, not MP-SYNC-01 performance distributions.

- Native-first starts with the signed Share Extension and credential-free App Group capture. The
  Member confirms the destination, reviews AI text, corrects quantities/units/measurement standard,
  and assigns a collection/tag. Angular observes the saved recipe and manually creates Wednesday's
  recipe. SwiftUI schedules four prepared/two eaten Monday, two explicit Tuesday leftovers and
  Wednesday cooking. Angular generates exactly 500 g rice plus unresolved salt before actuals.
- Web-first imports/reviews/organises through Angular. SwiftUI observes and edits the saved recipe,
  then performs the same planning workflow in a separate week. Angular generates the shared list.
- In both cases SwiftUI purchases/overrides rice to 600 g and adds numeric bread. Angular observes
  these live changes, then edits the recipe to 175 g. The original cooking snapshot stays 150 g
  until SwiftUI explicitly compares and refreshes it. Angular previews/applies reconciliation to a
  550 g baseline, retaining the 600 g override, purchased state, bread and unresolved salt. SwiftUI
  observes the new 175 g contribution without a manual list refresh; final exact values agree.

The controller is loopback-only TLS with a per-fixture random token and bounded request size. It
operates real Angular controls; SwiftUI performs the complementary writes. Backend APIs remain
authoritative for all calculations and reservations. The two cases use distinct weeks so neither
case contributes requirements to the other. No paid provider is used.

The first native-first run exposed a test tap below the keyboard. The input helper now checks the
input centre against keyboard/tab-bar bounds and waits for input readiness. The signed catalogue
regression separately passes after preventing refresh from disabling an open ingredient editor.
No arithmetic, allocation, snapshot or reconciliation rule changed to make these tests pass.
