# Meal planning and explicit leftover allocations

`20261101235120_MealPlanningAndAllocations` adds `planning.MealSlots`, `ScheduledMeals`,
`CookingEvents` and `leftovers.Allocations`. It preserves recipes, users and existing household data.
The migration seeds Breakfast/Lunch/Dinner for existing households; installation setup uses the
same defaults transactionally for new households.

All links include the household ID. A child discriminator FK prevents cooking events being attached
to Other/Leftovers meals and prevents allocations being attached to Cook/Other meals. One owned
cooking event or allocation is allowed per scheduled meal. Deleting a scheduled meal deletes its
owned child; restrictive source links block deleting a cooking event with remaining allocations.
Slots referenced by meals are retained and archived by the application. Recipe links remain
restrictive even though events also retain immutable recipe snapshots.

Portions use numeric(18,6). Actual prepared/eaten values and confirmation times are either all
present or all absent. Allocations store historical planned intention separately from nullable
absolute consumption and the remaining reservation. Reload uses the persisted reservation and
never reconstructs it from planned portions. Source chronology and aggregate capacity require
application transactions and source revision validation; row constraints cannot enforce a sum
across separate meals.

Back up the database and matching images/Data Protection keys before upgrading; apply with the
one-shot migration process. Validate on a restored copy first. Downgrade drops these four new tables
and their planning data; do not use it to recover an installation with accepted meal plans. Restore
the pre-upgrade backup with matching files/keys if rollback is necessary. Do not remove original
volumes. This schema migration alone does not establish completed planner API or client workflows.
