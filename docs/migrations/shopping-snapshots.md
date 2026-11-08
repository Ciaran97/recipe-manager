# Shopping snapshots migration

`20261102104519_ShoppingSnapshots` adds household-scoped shopping lists and items. Apply with the
normal migrator before starting the matching application version. Existing recipes, planner data
and household counters are retained. Back up PostgreSQL, image storage and the protected key ring
before upgrading; test restoration on an isolated installation.

Generated requirements and their original contributions are JSONB snapshots. Numeric overrides,
unresolved text, manual identities, purchase states and generated-item removal tombstones remain
separate. The schema adds no reconciliation proposal table or rational numeric columns. Exact
derived quantities use bounded integer strings inside existing snapshot JSON.

Composite household/list foreign keys prevent foreign household associations. Generated canonical
ingredients are retained by restrictive foreign keys; deleting a list cascades only its own items.
Historical category IDs and names remain display snapshots. Recipe/event deletion does not cascade
into shopping snapshots; application-level reference checks protect their source recipe references.

Rollback drops shopping data, so do not run `Down` against an installation whose lists must be kept.
Prefer restoring the full pre-upgrade backup with the matching application image and key ring.
`ShoppingPersistenceTests` verifies upgrade preservation, no model drift, unresolved quantities,
override/purchase/tombstone round trips, tenant constraints and restrictive ingredient references.
