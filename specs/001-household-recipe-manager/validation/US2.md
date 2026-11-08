# Meal planning and leftovers verification — complete

T055–T069 are complete. Both clients pass the independent meal-planning acceptance gate.
Shopping generation and production release verification remain separate later milestones.

| Gate | Result |
| --- | --- |
| Full backend | 193 pass: 69 unit, five architecture, 119 integration; no skips or excluded planner tests |
| Angular planner | 36 tests, lint and production build pass; browser journey passes in 4.8 seconds |
| Swift planner | 40 tests pass, including release payloads, unchanged consumed state, decimal strings, civil dates, stale-write draft preservation, opaque slot validators and protection of content edits during position-only actions |
| Native acceptance | Fresh setup/invitation and full planner journey pass without skips; Angular observes the native profile update (879 ms) |
| Schema | OpenAPI, TypeScript and Swift regenerated without dropped-body warnings |
| Migration | Existing-household backfill, restrictive/tenant/type FKs and no model drift pass in PostgreSQL |

Portion/allocation tests prove one planned/actual capacity basis, fractional portions, absolute
corrections, partial consumption, explicit remaining reservations, repeated release, reload and
fresh duplication without restoring historical reservations. Release of 0.5 from q=2/c=1.5/r=0.5
frees exactly 0.5 with q/c unchanged. Another reservation can use that 0.5. Released/partially consumed
plans cannot silently infer a new remainder when resized.

API tests cover weekly/inclusive calendar reads, all three meal types, no automatic leftover meals,
custom slots, moves, duplicates without actuals/dependent copies, used-slot archiving, same-day
chronology, recorded-consumption deletion confirmation, and foreign authenticated household denial
including conditional reads. Two-member allocations and allocation-versus-actuals races admit only
one stale-revision winner. Concurrent/lost-response cooking creation retries create one event.

Atomic resolution tests cover invalid actual correction rollback, reservation-only release preserving
consumption, explicit dependent clearing before source actuals removal, source deletion with explicit
dependent removal, and consumed-allocation reassignment checking both sources. Child changes bump
source revision; compound meal validators invalidate other dependent representations. All commands
use the common authenticated household transaction plus sorted source row locks. No broker, ledger,
general merge engine or background stock process was added.

Snapshot tests preserve complete original recipe content, exact amounts, standards, canonical factors,
instructions and private image references. Live recipe/catalogue edits and cooking portion changes
leave snapshots untouched. Refresh preview does not write; apply fences event/recipe revisions,
preserves planned/actual fields and rejects stale replay. Frozen ingredient/image references remain
protected until explicit refresh; scoped JSONB queries avoid a separate dependency index.

Tests exposed and verified fixes for EF insert ordering with a shadow child discriminator (now
explicitly initialised), padded decimal response strings, optional DELETE bodies dropped by Swift
codegen, and nullable numeric schemas losing their string type. Original ingredient text is retained;
normalising decimal display never rounds. Recovery notes are in docs/migrations/meal-planning.md.

The Angular journey schedules four prepared/two eaten, reserves Tuesday leftovers, confirms and
partially consumes them, releases 0.5, explicitly reallocates that availability to Wednesday,
preserves a frozen recipe after live edits, reviews/applies refresh and atomically corrects actuals
with an explicit dependent release. The dependent meal and its recorded consumption remain intact.
It also renames the canonical ingredient, removes its live-recipe reference, then verifies deletion
still conflicts because the cooking snapshot retains it; the frozen ingredient name remains original.

The equivalent native journey runs as the invited Member. It verifies planned-only scheduling,
explicit reservations, combined source confirmation and 1.5 consumed/0.5 reserved, release with
unchanged consumption, frozen recipes after live edits, complete saved/latest comparison and
explicit refresh. It then reserves the released 0.5 for Wednesday and corrects production to three
prepared/1.5 eaten with an explicit atomic release of Wednesday's reservation. Tuesday's 1.5
consumed portions remain recorded; Wednesday's scheduled meal remains present.

UI testing found and corrected the add-meal menu touch target and loss of the recipe-refresh
preview when returning from comparison. Returning now retains the reviewed revisions; stale apply
still fails through the backend. Tests target the share-sheet Copy label, verify confirmation-switch
state, and wait for actual editor dismissal before checking committed results. No authentication
bypass, application test endpoint or automatic conflict resolution was introduced.
