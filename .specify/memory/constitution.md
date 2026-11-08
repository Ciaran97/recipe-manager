<!--
Sync Impact Report (temporary review material; remove before committing)
Version change: unversioned scaffold → 1.0.0 (initial adoption)
Modified principles: placeholder principles replaced by principles I–IX below.
Added sections: Technology and Operational Constraints; Development Workflow and Quality Gates;
project-specific Governance.
Removed sections: none; illustrative scaffold comments removed.
Dependent templates and commands: unchanged; they read this constitution at runtime.
Deferred decisions: MVP scope; authentication and household membership model; supported units,
rounding and ingredient-specific conversions; open-source license. See workflow TODOs.
No unresolved template placeholders.
-->
# Recipe Manager Constitution

## Core Principles

### I. Modular Monolith and Explicit Domain Boundaries

Recipe Manager MUST use a modular monolith, with one backend application rather than
microservices. Recipes, ingredients, meal planning, leftovers, and shopping lists MUST have
explicit responsibilities and ownership of their business rules. Cross-module interactions
MUST use explicit application contracts rather than bypassing another module's rules through
its persistence details. Module boundaries MUST support independent testing without requiring
separate deployment, a generic framework, or speculative service extraction.

### II. Strong Types, Validation, and Maintainability

Backend and frontend code MUST use strong types for domain values and API contracts. Inputs
MUST be validated at trust boundaries; the backend MUST enforce business invariants regardless
of frontend validation. Invalid states and operations MUST produce actionable errors without
exposing secrets or internal implementation details. Code MUST favor clear names, cohesive
functions, and explicit dependencies. Abstractions MUST solve a demonstrated requirement.

### III. Household Isolation and Secure Authentication

Every operation on household-owned data MUST authenticate the user and authorize household
membership and the requested action on the server. Isolation MUST cover REST endpoints,
database access, SignalR subscriptions and broadcasts, and any background processing.
Client-supplied household identifiers MUST NOT establish authorization. Credentials and sessions
MUST use established security mechanisms; custom cryptography is prohibited. Automated tests
MUST verify unauthenticated access rejection and cross-household access denial, including
real-time updates. Logs MUST NOT expose credentials, tokens, or sensitive household content.

### IV. Deterministic Ingredient Calculations

Ingredient scaling, unit conversion, leftover accounting, and shopping quantity aggregation
MUST be deterministic: identical inputs and policy versions MUST produce identical results.
Quantities MUST use decimal or exact representations with explicit precision and rounding
rules; binary floating-point MUST NOT determine persisted business quantities. Units and
conversion factors MUST be explicit. Incompatible dimensions MUST NOT be silently combined;
mass-to-volume conversion MUST require an explicit ingredient-specific factor. Missing or
ambiguous quantities MUST remain explicit rather than being guessed. AI output MUST NOT be
treated as an authoritative calculation or conversion factor without validation.

### V. Automated Verification and Versioned Data Changes

Critical business rules MUST have automated tests, including recipe scaling, conversions,
shopping aggregation, meal and leftover state transitions, and authorization. Tests MUST cover
relevant boundary cases, invalid inputs, and failure paths. Database-dependent behavior MUST
have integration coverage against PostgreSQL where database semantics affect correctness.
Schema changes MUST use Entity Framework Core migrations committed with the corresponding
code. Each migration MUST have a reviewed data-preservation and recovery approach; manual
production schema edits MUST NOT replace migrations.

### VI. Optional, Provider-Configurable AI

Core recipe management, meal planning, leftovers, and shopping list workflows MUST work with
AI disabled and without an AI service account. AI integrations MUST sit behind explicit
provider contracts, with providers, models, endpoints, and credentials configurable outside
source code as applicable. AI failures MUST yield actionable errors without corrupting domain
state. External transmission of household data MUST be explicit to users and limited to the
requested operation. Generated content MUST pass normal validation before becoming domain data.

### VII. Mobile-First and Accessible Experience

User interfaces MUST be responsive and designed for mobile use first. Implemented workflows
MUST support keyboard operation, visible focus, meaningful labels, readable contrast, and
accessible validation feedback. New UI MUST target WCAG 2.2 AA and include accessibility checks
appropriate to the change. Critical workflows MUST be verified at mobile and desktop sizes;
real-time updates MUST preserve usable focus and communicate relevant state changes accessibly.

### VIII. Simple Self-Hosting and Publication Readiness

A documented Docker Compose deployment MUST provide the application and PostgreSQL with
external configuration and persistent data storage. Setup, upgrades, migrations, backup, and
restore MUST be documented as they become applicable. Secrets MUST NOT be hardcoded or committed;
example configuration MUST use safe placeholders. Core operation MUST NOT require proprietary
hosted services. Source, documentation, fixtures, and dependencies MUST remain suitable for
public release: no private household data, confidential material, or unreviewed licensing
obligations may be introduced.

### IX. Specification-Driven Delivery and Scope Discipline

Implementation MUST follow an agreed specification with testable acceptance criteria defined
before coding. Work MUST proceed in small, independently testable increments tied to those
criteria. Features outside the agreed MVP MUST receive explicit project-owner approval before
implementation. Architecture, dependencies, and abstractions MUST be justified by current
requirements; speculative features and overengineering are prohibited. This constitution defines
engineering constraints, not approval of every domain capability for the MVP.

## Technology and Operational Constraints

- Backend: .NET 10 and ASP.NET Core.
- Persistence: PostgreSQL accessed through Entity Framework Core.
- Frontend: Angular and TypeScript with strict type checking enabled.
- API: REST with an OpenAPI description maintained alongside endpoint behavior. Contract changes
  MUST update the description and any affected clients in the same increment.
- Real-time communication: SignalR for authorized household updates. REST and persisted state
  MUST remain authoritative; reconnect behavior MUST allow clients to recover current state.
- Deployment: Docker Compose with externally supplied configuration and secrets.

Changes to these stack or architectural constraints MUST follow the amendment process.
Dependency versions MUST be explicit and compatible with the chosen stack. Operational
complexity MUST be justified in the implementation plan against a current acceptance criterion.

## Development Workflow and Quality Gates

1. Define the MVP and feature specification, including acceptance criteria, error cases, and
   relevant household isolation, calculation, accessibility, and operational requirements.
2. Produce a plan that checks compliance with this constitution and documents domain boundaries,
   API changes, persistence changes, and any justified complexity.
3. Break implementation into small, independently testable tasks with a clear acceptance outcome.
4. Implement and verify each increment. Relevant builds, type checks, automated tests, migration
   checks, and UI checks MUST pass before the increment is declared complete.
5. Review changes against acceptance criteria and this constitution. Update affected API,
   configuration, deployment, and contributor documentation with the change.

The following decisions MUST be resolved in the relevant specification or plan before dependent
implementation; they do not block adoption of this constitution:

- TODO(MVP_SCOPE): Agree the precise included workflows, exclusions, and acceptance criteria.
- TODO(AUTH_MODEL): Select authentication/session mechanisms and define household creation,
  membership, invitations, roles, and account recovery within the approved scope.
- TODO(QUANTITY_POLICY): Define supported units and locales, canonical representations,
  precision, rounding points, and the source and validation of ingredient-specific factors.
- TODO(OPEN_SOURCE_LICENSE): Select the project license and contribution terms before public
  distribution; review dependencies and third-party assets for compatibility.

## Governance

This constitution takes precedence over conflicting project conventions, specifications, plans,
and implementation shortcuts. Project owners MUST approve amendments explicitly. Each proposed
amendment MUST state its rationale, affected principles, compatibility impact, and any required
migration or remediation. Approval MUST be recorded in the amendment's review record before
adoption; update the constitution version and last-amended date when adopted.

Constitution versions follow semantic versioning: MAJOR for incompatible removals or
redefinitions of governance or principles; MINOR for new principles or materially expanded
requirements; PATCH for non-semantic clarifications and corrections. The original ratification
date MUST remain unchanged by later amendments.

Each specification, implementation plan, and change review MUST assess applicable principles.
Noncompliance MUST be corrected or addressed by an approved constitutional amendment before
merging; MVP scope approval alone does not waive engineering rules. Reviewers MUST reject
unjustified complexity and unapproved scope expansion. Deferred decisions MUST be tracked until
resolved in the appropriate specification, plan, or governance amendment.

**Version**: 1.0.0 | **Ratified**: 2026-11-01 | **Last Amended**: 2026-11-01
