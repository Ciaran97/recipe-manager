# Architecture boundaries

One ASP.NET Core host, one PostgreSQL database and one EF Core DbContext form the modular monolith.
Core contains domain rules and use cases. Infrastructure implements persistence, file storage and
external HTTP adapters. Api composes both and maps typed HTTP/hub contracts. Neither Core nor
Infrastructure references the API host. Core does not depend on ASP.NET Core or EF Core.

The modules are Households, Ingredients, Recipes, MealPlanning, Leftovers, Shopping and Imports.
Place each module's public cross-module ports/DTOs in its `Contracts` namespace. Other modules
consume those contracts rather than navigating another module's EF entities or editing its rows.
Infrastructure composes module-owned EF configurations and the shared transaction boundary; one
DbContext does not grant permission to bypass a module's invariants. Use simple constructor DI,
not a generic repository/mediator framework or distributed messaging.

Architecture tests enforce project-reference direction, Core framework independence and direct
cross-module source namespace references. They are guardrails, not a proof of semantic isolation;
review delegates/aliases/reflection and cross-module orchestration as well. Add integration tests
for tenant FKs and transactional behavior when mappings/use cases arrive.

Angular and SwiftUI own platform presentation, form state, accessibility and networking only.
Keep quantities as strings/exact server representations. Serving previews, conversions, ingredient
identity/consolidation, leftover availability/reservations, permission checks and reconciliation
are authoritative backend operations. Do not independently recalculate them in either client.
Client-side validation assists entry but never replaces server validation or authorization.
