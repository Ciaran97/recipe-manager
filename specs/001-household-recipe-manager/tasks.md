---
description: "Dependency-ordered implementation tasks for the mandatory web and native v1 release"
---

# Tasks: Household Recipe Manager MVP

**Input**: Design documents from `specs/001-household-recipe-manager/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [REST contracts](contracts/rest-api.md),
[real-time/import contracts](contracts/realtime-and-imports.md), [quickstart.md](quickstart.md),
[acceptance-scenarios.md](acceptance-scenarios.md), constitution v1.0.0.

**Tests**: Required by the approved specification/constitution. Define failing tests before the
feature implementation, then run them against the completed slice; compile failures alone are not
business-rule test evidence. No implementation or test execution is performed by task generation.

**Organization**: Story phases are ordered P1 then P2, with dependencies explicit. US4 precedes US1
because authentication blocks real use. US8 is cross-client parity; native feature views are built
inside every owning story. US9 capture begins before P2 imports, but its final integration waits
for US5. A priority-ordered phase list is not permission to ignore forward completion dependencies.

## Format: `[ID] [P?] [Story] Description`

`[P]` means independent files within the named wave after its shared prerequisites; it does not
allow skipping earlier models/contracts. All other tasks run sequentially unless an explicit
independent branch is listed below. Story labels map to spec.md; setup/foundation/polish are unlabelled.
All checkboxes begin unchecked. Paths are repository-relative proposed implementation paths.

## Path Conventions

- Backend: `src/RecipeManager.Api/`, `src/RecipeManager.Core/`, `src/RecipeManager.Infrastructure/`.
- Web: `src/RecipeManager.Web/`; native app/extension/packages/tests: `ios/`.
- Server tests: `tests/RecipeManager.UnitTests/`, `tests/RecipeManager.IntegrationTests/`,
  `tests/RecipeManager.ArchitectureTests/`; web/cross-client E2E: `tests/e2e/`.
- Compose/image assets: `deploy/`; operational guidance: `docs/`; evidence: this feature's `validation/`.
- Model tasks quote source constraints; all original field lists in data-model.md remain normative.
  Enforce global string/tenant/UUID/revision constraints in every entity mapping. New migrations
  must include source-controlled recovery notes and serialize edits to the DbContext/migration snapshot.
- Regenerate OpenAPI/Swift/TypeScript after each endpoint task using the foundational tooling;
  feature clients consume that version before their parallel implementation wave.

## Phase 1: Setup (Shared Infrastructure)

**Goal**: Create backend, Angular and native build/test scaffolds only; no feature is considered delivered by scaffolding.

### Setup

- [X] T001 Create .NET 10 solution and Api/Core/Infrastructure projects with dependency direction in RecipeManager.slnx and src/RecipeManager.Api/RecipeManager.Api.csproj; pin SDK/packages in global.json and Directory.Packages.props.
- [X] T002 Initialize strict Angular 22 workspace with compatible locked Node/TypeScript versions in src/RecipeManager.Web/package.json and src/RecipeManager.Web/package-lock.json; add test/build/check:contracts/test:e2e scripts.
- [X] T003 Create Swift 6/iOS 18+ app and embedded Share Extension targets, shared RecipeManager/ShareHandoff schemes, test targets and unsigned-simulator configuration in ios/RecipeManager.xcodeproj/project.pbxproj; expose local signing/Bundle/App Group settings without secrets.
- [X] T004 Create extension-safe package and generated API adapter package manifests in ios/Packages/ShareHandoff/Package.swift and ios/Packages/RecipeManagerAPI/Package.swift; pin OpenAPI runtime/URLSession dependencies and Xcode in ios/TOOLCHAIN.md.
- [X] T005 Create xUnit/WebApplicationFactory/Testcontainers/architecture test projects and fake clock/provider fixtures in tests/RecipeManager.IntegrationTests/Fixtures/PostgresFixture.cs and tests/RecipeManager.UnitTests/RecipeManager.UnitTests.csproj; wire native unit/UI/share test targets in ios/RecipeManager.xcodeproj/project.pbxproj.
- [X] T006 Configure analyzers, formatting, nullable/strict checks and ignored build/secrets/signing files in .editorconfig, Directory.Build.props, .gitignore and src/RecipeManager.Web/eslint.config.js.
- [X] T007 Create developer-only PostgreSQL 18 Compose service and safe config example in deploy/compose.yaml and deploy/.env.example; pin image and keep credentials external, database unexposed outside development.
- [X] T008 Define module visibility/dependency checks and no-frontend-domain-calculation conventions in tests/RecipeManager.ArchitectureTests/ModuleBoundaryTests.cs and docs/architecture.md.
- [X] T009 Create Linux backend/web and macOS native build/test pipelines in .github/workflows/backend-web.yml and .github/workflows/ios.yml; build app plus extension and fail release on either pipeline failure.

**Checkpoint**: Shared prerequisites compile and their checks pass.

## Phase 2: Foundational (Blocking Prerequisites)

**Goal**: Establish shared persistence, tenant/auth primitives, transport and client infrastructure before feature phases.

**Dependencies**: Complete Phase 1 first.

**Independent Test**: Build all targets and run shared serialization/authorization harness tests; seeded authenticated households can exercise protected test routes.

**Critical**: Complete this foundation before feature implementation.

### Shared infrastructure

- [X] T010 Create tenant IDs, aggregate revisions, date/clock primitives and validators in src/RecipeManager.Core/Shared/DomainValues.cs; enforce "All application/domain IDs are UUIDs", "all tenant-owned roots include HouseholdId and bigint Revision", "names 200, descriptions/notes 20,000, URLs 2,048" and "meal dates are PostgreSQL date/DateOnly in household IANA timezone".
- [X] T011 Create household/Identity entities in src/RecipeManager.Core/Modules/Households/Household.cs plus shared DbContext/mapping conventions and initial Identity/household migration in src/RecipeManager.Infrastructure/Persistence/RecipeManagerDbContext.cs and src/RecipeManager.Infrastructure/Persistence/Migrations/20261101111249_InitialHouseholds.cs; enforce "Tenant-aware unique keys and foreign keys use (HouseholdId, Id)" and restrictive deletes. Model fields/constraints from data-model.md: Households / Installation: Singleton Id, SetupCompletedAt; "Pre-seeded row; locked for first setup"; Households / User: Identity user, email, normalized email, password hash, security stamp, IsActive, preferences, PreferencesRevision; "Unique normalized email; no public registration"; Households / Household: Name, timezone, configuration revision, ShoppingSourcesRevision, ContentRevision/domain counters, protected AI settings; "One initial household; config Admin-only"; Households / Membership: UserId, HouseholdId, role Administrator/Member, active state, revision; "Unique UserId in v1, unique household/user pair; removal deactivates access"; Households / Invitation: HouseholdId, token hash, expiry, accepted/revoked times, createdBy; "Unique token hash; single-use transaction; role Member".
- [X] T012 Implement Identity cookie/CSRF/role policies and account-active/current-membership/security-stamp/ticket-expiry validation on every protected request in src/RecipeManager.Api/Security/CurrentMembership.cs and src/RecipeManager.Api/Security/IdentityConfiguration.cs; 14-day maximum ticket, HTTPS/HttpOnly, no public generic registration or JWT subsystem.
- [X] T013 Implement household-before-sorted-aggregate locking, bounded three-attempt deadlock/serialization retry, revisions and rollback helpers in src/RecipeManager.Infrastructure/Persistence/TransactionBoundary.cs; never retry stale user intent or publish before commit.
- [X] T014 Implement ContentRevision/domain counters, separate ShoppingSourcesRevision, personal PreferencesRevision and transactional invalidation recorder in src/RecipeManager.Infrastructure/Persistence/RevisionCoordinator.cs; include range insert/delete/move changes, exclude shopping edits from source revision.
- [X] T015 Implement string decimal/rational/revision DTO serialization, If-Match/428/412, 409/422 Problem Details, 25/default-100/max cursor paging and 366-day range validation in src/RecipeManager.Api/Contracts/TransportConventions.cs; scope ordinary creation retry-key records to household/user/route with payload hash and 24h expiry; shared imports use durable handoff receipts, never this expiring cache.
- [X] T016 Write shared API contract serialization, ETag, CSRF and PostgreSQL tenant-FK tests in tests/RecipeManager.IntegrationTests/Contracts/TransportTests.cs; verify unauthenticated 401, role 403, foreign resource 404 and no secret-bearing errors.
- [X] T017 Generate and version OpenAPI plus TypeScript/Swift clients from endpoint metadata in specs/001-household-recipe-manager/contracts/openapi.json, src/RecipeManager.Web/src/app/core/api/generated/ and ios/Packages/RecipeManagerAPI/Sources/Generated/; configure reproducible regeneration script in scripts/check-contracts.sh, rerun after every endpoint task.
- [X] T018 Implement Angular HttpClient session/CSRF/error/ETag adapter and lazy four-tab shell with role-only UI guards in src/RecipeManager.Web/src/app/core/api/api-client.ts and src/RecipeManager.Web/src/app/app.routes.ts; no numeric business arithmetic in JavaScript.
- [X] T019 Write native session/network unit tests with stub URLProtocol in ios/RecipeManagerTests/Core/SessionTests.swift for origin binding, cookie renewal races, CSRF, expiry, redirect rejection, cancellation and no password persistence.
- [X] T020 Implement native structured URLSession/API/session actors and Keychain store in ios/RecipeManager/Core/APIClient.swift, ios/RecipeManager/Core/SessionActor.swift and ios/RecipeManager/Core/KeychainStore.swift; ephemeral cookie jar, "WhenUnlockedThisDeviceOnly", capture renewal atomically, verify TLS, no shared-container credentials.
- [X] T021 Implement SwiftUI four-tab shell, server selection, protected navigation, MainActor feature-state conventions and accessible reusable input components in ios/RecipeManager/App/RecipeManagerApp.swift and ios/RecipeManager/Core/ServerSelection.swift; account/server change clears origin-scoped credentials and requires pending-input confirmation.
- [X] T022 Implement basic DI/composition, secret-file configuration, protected Data Protection keys, safe structured logs and test-host setup in src/RecipeManager.Api/Program.cs and src/RecipeManager.Infrastructure/Configuration/SecretConfiguration.cs; no private recipe text/tokens in logs and no live paid provider in tests.

**Checkpoint**: Shared prerequisites compile and their checks pass.

## Phase 3: User Story 4 - Share safely within a household (Priority: P1)

**Goal**: Deliver first-run setup, invitations, shared-content authorization and role-appropriate settings on both clients.

**Dependencies**: Phase 2; later endpoint tasks extend the same security test matrix. Complete the common T029 barrier before finalising T027 membership removal and T028 password changes; those commands delegate to that barrier. Keep IDs stable.

**Independent Test**: Seed two households; exercise setup/invite/removal/role cases in Story 4 and AC-12; both roles share CRUD, outsiders cannot access records.

### Tests — define failing scenarios before feature implementation

- [X] T023 [P] [US4] Write setup/invite/profile/settings API contract tests in tests/RecipeManager.IntegrationTests/Households/HouseholdContractTests.cs covering contracts/rest-api.md, 48-hour invites, single use, personal credentials, last-Admin rejection and all-client logout.
- [X] T024 [P] [US4] Write PostgreSQL concurrent-first-setup/invite-acceptance/removal tests with separate connections in tests/RecipeManager.IntegrationTests/Households/MembershipRaceTests.cs; require exactly one first registration and immediate access revocation.
- [X] T025 [P] [US4] Define all-six-cause revocation integration/barrier tests in tests/RecipeManager.IntegrationTests/Households/SessionRevocationTests.cs: logout/password change/reset/membership removal/account deactivation/stamp rotation, queued and in-flight broadcasts, expired cookies, reconnect denial and valid re-login; use a minimal protected fixture hub sharing the real registry/dispatcher now, then rerun against production hubs after T078; no send starts after durable revoke.

### Implementation

- [X] T026 [US4] Implement setup wizard transaction with operator one-use setup secret, initial household/Admin and public-signup closure in src/RecipeManager.Core/Modules/Households/SetupHousehold.cs; lock pre-seeded singleton installation row.
- [X] T027 [US4] Implement invitation issue/list/revoke/accept and membership removal under household locking in src/RecipeManager.Core/Modules/Households/MembershipService.cs; token hashes only, Member role, one household/user, preserve shared data and sole Administrator.
- [X] T028 [US4] Implement own profile/preferences/password and Admin household/AI settings with encrypted write-only credentials in src/RecipeManager.Core/Modules/Households/SettingsService.cs and src/RecipeManager.Infrastructure/Security/ProtectedProviderSettings.cs; password change requires current password and delegates security-stamp rotation to the common revocation service; preserve own PreferencesRevision and send no household-wide personal notification.
- [X] T029 [US4] Implement common SessionRevocationService, account IsActive enforcement and per-user dispatch gate in src/RecipeManager.Core/Modules/Households/SessionRevocationService.cs and src/RecipeManager.Infrastructure/Security/AuthorizedConnectionRegistry.cs; cancel/drain bounded sends before commit, abort sockets before success, gate→household→aggregate order, all paths including operator maintenance delegate here and no direct Identity mutation bypass.
- [X] T030 [US4] Implement setup/auth/profile/settings/invitation/member endpoints in src/RecipeManager.Api/Endpoints/HouseholdEndpoints.cs; rate-limit credential/setup/invite actions, validate stamp every request, logout invokes common revocation barrier across web/native; password/reset/deactivation/removal/explicit stamp changes cannot bypass it and obtain fresh CSRF after session restore.
- [X] T031 [US4] Implement host-local authenticated maintenance endpoint/adapter in src/RecipeManager.Api/Maintenance/MaintenanceEndpoints.cs; 0600 Unix socket only, mounted maintenance secret, no public TCP listener, route reset/deactivate/stamp rotation through common live-host service and reject sole active Administrator deactivation.
- [X] T032 [P] [US4] Implement Angular setup/login/invite acceptance and Admin/member settings views in src/RecipeManager.Web/src/app/features/auth/auth.routes.ts and src/RecipeManager.Web/src/app/features/settings/settings-page.ts; label all-session sign-out, remove invite tokens from history/referrers.
- [X] T033 [P] [US4] Implement native setup/login/invite paste/open and role-appropriate settings/profile/password views in ios/RecipeManager/Features/Auth/AuthView.swift and ios/RecipeManager/Features/Settings/SettingsView.swift; confirm self-hosted origin and use shared session actor.
- [X] T034 [US4] Implement local operator password-reset maintenance entry point and recovery guide in src/RecipeManager.Api/Maintenance/ResetPasswordCommand.cs and docs/recovery.md; short-lived Identity reset and operator account-deactivation invoke the running host service via 0600 Unix socket plus mounted maintenance secret (or host-stopped offline mode), no public recovery/deactivation service or logged credentials; preserve last active Admin.
- [X] T035 [US4] Run browser/native login/setup/invite tests and full tenant/security suite in tests/e2e/households.spec.ts and ios/RecipeManagerUITests/AuthenticationUITests.swift; add protected API/hub endpoints to this matrix as each story lands and record results in specs/001-household-recipe-manager/validation/US4.md.

**Checkpoint**: US4 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 4: User Story 1 - Keep reusable structured recipes (Priority: P1)

**Goal**: Deliver structured manual recipes, catalogue, quantities and serving previews on Angular and SwiftUI.

**Dependencies**: Phase 2 and US4 authenticated session; organisers/search refinement arrive in US6, no duplication of their implementation.

**Independent Test**: Create/edit/duplicate/archive/unarchive/delete/favourite a recipe, manipulate grouped ingredient rows and scale without mutation in both clients; pass Story 1 and AC-10.

### Tests — independent first wave

- [X] T036 [P] [US1] Write property/unit fixtures for decimals/fractions, 1/0 rejection, exact thirds, AU/US conversion, scaling and no early rounding in tests/RecipeManager.UnitTests/Ingredients/QuantityTests.cs; cover 486.588ml and magnitude/precision/component limits.
- [X] T037 [P] [US1] Write recipe/catalogue/image/serving-preview contracts and tenant CRUD tests in tests/RecipeManager.IntegrationTests/Recipes/RecipeContractTests.cs including catalogue canonical rename/aliases/category/referenced-delete and archive/favourite/duplicate semantics and referenced-delete protection.
- [X] T038 [P] [US1] Write API catalogue acceptance plus Angular/Swift presentation tests in tests/RecipeManager.IntegrationTests/Ingredients/CatalogueManagementTests.cs, src/RecipeManager.Web/src/app/features/ingredients/catalogue-page.spec.ts and ios/RecipeManagerTests/Ingredients/CatalogueStateTests.swift for browse/search/create/edit/rename, alias add-edit-remove, category change, normalized conflicts, referenced delete and frozen snapshots.

### Models, services and endpoints

- [X] T039 [US1] Implement bounded decimal/fraction input values in src/RecipeManager.Core/Modules/Ingredients/Quantity.cs: quote limits "Accept at most six decimal places; quantities <=10^9 and portions <=10^6", "Numeric quantities are nonnegative and denominators positive"; preserve original input and unknown discriminator; quote "input components <=10^9" for fraction numerator/denominator, mutually exclusive decimal/fraction forms and unknown numeric nulls, no numeric(78,0) EF mapping.
- [X] T040 [US1] Implement pure exact scaling/conversion and display policy in src/RecipeManager.Core/Modules/Ingredients/IngredientCalculator.cs; "Bound each computed component to 4096 digits and input batches to 1000 lines", AU 250/20/5ml and US 236.588/14.787/4.929ml, g/ml/item canonical, no density guesses, display-only rounding as research.md.
- [X] T041 [US1] Implement catalogue entities, unified name lookup, aliases/category mappings and migration in src/RecipeManager.Core/Modules/Ingredients/Catalogue.cs and src/RecipeManager.Infrastructure/Persistence/Migrations/IngredientCatalogue.cs. Model fields/constraints from data-model.md: Ingredients / Ingredient: Canonical display name, normalized name, CategoryId; "Household normalized name unique; referenced deletions blocked"; Ingredients / IngredientAlias: IngredientId, normalized alias, display alias; "Unified name lookup uniqueness across canonical names and aliases within household"; Ingredients / IngredientName: NormalizedName, IngredientId, isCanonical; "Unique (HouseholdId, NormalizedName), enforces previous row's unified lookup"; Ingredients / Category: Name, sort order; "Seed Produce/Meat/Dairy/Pantry; uncategorised fallback".
- [X] T042 [US1] Implement recipe aggregate, ordered ingredient/group/instruction mappings, image references and personal favourites in src/RecipeManager.Core/Modules/Recipes/Recipe.cs and src/RecipeManager.Infrastructure/Persistence/Recipes/RecipeConfiguration.cs. Model fields/constraints from data-model.md: Recipes / Recipe: Title, description, image asset ID, default servings, prep/cook minutes, notes, source URL, standard AU/US, unit-policy version, archived; "Positive servings; times nonnegative; title/search indexes"; Recipes / IngredientGroup: RecipeId, heading, position; "Unique recipe/position; optional group"; Recipes / RecipeIngredient: RecipeId, IngredientId, group, position, quantity kind, original text, decimalValue or fraction numerator/denominator, unit code, preparation notes, uncertainty; "Decimal numeric(18,6) or bigint fraction pair; unknown has no numeric value; stable row ID"; Recipes / Instruction: RecipeId, position, text; "Unique recipe/position"; Recipes / Favourite: UserId, RecipeId; "Household membership required, unique user/recipe"; Recipes / ImageAsset: Storage key, validated media type, size, owner household; "Private authorized download; random key, no user file path".
- [X] T043 [US1] Add checked-in recipe migration and data-preservation/recovery notes in src/RecipeManager.Infrastructure/Persistence/Migrations/StructuredRecipes.cs and docs/migrations/structured-recipes.md; enforce numeric(18,6) or bounded bigint fraction pair with unknown/null checks and stable row IDs.
- [X] T044 [US1] Implement manual recipe CRUD/duplicate/archive/favourite, safe reference deletion checks, catalogue CRUD/canonical rename/alias add-edit-remove/categories/autocomplete with unified namespace collisions, referenced-delete safety and no bulk merge and contract query ports in src/RecipeManager.Core/Modules/Recipes/RecipeService.cs and src/RecipeManager.Core/Modules/Ingredients/CatalogueService.cs; duplicate content/order/organiser links, never meal allocations.
- [X] T045 [US1] Implement private image upload/read/removal in src/RecipeManager.Infrastructure/Files/RecipeImages.cs; quote "10 MiB uploaded image, JPEG/PNG/WebP only, maximum 20 megapixels", "reject SVG/HTML", decode/re-encode metadata stripping, preserve images referenced by snapshots.
- [X] T046 [US1] Expose recipe/catalogue/category/alias/image/serving-preview endpoints in src/RecipeManager.Api/Endpoints/RecipeEndpoints.cs and src/RecipeManager.Api/Endpoints/IngredientEndpoints.cs; include PUT alias rename and canonical rename/category/retainOldNameAsAlias; 409 name_conflict/ingredient_referenced; validate tenant-owned row IDs/URLs/required title, regenerate both clients and OpenAPI.

### Parallel client implementation wave — after recipe endpoints/types

- [X] T047 [P] [US1] Implement Angular structured ingredient/group/instruction editor, separate fields/autocomplete, keyboard add/remove/duplicate/reorder, AU defaults and server serving-preview in src/RecipeManager.Web/src/app/features/recipes/recipe-editor.ts.
- [X] T048 [P] [US1] Implement SwiftUI structured editor/group/instruction/native image selection, quantity string entry and server serving-preview in ios/RecipeManager/Features/Recipes/RecipeEditor.swift; preserve originals/unknowns, do not recalculate in Swift.
- [X] T049 [P] [US1] Implement Angular recipe detail/library CRUD/duplicate/archive/favourite actions in src/RecipeManager.Web/src/app/features/recipes/recipe-library.ts; preserve accessible focus, empty/error states and explicit deletion confirmation.
- [X] T050 [P] [US1] Implement SwiftUI native recipe list/detail CRUD/duplicate/archive/favourite actions in ios/RecipeManager/Features/Recipes/RecipeLibraryView.swift; use NavigationStack/sheets and accessible controls.
- [X] T051 [P] [US1] Implement explicit Angular ingredient catalogue list/search/detail/create/edit/rename and alias/category management views in src/RecipeManager.Web/src/app/features/ingredients/catalogue-page.ts; show 409 field errors and referenced-deletion explanation, preserve canonical identity and no implicit bulk merge.
- [X] T052 [P] [US1] Implement equivalent SwiftUI ingredient catalogue management with native forms in ios/RecipeManager/Features/Ingredients/CatalogueView.swift; canonical correction, alias add/edit/remove, category assignment/change, conflict and reference validation feedback; consume backend rules only.

### Story verification

- [X] T053 [US1] Add editor component/state tests and matching browser/native acceptance journeys in src/RecipeManager.Web/src/app/features/recipes/recipe-editor.spec.ts, ios/RecipeManagerTests/Recipes/RecipeEditorTests.swift, tests/e2e/recipes.spec.ts and ios/RecipeManagerUITests/RecipeEditingUITests.swift; record passing Story 1/AC-10 in specs/001-household-recipe-manager/validation/US1.md.
- [X] T054 [US1] Execute equivalent catalogue management acceptance on web/native in tests/e2e/catalogue.spec.ts and ios/RecipeManagerUITests/CatalogueUITests.swift; prove rename preserves recipe references, conflicting aliases reject atomically and referenced ingredient deletion is safe; extend frozen event/list snapshot regression after T069/T085 without blocking initial catalogue CRUD.

**Checkpoint**: US1 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 5: User Story 2 - Plan cooking and explicitly allocate leftovers (Priority: P1)

**Goal**: Deliver weekly calendar, immutable event snapshots, planned/actual portions and explicit leftover reservations on both clients.

**Dependencies**: US1 calculation/snapshot ports and US4; testing does not require shopping/imports.

**Independent Test**: With one seeded recipe run Story 2 and AC-01–05/08, including partial release, stale correction, recipe edit/explicit refresh and concurrent two-member allocation.

### Tests — independent first wave

- [X] T055 [P] [US2] Write planned/actual balance, partial consumption/release/correction and decimal portion tests in tests/RecipeManager.UnitTests/Leftovers/AllocationInvariantTests.cs for AC-01–04; assert "Available=K-sum(c_i+r_i)" and never deduct plannedPortions again; release 0.5 from q=2/c=1.5/r=0.5 frees exactly 0.5 with c/q unchanged, repeat release/reload frees nothing and another meal may reserve the freed 0.5.
- [X] T056 [P] [US2] Write calendar/actuals/refresh/resolve contracts and PostgreSQL race tests in tests/RecipeManager.IntegrationTests/MealPlanning/MealContractTests.cs and tests/RecipeManager.IntegrationTests/Leftovers/ConcurrentAllocationTests.cs for AC-05/08, rollback, chronology and foreign-household denial.

### Models, services and endpoints

- [X] T057 [US2] Implement meal slot, scheduled meal and cooking aggregate models and mappings in src/RecipeManager.Core/Modules/MealPlanning/Meals.cs and src/RecipeManager.Infrastructure/Persistence/MealPlanning/MealConfiguration.cs. Model fields/constraints from data-model.md: MealPlanning / MealSlot: Name, position, default/custom; "Defaults breakfast/lunch/dinner; used slots archived, not cascade-deleted"; MealPlanning / ScheduledMeal: Date, SlotId, type Cook/Leftovers/Other, label, revision; "Index household/date; multiple per slot"; MealPlanning / CookingEvent: ScheduledMealId, RecipeId, recipe revision, snapshot JSONB, plannedPrepared/Eaten, actualPrepared/Eaten nullable, confirmedAt, revision; "One per cook meal; actual pair both null or both set; prepared>0, 0<=eaten<=prepared".
- [X] T058 [US2] Implement allocation model with planned intention, absolute nullable consumption and disjoint remaining reservation in src/RecipeManager.Core/Modules/Leftovers/Allocation.cs; quote "plannedPortions q_i >0", "consumedPortions c_i >=0", "reservedRemainder r_i >=0" and "Its only balance commitment is c_i+r_i". Model fields/constraints from data-model.md: Leftovers / Allocation: ScheduledMealId, SourceCookingEventId, plannedPortions, consumedPortions nullable, reservedRemainder, confirmation time; "Source/date and balance enforced transactionally; nonnegative actuals/remainder".
- [X] T059 [US2] Add planner/leftovers migration, tenant FKs and recovery notes in src/RecipeManager.Infrastructure/Persistence/Migrations/MealPlanningAndAllocations.cs and docs/migrations/meal-planning.md; quote "actual pair both null or both set; prepared>0, 0<=eaten<=prepared" and retain restrictive source deletion.
- [X] T060 [US2] Implement immutable complete recipe snapshot factory and explicit revision-checked refresh preview/apply in src/RecipeManager.Core/Modules/MealPlanning/RecipeSnapshot.cs and src/RecipeManager.Core/Modules/MealPlanning/RefreshRecipeSnapshot.cs; live recipe edits/moves/reconciliation never refresh implicitly, preserve portion/allocation fields and protected image references.
- [X] T061 [US2] Implement meal/slot/calendar CRUD/move/duplicate with Other labels and all three meal types in src/RecipeManager.Core/Modules/MealPlanning/MealPlanner.cs; inclusive date queries, local dates/slot chronology, no automatic leftovers, no copying consumed state or dependent allocations on duplicate.
- [X] T062 [US2] Implement source-locked allocation create/edit/reassign/release/delete and available planned/actual query in src/RecipeManager.Core/Modules/Leftovers/AllocationService.cs; household then sorted source locks, child-only source revision bump, no restoration of released reservation from q on reload; preserve q for comparison and expose commitment=c+r in API, transactions reject source corrections causing over-allocation.
- [X] T063 [US2] Implement optional prefilled actual confirmation/correction/clear and explicit atomic dependent resolutions in src/RecipeManager.Core/Modules/Leftovers/ConfirmConsumption.cs; source confirmation precedes consumed leftovers, combined explicit confirmation allowed, no audit ledger or silent scheduled-meal changes.
- [X] T064 [US2] Expose calendar/slots/cook/other/leftovers/position/duplicate/delete, availability, refresh, actuals, consumption and atomic resolve contracts in src/RecipeManager.Api/Endpoints/MealEndpoints.cs; 0.5 step UI/positive direct decimals, zero same-meal consumption allowed; regenerate clients.

### Parallel client implementation wave — after planner API/types

- [X] T065 [P] [US2] Implement accessible Angular weekly grid/mobile day list, custom slots and add/edit/move/duplicate/remove all meal types in src/RecipeManager.Web/src/app/features/planner/meal-planner.ts; planned prepared/eaten separate and 0.5 step/direct decimal input.
- [X] T066 [P] [US2] Implement SwiftUI weekly/day navigation, native date/slot sheets and all meal actions in ios/RecipeManager/Features/Planner/MealPlannerView.swift; same prepared/eaten/decimal controls with server-derived availability.
- [X] T067 [P] [US2] Implement Angular source selector, partial retain/release, optional actual confirmation/correction, conflict resolution and explicit recipe refresh in src/RecipeManager.Web/src/app/features/planner/leftovers-panel.ts; no implicit consumption or mutation.
- [X] T068 [P] [US2] Implement native source selector, actual confirmation/correction, explicit refresh and partial reservation/conflict sheets in ios/RecipeManager/Features/Planner/LeftoversView.swift; source and dependent revisions remain visible to state model.

### Story verification

- [X] T069 [US2] Automate Story 2/AC-01–05/08 in tests/e2e/planner.spec.ts and ios/RecipeManagerUITests/PlannerUITests.swift plus presentation tests in ios/RecipeManagerTests/Planner/PlannerStateTests.swift; record PostgreSQL race/balance/snapshot evidence in specs/001-household-recipe-manager/validation/US2.md; extend T054 catalogue correction/deletion regression against frozen cooking snapshots.

**Checkpoint**: US2 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 6: User Story 3 - Generate and share a reliable shopping snapshot (Priority: P1)

**Goal**: Deliver both generation routes, unresolved rows, manual shopping edits and safe stateless reconciliation on both clients.

**Dependencies**: US1 and US2; active native sync coordinator is delivered by US8, use API/SignalR observers until that integration gate.

**Independent Test**: Generate equivalent plan/manual lists; verify exact examples/no leftover requirements, preserve edits/purchases on reconcile, test stale apply and two-session updates (Story 3, AC-06–09).

### Tests — independent first wave

- [X] T070 [P] [US3] Write consolidation/generation/baseline/manual-override tests in tests/RecipeManager.UnitTests/Shopping/ShoppingCalculationTests.cs for kg/g, aliases, distinct identities, unknown+known salt, prepared-not-eaten and no leftover contribution.
- [X] T071 [P] [US3] Write shopping/list/item/reconciliation contract and PostgreSQL stale-preview/apply tests in tests/RecipeManager.IntegrationTests/Shopping/ShoppingContractTests.cs; test AC-06–09, source range insertion/removal, all-actual basis validation and no stored proposal table.
- [X] T072 [P] [US3] Write compound sync HTTP/private-notification contract tests in tests/RecipeManager.IntegrationTests/Sync/PersonalRevisionTests.cs: same-user two devices get 200 after favourite/profile edit, unchanged gets 304, another member stays 304, personalized recipe/list refetch sees changed favourite rather than stale 304, household writes invalidate both and revoked/expired auth fails before conditional evaluation.

### Models, services and endpoints

- [X] T073 [US3] Implement shopping snapshot/item models, contribution identities, separate override/manual/tombstone/purchased state and migration in src/RecipeManager.Core/Modules/Shopping/ShoppingList.cs and src/RecipeManager.Infrastructure/Persistence/Migrations/ShoppingSnapshots.cs. Model fields/constraints from data-model.md: Shopping / ShoppingList: Name, source type, source selection JSONB, baseline JSONB, policy version, basis planned/actual, revision; "No cascade from recipe/event; immutable baseline between explicit reconciliations"; Shopping / ShoppingItem: ListId, ingredient/category snapshots, identity+dimension key, generated exact quantity, override exact quantity/text, manual flag, removed tombstone, purchased, position; "Stable IDs; manual items never disappear on regeneration".
- [X] T074 [US3] Implement single exact generation pipeline and consistent source selection in src/RecipeManager.Core/Modules/Shopping/GenerateShoppingList.cs; meal-range reads event snapshots, manual route reads current recipes, planned preparation default, explicit actual basis requires every cook confirmed.
- [X] T075 [US3] Implement unresolved contribution rows and category grouping in src/RecipeManager.Core/Modules/Shopping/ShoppingRequirements.cs; stable source-row keys, known subtotal plus unresolved count/text, unknown is never zero and purchased known row does not hide unknowns.
- [X] T076 [US3] Implement manual item/quantity override/removal-tombstone/purchase edits under list ETag in src/RecipeManager.Core/Modules/Shopping/EditShoppingList.cs; no automatic source updates or pantry subtraction.
- [X] T077 [US3] Implement stateless revision-based reconciliation preview/apply in src/RecipeManager.Core/Modules/Shopping/ReconcileShoppingList.cs; keep existing baseline/flags, server recompute under household then list lock, validate source and list revisions, preserve manual/purchased states unless explicitly changed, repeat stale apply safely fails.
- [X] T078 [US3] Implement SignalR shopping/household subscriptions and post-commit invalidations through the common per-user revocation/dispatch gate with in-process connection revocation in src/RecipeManager.Api/Hubs/ShoppingHub.cs and src/RecipeManager.Api/Hubs/HouseholdBroadcaster.cs; check current membership/Origin, serialize removal vs sends, no content payload/outbox/broker.
- [X] T079 [US3] Implement user-only PersonalChanged notification using existing src/RecipeManager.Core/Modules/Households/SettingsService.cs / src/RecipeManager.Infrastructure/Households/SettingsStore.cs and src/RecipeManager.Api/Hubs/PersonalNotifications.cs; reuse RevisionCoordinator.PersonalAsync for atomic own PreferencesRevision updates rather than duplicating profile/favourite services; derive recipients from authenticated user, gate every send with common revocation and never bump household content for personal writes.
- [X] T080 [US3] Expose generation/list/item/preview/apply and authenticated sync revision endpoint in src/RecipeManager.Api/Endpoints/ShoppingEndpoints.cs and src/RecipeManager.Api/Endpoints/HouseholdSyncEndpoints.cs; authenticate account/stamp/expiry/membership before 304; sync ETag binds household/user/ContentRevision/PreferencesRevision, private/no-cache and Vary Cookie, counters as strings; personalized recipe/list/profile ETags must also include own preference revision to avoid stale 304 after sync invalidation, regenerate OpenAPI/clients.

### Parallel client implementation wave — after shopping endpoints/types

- [X] T081 [P] [US3] Implement Angular source selection, category/unknown display, manual/quantity/remove/purchase actions and explicit reconciliation UI in src/RecipeManager.Web/src/app/features/shopping/shopping-list-page.ts; retain dirty forms and independently checkable unresolved rows.
- [X] T082 [P] [US3] Implement full native generation/shopping/category/manual/purchased/reconciliation forms in ios/RecipeManager/Features/Shopping/ShoppingListView.swift; all source calculations/previews from shared backend and preserve dirty state.
- [X] T083 [US3] Implement Angular SignalR reconnect/resubscribe/initial retry, household invalidation, user-only PersonalChanged and compound-sync ETag/focus/30-second fallback refresh in src/RecipeManager.Web/src/app/core/sync/household-sync.ts; no stale response overwrites accepted edits.

### Story verification

- [X] T084 [US3] Test shopping subscriptions, every revocation cause and expiry racing household/personal broadcasts, old-cookie reconnection denial/fresh-login success, lost post-commit notification recovery and two-session edits in tests/RecipeManager.IntegrationTests/Shopping/RealtimeTests.cs and tests/e2e/shopping.spec.ts; no cross-tenant update delivery.
- [X] T085 [US3] Add shopping native state/UI tests in ios/RecipeManagerTests/Shopping/ShoppingStateTests.swift and ios/RecipeManagerUITests/ShoppingUITests.swift; run AC-06–09 and record core calculation/reconciliation outcomes in specs/001-household-recipe-manager/validation/US3.md; close cross-client live gate after US8; extend T054 catalogue correction/deletion regression against generated shopping snapshots.

**Checkpoint**: US3 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 7: User Story 8 - Use a complete native household client (Priority: P1)

**Goal**: Complete native synchronization/lifecycle and enforce full parity; feature UIs are built in their owning stories, not deferred here.

**Dependencies**: Session foundation and US3 sync endpoint for implementation; full parity verification waits for US5 core/retry integration, US6, US7 and US9. This is an explicit completion gate, not permission to defer native.

**Independent Test**: Run all Story 1–7 core workflows on each client with opposite-client observation, AC-11–13 and SC-011/013/014; no native-only domain formulas.

### Tests — independent first wave

- [X] T086 [P] [US8] Write native sync/session foreground/background/reconnect state tests in ios/RecipeManagerTests/Core/SyncCoordinatorTests.swift for single polling task, conditional 304, backoff, dirty-state preservation and unauthorized clearing.
- [X] T087 [P] [US8] Write native-style API workflow integration tests in tests/RecipeManager.IntegrationTests/Native/NativeWorkflowTests.cs for cookie/CSRF/ETag parity, renewal, invalid membership before 304 and security-stamp revocation.
- [X] T088 [P] [US8] Add native compound ETag/personal-state tests and web personal-notification tests in ios/RecipeManagerTests/Core/PersonalSyncTests.swift and src/RecipeManager.Web/src/app/core/sync/personal-sync.spec.ts; cross-device same-user favourite/profile change refreshes and retains dirty state while different-user caches remain unaffected.

### Implementation and incremental parity gates

- [X] T089 [US8] Implement one-second foreground conditional sync actor with immediate session-checked foreground/reconnect refresh in ios/RecipeManager/Core/SyncCoordinator.swift; cancel on background/logout, jitter backoff, compare compound user-bound ETags/domain/own preference revisions, changed own favourites/profile must return 200 across devices without invalidating another member; no push/offline write queue.
- [X] T090 [US8] Wire scene lifecycle and feature invalidation/error/dirty-form handling in ios/RecipeManager/App/SceneCoordinator.swift; all native views use API-generated types/server calculations and refresh only affected visible state.
- [X] T091 [US8] Build bidirectional automation harness and deterministic fixture observer in tests/e2e/cross-client.spec.ts and ios/RecipeManagerUITests/CrossClientUITests.swift; coordinate web-writer/native-observer and inverse against same PostgreSQL/API, never mock away server invariants.
- [ ] T092 [US8] Run staged native parity suites after US1/US2/US3 and extend after US5/US6 through scripts/validate-release.sh --native-parity and ios/RecipeManagerUITests/ (including CoreWorkflowParityTests, RecipeImageUITests and RecipeLifecycleParityTests); record the full matrix in validation/native-parity.md; cover every FR-001–044 operation, partial release, unknown quantities, snapshot refresh and reconciliation rather than only read-only smoke tests.
- [ ] T093 [US8] Validate Keychain restore, all-session sign-out, secure server/account switch, accessible native navigation/Dynamic Type/VoiceOver and current+minimum OS in ios/RecipeManagerTests/Core/KeychainLifecycleTests.swift and ios/RecipeManagerUITests/NativeAccessibilityUITests.swift; record SC-011/013/014 in specs/001-household-recipe-manager/validation/US8.md after all feature gates.

**Checkpoint**: US8 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 8: User Story 9 - Share a recipe into import review (Priority: P1)

**Goal**: Deliver signed native URL/text capture and durable main-app import handoff in v1 without extension AI/extraction.

**Dependencies**: Phase 2 native scaffolding/auth; capture/storage can proceed early. Import routing uses contract fixtures until US5; end-to-end completion requires US5 draft API and native review.

**Independent Test**: From signed fixture host share URL/text, reopen signed-out/closed app, review/correct and commit exactly once; test AC-14–15 and Story 9.

### Tests — independent first wave

- [X] T094 [P] [US9] Write extension-safe envelope/provider/store tests in ios/ShareHandoffTests/HandoffStoreTests.swift for URL/text/mixed/oversize/cancel/full queue, file interruption, expiry and no credentials; test bindings and duplicate resumption.
- [X] T095 [P] [US9] Create signed share-sheet fixture host and XCUITest plan in ios/RecipeManagerUITests/Fixtures/ShareHost/ShareHostApp.swift and ios/RecipeManagerUITests/ShareHandoffUITests.swift; exercise Safari-style URL and ChatGPT-style text independently of third-party UI automation.

### Implementation

- [X] T096 [US9] Implement protected, coordinated App Group envelope/storage in ios/Packages/ShareHandoff/Sources/ShareHandoff/HandoffEnvelope.swift and ios/Packages/ShareHandoff/Sources/ShareHandoff/HandoffStore.swift; quote "schemaVersion, handoffId UUID, kind URL|text, raw bounded content, createdAt", "20 pending envelopes" and "seven-day expiry"; optional account/server binding, status/draftId/retry key, never session secrets.
- [X] T097 [US9] Implement extension-safe NSItemProvider UTType.url/plainText selection/validation and durable-confirm-before-complete UI in ios/RecipeManagerShareExtension/ShareViewController.swift and ios/RecipeManagerShareExtension/Info.plist; show open-app continuation, no unsupported force launch or AI/network extraction.
- [X] T098 [US9] Configure app/extension App Group, file protection and safe signed target packaging in ios/RecipeManager/RecipeManager.entitlements and ios/RecipeManagerShareExtension/ShareExtension.entitlements; document local team/identifier override in ios/Signing.xcconfig.example.
- [X] T099 [US9] Implement main-app pending capture inbox and explicit account/server confirmation in ios/RecipeManager/Features/Imports/PendingImportInbox.swift; retain content across sign-in/network interruption, discard/expire visibly, never silently cross household/origin.
- [X] T100 [US9] After US5 native review/API tasks, connect handoff to shared URL/text import with durable HandoffId registration/status, stable retry key/draftId and receipt-backed explicit commit in ios/RecipeManager/Features/Imports/ImportReviewViewModel.swift; interrupted retry resumes draft and successful save/discard clears envelope.
- [ ] T101 [US9] Run signed fixture extension tests plus physical Safari/ChatGPT compatibility and VoiceOver share-to-review checks in ios/RecipeManagerUITests/ShareHandoffUITests.swift; record AC-14–15/SC-012 outcomes in specs/001-household-recipe-manager/validation/US9.md after US5 core T102–T115 (not T116); include lost successful response/day-six retry with expired draft, committed outcome after expiry and uncommitted expired failure.

**Checkpoint**: US9 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 9: User Story 5 - Import and review recipes (Priority: P2)

**Goal**: Deliver website URL and optional provider-configurable AI pasted-text review/save on both clients; unlock final Share Extension handoff.

**Dependencies**: US1 recipe write/calculator/catalogue and US4 settings. No dependence on US9; completing this phase unlocks US9 final tasks. Priority P2 does not mean optional for release.

**Independent Test**: Controlled supported/partial/failing website and AI fixtures reach editable drafts; no missing quantity/standard is guessed, save requires review, other workflows work with AI off.

### Tests — independent first wave

- [X] T102 [P] [US5] Write import draft/commit API contracts and source-standard/uncertainty tests in tests/RecipeManager.IntegrationTests/Imports/ImportContractTests.cs; preserve text on failure, same handoff resolves its original recipe even after ordinary retry/draft expiry, no save before review.
- [X] T103 [P] [US5] Write bounded URL importer/DNS/redirect/schema.org and fake-AI schema/provider tests in tests/RecipeManager.UnitTests/Imports/ImporterTests.cs and tests/RecipeManager.IntegrationTests/Imports/UnsafeUrlTests.cs (plus `WebsiteWireTests.cs`, `AiProviderTests.cs`, `ImportExtractionTests.cs`, `ImportQualityTests.cs` and `ImportedRangeShoppingTests.cs`); private/IPv6/metadata/rebinding targets denied.
- [X] T104 [P] [US5] Write PostgreSQL durable handoff contract/race/cleanup tests in tests/RecipeManager.IntegrationTests/Imports/HandoffCommitTests.cs: lost successful response then day-six retry after draft/cache deletion, simultaneous commits, fixed expiry, deleted recipe result, replay after expiry, changed-body conflict and owner isolation; one recipe at most.

### Models and services

- [X] T105 [US5] Implement import draft model/mapping and migration in src/RecipeManager.Core/Modules/Imports/ImportDraft.cs and src/RecipeManager.Infrastructure/Persistence/Migrations/ImportReviewDrafts.cs; quote "measurementStandard AU|US|unknown", unresolved field flags and "Missing source standard blocks draft commit until user selects it". Model fields/constraints from data-model.md: Imports / ImportDraft: RequestedBy, optional HandoffId, source kind/URL, extracted JSONB, uncertainty fields, standard status, expiry, revision; "Household-private; 24h expiry; explicit commit creates recipe atomically".
- [X] T106 [US5] Implement ImportHandoff mapping and checked-in migration/recovery notes in src/RecipeManager.Core/Modules/Imports/ImportHandoff.cs, src/RecipeManager.Infrastructure/Persistence/Migrations/DurableImportHandoffs.cs and docs/migrations/import-handoffs.md; fields HandoffId/HouseholdId/UserId/CapturedAt/RegisteredAt/ExpiresAt/InputHash/Open|Committed|Expired/CommitHash/OriginalRecipeId/CommittedRevision/CommittedAt; quote "Unique (HouseholdId, UserId, HandoffId); one immutable commit outcome; no raw text/credentials; retained independently of drafts".
  - Implementation evidence: the two additive storage tasks share `20261103001539_ImportReviewDraftsAndDurableHandoffs`; domain and PostgreSQL persistence checks are recorded in `validation/US5.md`. Coordination, commit and concurrency acceptance are now verified; see `validation/US5.md`.
- [X] T107 [US5] Implement authenticated handoff register/status/atomic commit service in src/RecipeManager.Core/Modules/Imports/ImportHandoffService.cs; capture ≤7 days old/≤5 minutes future, fixed min(capture+7d,register+7d) expiry, insert-on-conflict/row lock, committed-first replay and 410 safe expiry/deleted outcome, retain result/tombstone metadata for installation lifetime independent of 24h draft cleanup.
  - Verified through eight PostgreSQL service scenarios and HTTP draft/handoff contracts in `validation/US5.md`; ordinary commits delegate bound drafts to the same durable receipt. URL/text extraction endpoints are now implemented; complete client review/share flows remain open.
- [X] T108 [US5] Implement bounded public-HTTP fetcher and schema.org Recipe JSON-LD/@graph/multiple extraction in src/RecipeManager.Infrastructure/Imports/WebsiteRecipeImporter.cs; quote "15 s website timeout, 2 MiB decoded body, at most 3 redirects, 1000 ingredient/instruction rows", validate/pin public DNS each hop, disable ambient proxy/cookies and never fetch candidate images automatically.
- [X] T109 [US5] Implement optional provider interface, OpenAI-compatible adapter and fake adapter in src/RecipeManager.Core/Modules/Imports/IAiRecipeProvider.cs and src/RecipeManager.Infrastructure/Imports/AiTextImporter.cs; quote "pasted text 100 KiB, AI 60 s timeout, validated response <=1 MiB", admin-configured endpoint/model/credential, no provider write/tools or invented authoritative values.
- [X] T110 [US5] Implement review draft edits/multiple-result selection/catalogue matching/source-standard confirmation and atomic reviewed recipe/receipt commit with immutable CommitResult in src/RecipeManager.Core/Modules/Imports/ImportHandoffService.cs and src/RecipeManager.Infrastructure/Imports/ImportStore.cs (using the Ingredients matching and Recipes creation contracts); preserve unknowns/original input, disclose external processing, validate every field through normal recipe rules.
- [X] T111 [US5] Implement bounded expired-draft/creation-idempotency cleanup without a broker in src/RecipeManager.Infrastructure/Imports/ExpiredDraftCleanup.cs; startup and periodic work use explicit household predicates, no persisted reconciliation proposals; never purge durable committed handoff results or expired-key tombstones with 24h draft/retry cleanup.
- [X] T112 [US5] Expose URL/text/draft read-update-delete/commit endpoints plus authenticated handoff register/status/commit endpoints with owner-bound fixed expiry and original CommitResult replay before draft/expiry checks; no non-handoff fallback for bound drafts; use retry-key support in src/RecipeManager.Api/Endpoints/ImportEndpoints.cs; cancellation/timeouts leave original input recoverable, AI disabled does not disable URL/manual entry; regenerate both clients.

### Parallel client implementation wave — after import API/types

- [X] T113 [P] [US5] Implement Angular URL/text input and editable import review/uncertainty/source-standard/catalogue selection in src/RecipeManager.Web/src/app/features/recipes/import-review.ts; cancellation saves no recipe, disclose configured-provider text processing before the explicit import action, and credentials never enter browser provider calls.
- [X] T114 [P] [US5] Implement SwiftUI manual URL/pasted-text import and complete native review fields/measurement confirmation in ios/RecipeManager/Features/Imports/ImportReviewView.swift; route normal and pending Share Extension input through same state, resolve durable handoff status before retry/extraction and recover original commit after a lost response, extraction only server-side; disclose configured-provider text processing before the explicit import action.

### Story verification

- [X] T115 [US5] Add native import presentation tests plus browser/native review journeys in ios/RecipeManagerTests/Imports/ImportReviewTests.swift, tests/e2e/imports.spec.ts and ios/RecipeManagerUITests/ImportReviewUITests.swift; run AI-off/provider failure/source standard cases and record Story 5 outcomes in specs/001-household-recipe-manager/validation/US5.md; now complete US9 handoff and US8 import parity gates.
- [X] T116 [US5] Test Angular/native reviewed commit-result and delayed-retry handling in tests/e2e/import-retry.spec.ts and ios/RecipeManagerUITests/ImportRetryUITests.swift; share coordinator resolves existing result first, never falls back to an unbound commit, day-six retry creates no duplicate after lost response.

**Checkpoint**: US5 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 10: User Story 6 - Organise and find recipes (Priority: P2)

**Goal**: Deliver many-to-many shared collections/tags and full search/filter on both clients.

**Dependencies**: US1 library/catalogue plus US4. Independent of planning/shopping/imports; may run earlier after recipe contracts stabilize.

**Independent Test**: Seed varied recipes; manage organisers, assign multiple memberships and search names/ingredient aliases/tags with filter combinations without deleting recipes on organiser removal.

### Tests — independent first wave

- [X] T117 [P] [US6] Write organiser/assignment/search contract tests in tests/RecipeManager.IntegrationTests/Recipes/RecipeSearchContractTests.cs; OR within tag/collection groups, AND across groups/text, case-insensitive aliases, archived exclude/include/only and favourite-only.
- [X] T118 [P] [US6] Write native search/filter presentation tests in ios/RecipeManagerTests/Recipes/RecipeSearchTests.swift for retained selection, paging, cancellation and no stale-result overwrite.

### Implementation

- [X] T119 [US6] Implement collections/tags and many-to-many links, migration and tenant uniqueness in src/RecipeManager.Core/Modules/Recipes/RecipeOrganisers.cs and src/RecipeManager.Infrastructure/Persistence/Migrations/20261103112039_RecipeOrganisers.cs. Model fields/constraints from data-model.md: Recipes / Collection, Tag: Name, normalized name; "Unique household name within each type"; Recipes / RecipeCollection, RecipeTag: RecipeId, organiser ID; "Unique association; removal deletes only link".
- [X] T120 [US6] Implement organiser CRUD/link updates, duplicate-link preservation and parameterized case-insensitive name/ingredient/alias/tag search in src/RecipeManager.Infrastructure/Recipes/RecipeOrganiserStore.cs and src/RecipeManager.Api/Endpoints/RecipeOrganiserEndpoints.cs; empty filter reset, cursor pagination, no private folders/nesting, update client contracts.
- [X] T121 [P] [US6] Implement Angular collection/tag management, multi-assignment and combined search/filter UI in src/RecipeManager.Web/src/app/features/recipes/recipe-filters.ts and src/RecipeManager.Web/src/app/features/recipes/organisers-page.ts.
- [X] T122 [P] [US6] Implement native collection/tag forms, multi-selection and recipe search/filter state in ios/RecipeManager/Features/Recipes/RecipeFiltersView.swift and ios/RecipeManager/Features/Recipes/OrganisersView.swift.
- [X] T123 [US6] Run Story 6 filter/alias/organiser deletion acceptance in tests/e2e/recipe-search.spec.ts and ios/RecipeManagerUITests/RecipeSearchUITests.swift; record results in specs/001-household-recipe-manager/validation/US6.md and complete US8 organiser parity checks.

**Checkpoint**: US6 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 11: User Story 7 - Operate the application at home (Priority: P2)

**Goal**: Deliver working self-hosting, persistence/recovery and usable accessible web/native operation.

**Dependencies**: Foundation deployment scaffold may be hardened earlier; final restore/UX verification requires US1–6/9 and US8 synchronization implementation (T089–T090), not the US8 final acceptance gate.

**Independent Test**: Clean setup/restart/backup-restore preserves records; native signed install reaches same HTTPS server; main workflows work without paid providers, on mobile/web and native accessibility.

### Tests

- [X] T124 [US7] Write PostgreSQL migration upgrade/pending-model and persistence/restore validation in tests/RecipeManager.IntegrationTests/Operations/MigrationTests.cs and tests/RecipeManager.IntegrationTests/Operations/RestoreValidationTests.cs; realistic synthetic recipes/events/allocations/list edits and persisted keys/images/handoff receipts/expired identity tombstones, no secret fixtures.

### Implementation and operational checks

- [X] T125 [US7] Implement multi-stage .NET/Angular image, non-root static/API host and one-shot migrator mode in deploy/Dockerfile and src/RecipeManager.Api/Maintenance/MigrateCommand.cs; no concurrent automatic app-start migrations.
- [X] T126 [US7] Complete production Compose health/readiness, db→migrator→app ordering, named DB/images/key volumes, mounted secrets and trusted TLS-proxy configuration in deploy/compose.yaml and src/RecipeManager.Api/Health/HealthEndpoints.cs; private DB, amd64/arm64, AI outage not liveness failure.
- [X] T127 [P] [US7] Document runnable TLS/config/setup/upgrade/paused-write pg_dump+files+keys backup/restore/rollback instructions and safe defaults in docs/self-hosting.md and docs/backup-restore.md; validate on clean 2-vCPU/4-GiB host, never delete original volumes in restore tests.
- [X] T128 [P] [US7] Document native macOS/Xcode tooling, App Group/signing IDs, device install and trusted LAN/VPN HTTPS access in ios/README.md and docs/ios-installation.md; Compose does not build iOS, no TLS bypass or production arbitrary-loads exception, both client delivery mandatory.
- [ ] T129 [US7] Verify responsive 320px/desktop layouts, keyboard/focus/contrast/screen-reader navigation and native Dynamic Type/VoiceOver/date/forms in tests/e2e/accessibility.spec.ts and ios/RecipeManagerUITests/NativeAccessibilityUITests.swift and ios/RecipeManagerUITests/DynamicTypeUITests.swift; fix feature-owned source files and record zero blocking issues in specs/001-household-recipe-manager/validation/accessibility.md.
- [X] T130 [US7] Execute clean deployment/restart/upgrade/restore and native app+extension signed installation scenarios from quickstart.md, recording commands and synthetic data checks in specs/001-household-recipe-manager/validation/US7.md; validate no-AI/paid-service core operation and recovery access.

**Checkpoint**: US7 independent acceptance passes on both clients, with any explicitly named integration gate closed before release.

## Phase 12: Polish & Cross-Cutting Concerns

**Goal**: Close all v1 acceptance, security, performance and publication gates; no new features.

**Dependencies**: All nine stories and their deferred completion gates must pass.

### Final verification and publication readiness

- [X] T131 Run full two-household resource/role/CSRF/image/import/SignalR/native-origin security regression in tests/RecipeManager.IntegrationTests/Security/FullIsolationTests.cs; include removal racing notifications, native startup credential restoration racing server/account changes, and invalid auth before sync 304, inspect logs/config/artifacts for credentials/private data.
- [X] T132 Execute critical scenario in both web→iOS and iOS→web directions including Share Extension, source refresh/manual reconciliation and exact totals in tests/e2e/critical-journey.spec.ts and ios/RecipeManagerUITests/CriticalJourneyUITests.swift; record SC-001–014/AC-01–21 in specs/001-household-recipe-manager/validation/acceptance-report.md.
- [ ] T133 Implement MP-SYNC-01 once and measure 100 samples per pairing/mode for Angular→Angular, Angular→iOS, iOS→Angular, iOS→iOS connected/reconnect/foreground at 500 recipes/two members on reference host, plus warmed search/calendar in tests/performance/household-workload.js; enforce p95 two seconds and record distributions/network/hardware in specs/001-household-recipe-manager/validation/performance.md, only add indexes/optimize where measured.
- [ ] T134 Validate OpenAPI and both generated clients, module boundaries, checked-in migrations, Linux and macOS pipelines, app+extension signing and pinned dependency licenses in scripts/validate-release.sh and .github/workflows/release-validation.yml; do not waive native failures or replace PostgreSQL race tests with mocks.
- [ ] T135 Prepare contributor/security/dependency notice guidance and license-decision record in CONTRIBUTING.md, SECURITY.md, THIRD_PARTY_NOTICES.md and docs/publication-readiness.md; obtain owner license selection before adding LICENSE or public distribution, do not invent a license or block unrelated implementation.
- [X] T136 Replace planned commands with verified exact commands/tool versions and link evidence in specs/001-household-recipe-manager/quickstart.md and docs/release-checklist.md; document all-session logout, explicit app-open share handoff, foreground-only sync, compound personal cache validator, revocation boundary and durable handoff lifecycle and all remaining release gates without claiming unrun tests passed.

**Checkpoint**: Shared prerequisites compile and their checks pass.

## Dependencies & Execution Order

### Phase Dependencies

Setup → Foundational → authenticated US4 → US1 recipe/catalogue/calculator.
US1 → US2 calendar/leftovers → US3 generation/reconciliation and sync API.
US3 → US8 native synchronization implementation. US1 independently unlocks US5 imports and US6
organisers/search; those P2 slices may run before the final P1 cross-client/share completion tasks.
Foundation → US9 capture/store; US5 → US9 import routing/end-to-end completion.
US1–7 plus US9 → US8 full parity gate. All nine stories → final release validation.

There is no implementation cycle: US8/US9 test harnesses/capture can be written early against
contract fixtures; their completion tasks explicitly wait for the real API/UI owners. US5 does not
depend on Share Extension. US1 initially supports unassigned organisers; US6 adds assignments/full
search and its completion is required before full Recipe Library release acceptance. Run US7
operational scaffolding progressively, but its final restore/accessibility validation waits for all
features. Use seeded data to test individual stories without completing every user journey.

### User Story Dependencies and Independent Outcomes

| Story | Priority | Required production inputs | Independent acceptance outcome |
| --- | --- | --- | --- |
| US4 | P1 | Foundation | Setup/invite/roles/removal deny cross-household access on both clients |
| US1 | P1 | US4, calculation foundation | Structured manual recipe CRUD and immutable serving preview |
| US2 | P1 | US1 snapshots/catalogue | Explicit planned/actual leftovers with no over-allocation/double count |
| US3 | P1 | US1 calculator, US2 source events | Correct editable shopping snapshots and conflict-safe reconciliation |
| US8 | P1 | Session/sync API; final gate US1–7/9 | Every core workflow native/web parity with reconnect/foreground recovery |
| US9 | P1 | Native foundation; final routing gate US5 | URL/text share handoff reaches review and commits once |
| US5 | P2 | US1 write/calculator, US4 AI settings | Reviewed URL/text imports; AI-off core operation remains intact |
| US6 | P2 | US1 library/catalogue | Multi-collection/tag organisation and matching/filter semantics |
| US7 | P2 | Foundation; final fixture data from feature slices | Repeatable deploy/restart/restore and accessible signed clients |

### Concrete Deferred Gates (No Cycles)

| Work range | Start prerequisite | Final completion prerequisite |
| --- | --- | --- |
| T001–T009 | None | Setup builds |
| T010–T022 | T001–T009 | Foundation checks |
| T023–T035 (US4) | T010–T022 | Extend security matrix after later endpoints |
| T036–T054 (US1) | T023–T035 | US6 completes organiser/search portions of library release |
| T055–T069 (US2) | US1 recipe/calculator ports | No shopping/import dependency |
| T070–T085 (US3) | US1 + US2 | T089–T090 for live native observer |
| T086–T091 (US8 infrastructure) | T080 + native session foundation | Can complete before imports/share |
| T092–T093 (US8 final parity) | T086–T091 | T101, T115–T116, T123, T130 plus all earlier core slices |
| T094–T099 (US9 capture) | Native foundation/session | No import implementation required yet |
| T100–T101 (US9 routing/acceptance) | T094–T099 | T112–T115 real imports/review |
| T102–T115 (US5 core) | US1 write/catalogue + US4 settings | No US8/US9 dependency |
| T116 (US5 share replay integration) | US5 core plus T100 share coordinator | May run alongside T101; does not block T100 |
| T117–T123 (US6) | US1 library/catalogue | No planner/import dependency |
| T124–T128, T130 (US7 deployment/install) | Setup/foundation; operational work can progress early | Core features, T100 capture routing, T089–T090 native session/sync; signed installation and reference-host operational evidence |
| T129 (US7 accessibility) | Functional web/native workflows | T101 physical share/VoiceOver where applicable, current/minimum OS and zero blocking accessibility issues |
| T131–T136 | All prior story completion gates | All release evidence |

T092 is extended incrementally as features arrive; mark complete only after its full matrix runs.
T035 similarly establishes authorization coverage early and adds later endpoints without blocking
those endpoints from being built. Integration completion does not mean scaffold tests counted as
passing real workflows. Run US5 before T100; never stall at that forward dependency.

### Within Each Story

Define tests first; models/migrations → use cases → endpoints/generated contracts → parallel web
and native presentation → independent journey validation. Do not mark tests done just because
files exist. Story-owned UI tests can be written before implementation using public contract
fixtures; final runs use the real backend. Run relevant existing critical tests after each slice.
For revocation, acquire per-user dispatch gate before database locks. Otherwise acquire household
before sorted aggregate locks for shared writes; never run external
imports while holding list/source locks. Use PostgreSQL connections/barriers for concurrency tests.
No separate service, generic merge framework, offline domain sync or frontend business calculator.

### Parallel Opportunities and Examples Per Story

Run only after the indicated common prerequisite. These are task-execution examples, not an
instruction to spawn agents during task generation.

- **US4**: T023 + T024 can define separate tests together; T032 + T033 can implement separate Angular/native files after their API/types.

- **US1**: T036 + T037 can define separate tests together; T047 + T048 can implement separate Angular/native files after their API/types.

- **US2**: T055 + T056 can define separate tests together; T065 + T066 can implement separate Angular/native files after their API/types.

- **US3**: T070 + T071 can define separate tests together; T081 + T082 can implement separate Angular/native files after their API/types.

- **US8**: T086 + T087 can define separate tests together; implementation tasks remain sequential; tests share only stable contract fixtures.

- **US9**: T094 + T095 can define separate tests together; implementation tasks remain sequential; tests share only stable contract fixtures.

- **US5**: T102 + T103 can define separate tests together; T113 + T114 can implement separate Angular/native files after their API/types.

- **US6**: T117 + T118 can define separate tests together; T121 + T122 can implement separate Angular/native files after their API/types.

- **US7**: T127 + T128 can document/test independent server restore and native signing/install paths after T126; T130 validates deployment and signed installation after both; T129 retains its independent accessibility and physical VoiceOver gates. Completing installation does not close T101/T129.

Shared files (DbContext, migration snapshot, OpenAPI artifact, generated-client folders, app routing,
Xcode project/schemes and DI composition) are serialization points even when feature files differ.
Merge/regenerate these centrally after each API/model change; never run two migration generators or
overwrite generated clients concurrently. Independent cross-story branches: US5 and US6 after US1;
US9 capture after foundation can run alongside domain slices, but final routing still waits for US5.

## Implementation Strategy

### MVP First — Internal Validation, Not Reduced v1 Scope

First demonstrate setup/invites plus structured manual recipes on both Angular and SwiftUI
(US4+US1). This is an internal vertical slice, not permission to ship a web-only or reduced MVP.
All nine stories, Share Extension, both clients and SC-001–014 are mandatory for v1. Preserve
existing recipe/ingredient/meal/leftover/shopping requirements throughout incremental delivery.

### Incremental Delivery

1. Foundation and first-run authenticated shells for both clients; native Share Extension scaffolding.
2. Recipe/catalogue/calculator slice on both clients; start organiser and import branches.
3. Completed import/review and native share capture/handoff, then explicit calendar/leftovers.
4. Shopping generation/editing/reconciliation plus Angular SignalR and native foreground sync.
5. Finish organiser/parity integration, accessible UX and clean deployment/restore/signing evidence.
6. Release only after every acceptance gate, both CI pipelines and owner license selection for
   public distribution. No implementation task authorizes publishing, messaging others or deployment
   to an existing production installation without an applicable user instruction.

This roadmap follows the plan's parallel-client milestones; numbered story phases remain the
ownership index, while the dependency graph supplies actual executable scheduling. Keep each change
small and independently verified; no feature outside the agreed MVP is introduced here.

## Notes

Tasks are generated work, not completed work. All model/transport limits and business invariants
remain those in spec.md/data-model.md/contracts. Tests are expressly required, including Swift
presentation, native API/UI/share, exact calculations, PostgreSQL races and cross-client acceptance.
Owner license selection and signing team/provisioning are release inputs, not invented credentials.

## Task Counts and Requirement Coverage

**Total**: 136 tasks; **parallel-marked**: 41. All tasks remain unchecked.

| Area | Task count |
| --- | ---: |
| Shared | 28 |
| US4 | 13 |
| US1 | 19 |
| US2 | 15 |
| US3 | 16 |
| US8 | 8 |
| US9 | 8 |
| US5 | 15 |
| US6 | 7 |
| US7 | 7 |

### Functional Requirement Coverage

Every FR has concrete server/domain or operational ownership and dependent client/test work;
shared parity checks supplement rather than replace feature implementation.

| Requirement | Implementation/test task IDs |
| --- | --- |
| FR-001 | T044, T046, T049, T050, T120 |
| FR-002 | T042, T045, T047, T048 |
| FR-003 | T119, T120, T121, T122 |
| FR-004 | T120, T121, T122 |
| FR-005 | T044, T049, T050, T079 |
| FR-006 | T044, T120 |
| FR-007 | T044, T049, T050 |
| FR-008 | T044, T062, T064 |
| FR-009 | T042, T047, T048 |
| FR-010 | T039, T040, T047, T048 |
| FR-011 | T041, T044, T046, T051, T052, T054 |
| FR-012 | T036, T040, T046, T047, T048 |
| FR-013 | T040, T070, T074 |
| FR-014 | T039, T040, T036 |
| FR-015 | T044, T108, T109, T113, T114 |
| FR-016 | T103, T108 |
| FR-017 | T109, T110, T113, T114 |
| FR-018 | T028, T109, T115 |
| FR-019 | T061, T065, T066 |
| FR-020 | T061, T064 |
| FR-021 | T057, T061, T064 |
| FR-022 | T058, T062, T056 |
| FR-023 | T055, T062, T063 |
| FR-024 | T055, T063, T067, T068 |
| FR-025 | T056, T062, T063 |
| FR-026 | T060, T056, T067, T068 |
| FR-027 | T074, T081, T082 |
| FR-028 | T071, T074, T085 |
| FR-029 | T075, T081, T082 |
| FR-030 | T076, T081, T082 |
| FR-031 | T073, T074 |
| FR-032 | T077, T071, T081, T082 |
| FR-033 | T078, T083, T084, T089 |
| FR-034 | T012, T016, T131 |
| FR-035 | T027, T028, T030, T032, T033 |
| FR-036 | T018, T021, T047, T065, T066 |
| FR-037 | T129 |
| FR-038 | T125, T126, T127, T130 |
| FR-039 | T134, T135 |
| FR-040 | T026, T024, T032, T033 |
| FR-041 | T027, T023, T032, T033 |
| FR-042 | T027, T029, T024 |
| FR-043 | T055, T064, T065, T066 |
| FR-044 | T110, T113, T114 |
| FR-045 | T051, T052, T092, T132 |
| FR-046 | T008, T020, T040, T062, T074 |
| FR-047 | T019, T020, T033, T093 |
| FR-048 | T021, T093, T129 |
| FR-049 | T078, T083, T089, T090 |
| FR-050 | T095, T097, T100 |
| FR-051 | T096, T099, T100, T107, T116 |
| FR-052 | T005, T091, T095, T132 |
| FR-053 | T025, T029, T031, T078, T084 |
| FR-054 | T104, T106, T107, T110, T112, T116 |
| FR-055 | T072, T079, T088, T080, T089 |
| FR-056 | T001, T008, T012, T040, T044, T062, T074, T110, T134 |

### Success Criterion Coverage

| Criterion | Verification task IDs |
| --- | --- |
| SC-001 | T132 |
| SC-002 | T036, T040, T070 |
| SC-003 | T055, T056 |
| SC-004 | T133 |
| SC-005 | T133, T091 |
| SC-006 | T071, T077 |
| SC-007 | T131, T025 |
| SC-008 | T053, T069, T129 |
| SC-009 | T124, T130 |
| SC-010 | T129 |
| SC-011 | T092, T132, T054 |
| SC-012 | T101, T116 |
| SC-013 | T091, T072, T133 |
| SC-014 | T093, T129 |

### First-Class Backend Completeness Gate

Backend use cases precede their REST endpoints and generated contracts; both client implementations
follow. The core/backend slices are independently testable with seeded household/recipe fixtures,
real PostgreSQL and API calls without either UI running. Feature UI tasks do not implement domain
calculations or authorize actions. No backend function is delegated to a frontend stub.

| Backend concern | Explicit implementation and tests |
| --- | --- |
| Auth/setup/login/logout/password/reset/invites/roles/isolation/revocation | T011, T012, T023, T024, T026, T027, T028, T029, T025, T031, T030, T034, T131 |
| Recipe lifecycle/organisers/favourites/images | T042, T043, T044, T045, T046, T119, T120, T037 |
| Catalogue/aliases/categories/quantities/AU-US/exact serving rules | T039, T040, T041, T044, T046, T038 |
| Manual/URL/AI imports/drafts/review/handoff/retry/security/errors | T044, T103, T105, T106, T107, T108, T109, T110, T111, T112, T104 |
| Weekly planning/snapshots/planned-actual/leftovers/conflicts/concurrency | T055, T056, T057, T058, T059, T060, T061, T062, T063, T064 |
| Shopping source routes/scaling/consolidation/unknowns/manual/purchased/snapshots/reconcile/retry | T070, T071, T073, T074, T075, T076, T077, T080 |
| Household and personal notifications/sync/reconnection/revocation | T014, T078, T079, T080, T084, T072, T029 |
| REST/OpenAPI/validation/errors/logs/migrations/health/Compose/storage/secrets/restore | T011, T015, T017, T022, T124, T125, T126, T127, T130, T134 |

### Refinement Dependencies and Closure

The revocation tests precede the shared service; all Identity/logout/reset/removal/deactivation
paths and later hubs call it. Catalogue tests precede server contracts and both explicit catalogue
screens. Personal sync tests precede compound-validator/personal-dispatch implementation. Durable
handoff tests precede receipt model/migration/service, then import commit/endpoints, then native
handoff routing and late-retry UI validation. Existing deferred gates remain acyclic: US5 never
waits for US9, and operational verification waits only for US8 synchronization implementation,
not its final parity report. MP-SYNC-01 is implemented once by the performance task and reused
for all four pairings and recovery modes. Original task IDs were renumbered consistently to keep
execution-order IDs consecutive; all dependency references and coverage tables use the new IDs.

No ingredient bulk merge, extra identity provider, distributed lock service or offline domain
store has been introduced. The durable handoff index and in-process revocation gate are bounded
correctness requirements. License selection/signing inputs remain public-release administration,
not unresolved business requirements. All FR-001–056 and SC-001–014 have named task coverage.
