# Native synchronization and parity — implementation in progress

T086–T091 implementation and incremental synchronization gates pass. Native foreground polling and scene/feature invalidation are
implemented and verified through signed UI observation. This is not a final
native parity or reference-deployment performance report.

| Gate | Evidence |
| --- | --- |
| Swift unit/state tests | 59 pass across 22 suites; single polling task, conditional 304, account-scoped compound validator, personal-only changes, background cancellation/late reply suppression, actual retry backoff, foreground refresh, unauthorized account clearing and dirty shopping draft/original-ETag preservation |
| Native-style API integration | Three PostgreSQL/API tests pass: explicit cookie/CSRF transport, shared exact business results, list preconditions, fresh-login absolute ticket renewal, original-ticket expiry, membership removal and security-stamp rejection before 304 |
| Angular sync/personal tests | 50 pass across 13 suites, including initial retry/reconnect/resubscribe, foreground refresh, user-only changes and account-specific validator/late-event isolation |
| Existing independent client stories | US1/US2/US3 acceptance passes; shopping snapshot, unknown amount, leftovers and reconciliation invariants remain backend-owned |
| Live native observer | Signed authentication, genuine Angular/native shopping observers, native foreground/draft/background-recovery and full shopping UI regression pass with no skips |
| Genuine cross-client UI harness / final parity | T092–T093 pending; API-driven native observer checks alone do not close the four-pairing MP-SYNC-01 matrix |

Sessions have a fourteen-day absolute lifetime; renewal here means an explicit fresh sign-in,
not a refresh-token endpoint or sliding cookie extension. Native synchronization uses a single
foreground structured task and the existing authenticated conditional REST endpoint. It stops on
background/logout, uses bounded jittered backoff and restarts immediately after foreground return
or a reachability recovery hint. Reachability never gates local-server access.

Visible features refetch accepted server state. Shopping keeps dirty forms and their original
validators on remote changes, deduplicates parent/sheet notification handling and refreshes on
returning to a tab. Profile/password/provider fields stay intact while dirty. Recipe serving text
is preserved while an old computed preview is invalidated. Existing cooking-event editor snapshots
and leftover allocation rules are not recalculated by the client.

Lifecycle regression work: two additional tests first reproduced missing polling after account
restoration under an already-bound session identity and missing visible-state refresh when a
foreground conditional read returns 304. Both fixes now pass; 59 native tests pass across 22
suites. Restarts explicitly revalidate visible resources even with unchanged metadata, closing
the race between accepting a revision and suppressing UI delivery during background cancellation.
Ordinary unchanged foreground polls remain silent. An unconditional root Group disappearance
hook was removed so view transitions cannot deactivate the authenticated scene; scene phase and
session identity own polling lifetime. The combined signed UI run passes, closing T089–T091.

The genuine cross-client test passes in both directions: SwiftUI purchase → Angular observer, Angular purchase → SwiftUI observer (two-second receiver assertion). Native live observation separately passes same-household changes, retained manual draft text and two-second foreground recovery. The final profile observer passes in 850 ms. These are incremental correctness checks; the 100-sample/four-pairing/reference-deployment MP-SYNC-01 gate remains pending. T092/T093 remain explicit final parity gates dependent on imports, organisers and deployment, rather than deferred native feature implementation.

## Continuous cross-client acceptance — 2026-11-04

T132 passes both signed continuous entry directions with the real Share Extension, Angular writer/
observer and SwiftUI writer/observer. The same backend calculates 500 g planned requirements and a
550 g explicitly refreshed/reconciled baseline, retaining a 600 g override, purchased state, bread
and unresolved salt. Native-first takes 345.7 seconds; web-first 282.5 seconds. No skips. See
validation/acceptance-report.md for every SC/AC status. This closes continuous critical integration,
not the exhaustive FR-001–044 parity matrix in T092 or manual/device/performance gates in T093.

Native title rendering also measures 20.333 points at normal size and 125.333 at the actual largest
system accessibility size; authentication/invitation/profile/calendar subsets pass at that size.
The unfiltered four-tab audit still has 12 findings. iOS 18.0 runtime downloads are unavailable in
the current Xcode catalogue, so minimum-OS evidence remains absent and is not inferred from iOS 27.

## Complete functional orchestration — 2026-11-04

All thirteen suites in `scripts/validate-release.sh --native-parity` pass in one sequential command,
with signed isolated fixtures, setup/invitation/profile checks, Angular observers and no skips.
This includes imports, handoffs, delayed retry, organisers, leftovers, shopping and both continuous
critical entry directions. Additional assertions for ingredient/group operations, custom slots,
calendar move/duplicate/delete, selected-recipe shopping and images are being validated before T092
closes. The unfiltered audit, physical/minimum-OS and performance gates remain separate and open.

## Physical resumption and transport regression

The designated signed iPhone passes the complete planner journey (293.731 s) and shopping journey
(112.981 s) against the preserved isolated LAN server. Caddy API compression had rewritten opaque
ETags; excluding API encoding fixes conditional writes without relaxing backend validation.
Recipe editing needs its unique-title rerun after an ambiguity from retained test recipes; that
retry built but was blocked by device locking before tests started. Native unit/handoff baseline
is 94 passing tests (75 application, 19 handoff). The expanded simulator matrix currently fails
its catalogue input case; a scroll-surface test-helper correction is pending validation. Automatic
approval review capacity was restored and the focused physical/simulator retries resumed. See native-parity.md for
precise evidence and remaining gates; no full native release acceptance is claimed.

The focused physical recipe retry now passes in 130.464 s with a 93.744 s network prerequisite and
`TEST EXECUTE SUCCEEDED`. Combined with the individually recorded planner and shopping passes,
all three core device workflows have passing evidence. Real share providers, VoiceOver, minimum
OS, the latest catalogue regression and the expanded sixteen-suite matrix remain open.

The focused catalogue regression now passes without skips after correcting test interactions for
clipped form content. Its Angular observer passed in 894 ms. The expanded sixteen-suite matrix
has restarted with retained evidence; T092 remains open until it and the operation audit pass.

Final current-source native unit recheck (Xcode 27.2, iOS 27.0 simulator) passes 76 application tests across 27 suites and 19 ShareHandoff tests across three suites. This includes the current presentation changes; no physical iPhone access was used. The final generic app/extension build signs successfully. These results do not close unfiltered accessibility or minimum-OS acceptance.

## Current physical recheck — 2026-11-05

The connected designated iPhone (iOS 27.0.1) passes the current signed recipe-editing/lifecycle journey in 168.677 s, plus the real HTTPS/local-network prerequisite in 82.735 s. Exact field replacement, serving preview, favourite, duplicate, archive and delete operations pass without resetting existing household data. The run installs the current signed application and test runner; the separate unfiltered physical accessibility audit remains failing.
