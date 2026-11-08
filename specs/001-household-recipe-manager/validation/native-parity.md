# Native functional parity matrix

## In-progress expanded rerun — 2026-11-05

The current sequential rerun has passed catalogue management (224.928 s), structured recipe
editing/lifecycle (112.649 s), custom slots and other-meal lifecycle (194.507 s), ingredient/group
editing (115.876 s), and selected-recipe shopping/list lifecycle (108.747 s). Each isolated
fixture also passed its authentication journey and final Angular observer without skips.
Photo selection/replacement/removal also passes (168.145 s), including rendering each newly saved
asset identifier. Its final removal assertion has since been strengthened to use the actual asset
identifier rather than a recipe-title lookup and is queued for another run. These are completed
stages, not a passing full sixteen-suite matrix. The newly added manual
unspecified-quantity/no-unit case was added after that recipe-editing build and still requires
its own execution. The import-review suite now additionally checks retained input and no recipe
creation after a blocked private URL; its updated execution is pending.

The complete native acceptance command now includes both the targeted Dynamic Type suite and
the continuous cross-client critical journey at the largest accessibility text size, followed
by the unfiltered native accessibility audit. Functional passes do not waive audit findings.

T092 remains open. The original thirteen-suite sequential matrix passed without skips on
2026-11-04 using:

```sh
RECIPE_MANAGER_E2E_TEXT_SIZE=large scripts/validate-release.sh --native-parity
```

Use the pinned .NET/Node/Xcode tools and fixture prerequisites documented in quickstart.md. Every
suite receives its own migrated PostgreSQL fixture, HTTPS application and signed iOS simulator;
the authentication setup/invitation/profile journey and final Angular observer run with each suite.
The command stops on failure. A passing earlier standalone suite is not a passing full matrix.

| Suite | Principal acceptance scope | Original thirteen-suite result |
| --- | --- | --- |
| CatalogueUITests | Canonical correction, aliases, categories, referenced ingredient safety | Passed without skips |
| RecipeEditingUITests | Structured editing, serving preview, recipe lifecycle | Passed without skips (107.3 s feature test) |
| PlannerUITests | Explicit reservation, partial consumption, release, snapshot refresh | Passed without skips (253.0 s feature test) |
| ShoppingUITests | Planned snapshot, unknown quantities, edits, reconciliation | Passed without skips |
| NativeSyncUITests | Foreground updates, retained dirty draft, background recovery | Passed without skips |
| RecipeSearchUITests | Organisers, assignments, search/filtering, safe deletion | Passed without skips |
| ImportReviewUITests | AI text review, measurement standard, unknown quantities | Passed without skips |
| ShareHandoffUITests | Signed URL/text/mixed capture and cancellation | Passed without skips |
| ShareImportUITests | Capture to reviewed durable commit and inbox cleanup | Passed without skips |
| ImportRetryUITests | Lost successful response, restart, expired draft, disabled AI | Passed without skips |
| DynamicTypeUITests | Actual system text growth and usable calendar targets | Passed without skips |
| CrossClientUITests | Angular and SwiftUI shopping writers/observers | Passed without skips |
| CriticalJourneyUITests | Native-first and web-first continuous import-to-reconciliation | Passed without skips |

These are functional checks on iOS 27.0, not the iOS 18 minimum-OS, physical-device, manual VoiceOver
or MP-SYNC-01 performance gates. `--native-acceptance` additionally runs the largest-system-text
subset and unfiltered native audit; the latest completed unfiltered audit remains red with 12
findings. No finding is waived by running the functional-only matrix.

The original thirteen-suite command passed. The command now also includes CoreWorkflowParityTests,
RecipeImageUITests and RecipeLifecycleParityTests; the expanded sixteen-suite orchestration has not
yet passed end to end. Continue reviewing assertions against FR-001–044 and retain any uncovered operations
as explicit work before checking T092. A list of suite names alone does not establish full coverage.

## Assertion review still required

The reviewed native story tests establish substantial behavior but do not yet establish every
operation in FR-001–044. Before final closure, explicitly check image upload/replacement/removal
(FR-002); independently editable complete duplicates and restore/referenced deletion (FR-006–008);
ingredient/group add, duplicate, reorder and remove plus all supported quantity/unit forms
(FR-009–010); website multi-candidate/error paths (FR-016); custom slots and calendar move/duplicate/
remove/other meals (FR-019–020); selected-recipe shopping generation and list item removal
(FR-027/030). Backend invariant coverage supports these operations but does not substitute for
client interaction assertions. Expand the applicable suites where assertions are absent, then run
the changed suites. Keep accessibility, authorization and deployment evidence in their own gates.

## Additional operation-level validation

The additional signed simulator fixtures passed custom slot create/rename/reorder/remove and
other-meal create/move/duplicate/edit/delete (165.5 s), selected-recipe shopping generation,
generated item removal/restoration and list rename/delete (97.8 s), ingredient/group editing
(102.291 s), photo selection/replacement/removal (121.825 s), and complete independent recipe
duplication, restore and blocked referenced deletion (94.790 s).

These runs exposed and verified fixes for embedded ingredient pickers capturing disclosure taps,
stale ingredient bindings after deletion/reordering, repeated private-image fetches, and referenced
recipe deletion feedback appearing outside the visible viewport. The two-photo fixture now issues
six image requests; the prior looping run issued 1,507. This request-count guard is a regression
check, not the real-time performance acceptance criterion. The current native unit/handoff baseline
is 95 passing tests (76 application and 19 handoff tests), including binding and connection-retry
regressions. The complete expanded 16-suite release invocation still requires a successful run.

## Shared-household simulator regression

A combined fixture passed authentication/setup (148.720 s), planned/actual leftover accounting,
partial consumption, release and explicit snapshot refresh (251.294 s), structured recipe
editing/lifecycle (97.453 s), and shopping snapshots, unknown quantities and reconciliation
(103.811 s). Its final Angular profile observer passed in 1.0 s. Assertions scope owned records
while retaining other household content.

## Physical iPhone evidence — 2026-11-04

Automatic development signing, app/extension entitlements, provisioning and signed installation
pass on the designated iPhone running iOS 27.0.1. The phone uses normal HTTPS certificate validation
against the owned isolated LAN deployment; the owner enabled trust for its test CA. Existing
household data and credentials are preserved across retries.

| Physical case | Result |
| --- | --- |
| Initial installation/launch | Passed (11.265 s) |
| Initial household setup, invitation and shared profile | Passed (170.865 s) |
| Planner: planned/actual quantities, partial consumption/release, frozen snapshot, explicit refresh and conflict resolution | Passed (293.731 s) |
| Shopping: planned snapshot, unknown quantities, purchased/manual edits and reconciliation | Passed (112.981 s) |
| Recipe editing: structured fractions/units, serving preview, edit, favourite, duplicate, archive and delete | Passed (130.464 s) |

Planner and shopping passed within an invocation whose recipe fixture was ambiguous because prior
interrupted runs retained identically titled recipes. Recipe titles are now unique; its focused
retry passes with the network prerequisite (93.744 s) and `TEST EXECUTE SUCCEEDED`. Earlier locked-
device or runner-initialization failures add no acceptance evidence. The three feature passes are
separate case results, not a claim that the earlier failed combined invocation passed.

Physical automation is finished for these cases. Real Safari/ChatGPT share-provider checks and
manual VoiceOver remain pending owner interaction; controlled simulator ShareHost results do not
substitute for those checks. The isolated deployment has AI disabled, so successful text capture
can be tested physically without claiming successful provider extraction.

## TLS proxy regression

Compression-capable HTTPS reads exposed Caddy changing opaque API ETags from `:1` to `:1-gzip`.
The backend correctly returned 412 for those altered validators. Both the shipped Caddy
configuration and LAN generator now exclude API responses from encoding, retaining web asset
compression. Clients do not strip/reconstruct validators, and backend concurrency checks remain
unchanged. The deployment probe now advertises compression and rejects encoded API responses.

A live normally trusted TLS regression verified identical identity/compression-capable validators
and a successful conditional purchase update; only its newly created disposable list was removed.
Physical planner and shopping passes verify the corrected transport in actual native workflows.
API JSON remains uncompressed in v1 to keep opaque validator semantics simple.

## Current simulator regression and remaining gates

The focused catalogue regression now passes without skips. Its final Angular observer passed in
894 ms. Saved recording inspection showed the canonical-name field clipped beneath the fixed
error banner; the test helper now scrolls inside the visible form and checks its containing
viewport in addition to app/keyboard bounds. Native search fields and tab-bar buttons retain their
own interaction areas. No ingredient business rule changed. An intervening runner launch failure
is not counted as a functional result.

Approval review capacity has returned. The expanded sixteen-suite matrix has restarted;
complete it and the remaining FR operation audit before closing T092. The unfiltered
11-finding native accessibility audit, unavailable minimum-iOS runtime evidence, physical share/
VoiceOver checks, reference-host performance and publication decisions remain separate open gates.

The restarted matrix was interrupted deliberately to fix owner-reported website import quality
before further parity validation. No expanded-matrix pass is claimed. The fixed shared backend
passes 348 tests and the isolated LAN deployment check; staged browser/native import regressions
must now precede another complete matrix. See validation/US5.md.

## Current staged expansion — 2026-11-05

After the stable-snapshot helper correction, the staged run passes catalogue management (205.291 s), structured recipe editing/lifecycle (200.561 s), and all three CoreWorkflowParityTests: custom slots/other meals (188.097 s), ingredient groups/order/duplication/removal (112.572 s) and selected-recipe shopping lifecycle (104.567 s). Each fixture also passes its Angular household observer. The core-workflow authentication setup includes a long host suspension; its wall duration is not performance evidence.

The expanded command stopped at photo detail visibility. Its replacement check now scrolls the saved photo into view and requires the exact new asset ID, with diagnostic screenshots on failure. That stronger check has not yet passed; a subsequent invocation failed during setup before reaching photos. Native performance calibration separately exposed test selection by favourite-suffixed labels and incomplete long-field replacement. Stable row IDs and complete-field selection/focus checks are being validated. These are not grounds to close T092 or claim a full expanded-matrix pass.
