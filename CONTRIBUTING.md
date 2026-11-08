# Contributing to Recipe Manager

Recipe Manager is preparing for eventual open-source publication. Project license, contribution
terms and private security contact still require the owner's decision; see
[publication readiness](docs/publication-readiness.md). Do not assume a license has been selected.

Read the [constitution](.specify/memory/constitution.md), approved feature specification and technical
plan in `specs/001-household-recipe-manager/` before proposing implementation. Define acceptance
criteria before code, work in small independently testable increments and update the existing task
list/evidence. New MVP features require owner approval. Keep Angular and native SwiftUI core parity;
the Share Extension is part of version 1.

Use the modular monolith and shared REST/OpenAPI backend. Ingredient calculations/consolidation,
recipe snapshots, leftover accounting, permissions and concurrency belong in the backend. Clients
own presentation and networking. Household predicates, current membership/stamp validation and
transactional revision checks are required for data access and mutation. Preserve exact source
quantities, unresolved amounts, immutable cooking snapshots and editable shopping snapshots.

Use pinned dependencies and source-controlled EF migrations. Regenerate both clients after contract
changes, include meaningful business/race acceptance tests, run the appropriate backend/web/native
checks, and record only executed results. Follow [development](docs/development.md),
[testing](docs/testing.md) and [native tooling](ios/README.md). PostgreSQL tests use isolated
Testcontainers; browser/native fixtures refuse occupied ports and never reset a real installation.

Keep secrets, provisioning files, real household exports, screenshots containing private data and
private fixture artifacts out of contributions. Use synthetic recipes/accounts/images and local fake
AI providers for tests. Do not introduce a paid-service requirement. Raise security reports through
the private process in [SECURITY.md](SECURITY.md), once the owner publishes that route.

A proposed change should explain the concrete behavior, acceptance criteria and validation; include
migration/recovery impact and material limits. Keep public API/error text and accessibility usable
on both clients. Do not mark physical-device, reference-host performance or publication gates complete
from unit/container tests alone. No maintainer response-time or contribution-license agreement is
promised before those policies are approved.
