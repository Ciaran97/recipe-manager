# InitialHouseholds migration review

Migration: `20261101111249_InitialHouseholds` (EF Core 10 / Npgsql / PostgreSQL 18).

This initial migration creates new `identity` and `households` schemas/tables, Identity users and
internal claim/login/token stores, households, memberships, invitations and one unconfigured
installation row. It contains no production seed account, password or AI credential. Domain
resource IDs are UUIDs; framework-owned internal Identity claim keys retain their standard integer
storage and are never exposed as household resource IDs. Identity persistence stays in Infrastructure
rather than introducing EF/ASP.NET dependencies into Core.

The database enforces unique normalized email, one household membership per user in v1, a unified
household/member principal key and tenant-aware invitation creator references. Deletions are
restricted across these references. Revisions are concurrency tokens; safe application-level locking,
request isolation and authentication are subsequent foundation tasks, not completed by this migration.

## Data preservation and recovery

- Apply only to a new database or an explicitly reviewed compatible schema. If pre-existing tables
  conflict, stop; do not drop or overwrite them to make the migration pass.
- Before applying to any installation with data, take and verify a database backup and preserve
  matching image/key files. Development/test fixtures use isolated disposable databases.
- Up creates tables and the singleton setup row without changing pre-existing domain data.
- Down drops the new tables and loses any accounts/households added afterward. Do not use Down
  as a production recovery strategy; restore a verified pre-change backup and matching app version.
- Later multi-household support requires a controlled migration of the v1 unique UserId index,
  not weakening tenant foreign keys or silently accepting additional memberships.

Use the pinned local tool (`dotnet tool restore`). Supply ConnectionStrings__Database externally,
never in checked-in source or printed CLI arguments. The design-time factory supports generation
without credentials; integration tests apply the migration against real PostgreSQL/Testcontainers.
