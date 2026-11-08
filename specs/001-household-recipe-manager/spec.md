# Feature Specification: Household Recipe Manager MVP

**Feature Branch**: None created; no branch-creation hook configured.

**Created**: 2026-11-01

**Status**: Ready for planning — mandatory web and native iOS v1 scope approved by the project owner.

**Input**: User description: A self-hosted recipe library, structured ingredient editor,
recipe importer, weekly meal planner with explicit leftovers, and shared editable shopping
lists for an initial household of two, with first-class Angular web and native SwiftUI iOS
clients plus an iOS Share Extension mandatory in version 1, suitable for open-source publication.

## User Scenarios & Testing *(mandatory)*

Priorities order delivery, not release eligibility: all stories below are in the requested
initial release, on both clients where applicable. Each story can be demonstrated with seeded household/recipe data without
completing the other user journeys. All shared workflows in Stories 1–7 require parity across
web and native iOS, not merely access to a web view. P1 establishes the core household workflow; P2 completes
importing, organisation, and operational usability.

### User Story 1 - Keep reusable structured recipes (Priority: P1)

As a household member, I can quickly enter and maintain recipes and their ingredients so
that cooking and shopping use quantities I can trust.

**Why this priority**: Structured recipe data is the foundation for planning and calculations.

**Independent Test**: With an authenticated household, manually create a recipe and exercise
its ingredient editor and serving preview without using importing or the calendar.

**Acceptance Scenarios**:

1. **Given** a blank recipe, **When** I enter title, description, image, default servings,
   preparation/cooking times, ingredients, ordered instructions, notes, and source URL and
   save, **Then** reopening preserves all supplied values and ordering.
2. **Given** an ingredient row, **When** I enter `1 1/2`, cups, rice, and `rinsed` in separate
   fields, duplicate it, group rows, reorder them, and remove one, **Then** each operation
   preserves the other rows and the quantity remains usable in calculations.
3. **Given** a recipe for two with 150 g rice and one capsicum, **When** I preview four servings,
   **Then** I see 300 g and two capsicums and the saved recipe still serves two.
4. **Given** a saved recipe, **When** I edit, duplicate, favourite/unfavourite, archive, and
   delete it in separate tests, **Then** edits persist, the duplicate is independently editable,
   the favourite choice persists, archived recipes remain retrievable, and deleted recipes
   disappear from the library subject to reference protection in FR-008.
5. **Given** blank title, non-positive default servings, negative times, negative quantities,
   or an invalid fraction such as `1/0`, **When** I save, **Then** field-level feedback identifies
   the invalid value and no invalid recipe is saved.
6. **Given** one Australian cup and one US cup of the same ingredient, **When** consolidated,
   **Then** the total is 486.588 ml before display rounding and both source recipes retain one
   cup in their respective standards; tablespoons and teaspoons use the factors in FR-014.
7. **Given** a manual recipe, **When** I first enter cups or spoons, **Then** Australian measures
   are selected; changing preview/display formatting does not change the saved original amounts.

### User Story 2 - Plan cooking and explicitly allocate leftovers (Priority: P1)

As a household member, I can plan how much to cook and when to eat it, without automatically
filling future meals or buying ingredients twice.

**Why this priority**: Preparing and eating different portions is central to this product.

**Independent Test**: Seed one recipe, then schedule cooking and leftover events and verify
availability and conflict handling without generating a shopping list.

**Acceptance Scenarios**:

1. **Given** an empty week, **When** I schedule four portions to prepare and two to eat on
   Monday dinner, **Then** Monday shows that cooking event, two portions are available for
   planned allocation, and Tuesday remains empty.
2. **Given** that cooking event, **When** I explicitly allocate two portions to Tuesday lunch,
   **Then** Tuesday references Monday's event and its unallocated planned balance is zero.
3. **Given** no remaining portions, **When** either member tries another allocation, including
   simultaneous attempts, **Then** the system rejects over-allocation and preserves a valid balance.
4. **Given** a cooking event with a two-portion dependent allocation, **When** I reduce prepared
   portions below the required total or remove the source event, **Then** affected allocations
   are identified and no conflicting change is committed before I resolve or cancel it.
5. **Given** a weekly calendar with recipe, leftover, and other meals, **When** I edit, move,
   duplicate, or remove a meal, **Then** the calendar and balances reflect only the accepted
   change; duplicating a leftover consumes a fresh allocation and cannot exceed availability.
6. **Given** planned cooking and leftover meals, **When** cooking and eating are confirmed,
   **Then** planned allocations remain distinguishable from actual production and consumption;
   planned quantities prefill optional actual confirmation, which may be corrected without an
   audit history; a reservation is counted once, not again when it becomes consumed.
7. **Given** a weekly view, **When** I add breakfast, lunch, dinner, a custom slot, or an
   `eating out` meal, **Then** each appears on its chosen date and slot; the other meal creates
   no ingredient or leftover requirements.
8. **Given** four planned prepared portions, two same-meal consumed, and two reserved,
   **When** I confirm five actual prepared and 1.5 actual consumed, **Then** the available actual
   balance is 1.5 and the two reserved portions are not shown as consumed.
9. **Given** that reservation, **When** I confirm two portions consumed as leftovers, **Then**
   it changes from reserved to consumed without another deduction and the balance remains 1.5.
10. **Given** reserved leftovers, **When** an actual correction makes availability negative,
    **Then** the conflict identifies affected meals and requires my resolution or cancellation;
    no scheduled leftover is silently changed and no audit-history workflow is required.
11. **Given** optional confirmation is unused, **When** I plan and generate shopping requirements,
    **Then** both work from planned quantities without requiring daily cooking records.
12. **Given** portion controls, **When** I step through 0.5, 1, and 1.5 or type 1.25 directly,
    **Then** valid values are retained; zero/negative preparation or allocation is rejected,
    while zero same-meal consumption is accepted for batch cooking.
13. **Given** a two-portion reservation, **When** I record 1.5 consumed, **Then** I explicitly
    retain or release the remaining 0.5 reservation; neither choice creates another meal.

### User Story 3 - Generate and share a reliable shopping snapshot (Priority: P1)

As a household member, I can derive a shopping list from cooking plans or selected recipes,
then shop together without losing manual changes.

**Why this priority**: Correct consolidated requirements deliver the main planning benefit.

**Independent Test**: Seed recipes and cooking events, generate each list type, and edit a
shared list from two signed-in sessions without importing recipes.

**Acceptance Scenarios**:

1. **Given** cooking events dated Monday through Wednesday and a Tuesday leftover event,
   **When** I generate for that inclusive range, **Then** only cooking events contribute,
   quantities scale to portions prepared, and no Tuesday leftover ingredients are added.
2. **Given** two selected recipes requiring 500 g and 250 g beef mince, 150 g and 0.2 kg rice,
   and one red capsicum and two red peppers recognised as aliases, **When** I generate,
   **Then** totals are 750 g beef mince, 350 g rice, and three red capsicums.
3. **Given** beef strips and beef mince, or rice measured in grams and cups without a validated
   conversion, **When** I generate, **Then** distinct ingredients or incompatible quantities
   remain separate and unresolved quantities are visible rather than guessed.
4. **Given** equivalent recipe quantities selected manually and through cooking events,
   **When** I generate separately, **Then** both methods produce equivalent consolidated totals.
5. **Given** a generated list, **When** I add a manual item, adjust a quantity, remove an item,
   or mark it purchased, **Then** the changes persist and the second member sees accepted
   changes without refreshing.
6. **Given** manual edits and purchased items on a generated list, **When** a source recipe or
   meal changes, **Then** the saved list is unchanged; requesting updated requirements shows
   additions, removals, and quantity changes for explicit reconciliation while retaining
   unrelated manual items, manual overrides, and purchase states unless I choose otherwise.
7. **Given** concurrent changes to one item or a disconnected member, **When** changes conflict
   or connectivity returns, **Then** the user sees the current accepted state and an explicit
   conflict/retry outcome rather than silently losing an accepted edit.
8. **Given** four planned but five actual prepared portions, **When** I generate normally,
   **Then** four portions determine requirements; explicitly choosing actuals in reconciliation
   previews five-portion requirements and changes the snapshot only after acceptance.

### User Story 4 - Share safely within a household (Priority: P1)

As one of two members, I can access our shared records while other households cannot.

**Why this priority**: Privacy is a release prerequisite for every shared workflow.

**Independent Test**: Seed two households, sign in as their members, and attempt reads, writes,
searches, imports, and live-update access across household boundaries.

**Acceptance Scenarios**:

1. **Given** two members of one household, **When** either creates a recipe, collection, tag,
   meal, or shopping list, **Then** both Administrator and Member can create, read, update, and
   delete shared content; only Administrators can manage membership and household settings.
2. **Given** a member of another household or a signed-out visitor, **When** they attempt to
   read or change those records, including by known identifiers or live updates, **Then**
   access is denied and no household content is disclosed.
3. **Given** a session that has expired or membership that has been revoked, **When** a member
   attempts another protected action or update subscription, **Then** access is denied and
   unsaved changes are not falsely reported as saved.
4. **Given** a new installation, **When** I complete the setup wizard, **Then** one household
   and its Administrator are created; simultaneous first-registration attempts cannot create
   a second initial Administrator and later public signup is unavailable.
5. **Given** an Administrator invitation, **When** a recipient accepts a valid unexpired link,
   **Then** they create their own credentials and join as a Member; invalid, expired, and reused
   links are rejected, as is joining a second household in v1.
6. **Given** each role in separate tests, **When** it edits shared content, **Then** all content
   CRUD actions are allowed; a Member cannot invite/remove members or change household settings
   or AI credentials, but can edit their own profile/preferences.
7. **Given** a connected Member with saved shared data, **When** the Administrator removes them,
   **Then** their access and live updates stop while shared data remains for the household.
8. **Given** the sole Administrator, **When** removal would leave no Administrator, **Then**
   the change is rejected with an actionable explanation.

### User Story 5 - Import and review recipes (Priority: P2)

As a member, I can bring in a website recipe or pasted text, review the result, and correct
it before it becomes part of our library.

**Why this priority**: Importing reduces entry effort but must not undermine trusted quantities.

**Independent Test**: Import controlled website and pasted-text fixtures, including incomplete
and failing inputs; verify that only reviewed recipes reach the library.

**Acceptance Scenarios**:

1. **Given** a supported recipe website URL, **When** I import, **Then** available recipe details,
   ingredients, quantities, and instructions appear in an editable review draft before saving.
2. **Given** pasted ChatGPT recipe text and configured AI assistance, **When** I import,
   **Then** separate ingredient fields and ordered instructions appear for review; missing or
   uncertain quantities are flagged and no invented quantity is represented as established fact.
3. **Given** a review draft, **When** I correct quantities, ingredient matches, servings, title,
   collections, and tags and confirm save, **Then** the saved recipe contains my reviewed values;
   cancelling review creates no library recipe.
4. **Given** AI is disabled, credentials are missing, or a provider fails, **When** I open import,
   **Then** AI unavailability is explained, entered text is retained for correction/retry, and
   manual recipes, website importing, planning, and shopping remain usable without a paid service.
5. **Given** an unreachable URL, a page without an extractable recipe, multiple detected recipes,
   or partial extraction, **When** import runs, **Then** I receive actionable feedback or a choice
   of detected recipes, can review any extracted fields, and no incomplete recipe is silently saved.
6. **Given** text to send to an external AI provider, **When** I initiate import, **Then** the
   external processing is disclosed and only the requested content is sent, with no unrelated
   household records or credentials included.
7. **Given** an import with identifiable US measures, **When** I review it, **Then** its standard
   and original units are retained; an uncertain or inconsistent source requires explicit
   measurement-standard confirmation before saving, not a guessed Australian conversion.

### User Story 6 - Organise and find recipes (Priority: P2)

As a member, I can organise a shared library using collections, tags, ingredient names, and favourites.

**Why this priority**: A growing library must stay usable on a phone.

**Independent Test**: Seed a varied library and verify organisation and search without a meal plan.

**Acceptance Scenarios**:

1. **Given** a recipe, **When** I assign two collections and several tags, **Then** it appears in
   each applicable result without being duplicated as separate recipes.
2. **Given** collections and tags in use, **When** I create, rename, or delete them, **Then**
   labels and assignments update consistently and removing an organiser does not delete recipes.
3. **Given** recipes with distinct names, ingredients, and tags, **When** I search each supported
   field or apply collection/tag filters, **Then** matching recipes appear, nonmatching recipes
   are excluded, and clearing filters restores the default library view.
4. **Given** an alias in the household ingredient catalogue, **When** I type it in the ingredient
   editor or search, **Then** the corresponding ingredient can be found without matching a
   genuinely different ingredient.

### User Story 7 - Operate the application at home (Priority: P2)

As a self-hosting operator and member, I can deploy, retain household data, and use the main
workflows on desktop and mobile without mandatory paid services.

**Why this priority**: Deployment and everyday usability determine whether the MVP is usable.

**Independent Test**: Follow deployment instructions on a clean supported host, create data,
restart, restore a backup, and exercise navigation on mobile and desktop.

**Acceptance Scenarios**:

1. **Given** the documented prerequisites and external configuration, **When** I follow setup,
   **Then** both members can sign in and use core features without an AI account or paid service.
2. **Given** saved household data, **When** the application restarts or a documented backup is
   restored to a clean installation, **Then** recipes, plans, allocations, and shopping edits survive.
3. **Given** a phone-sized or desktop browser, **When** I navigate Recipes, Meal Planner,
   Shopping Lists, and Settings and perform the core workflow, **Then** essential controls remain
   usable without horizontal page scrolling; keyboard and assistive-technology users can identify
   fields, errors, focus, and relevant live updates.

### User Story 8 - Use a complete native household client (Priority: P1)

As a household member, I can perform all core workflows in a native iOS app and continue the
same shared work in the web app, without feature gaps or a separate household data store.

**Why this priority**: Both fully functional clients are mandatory v1 deliverables.

**Independent Test**: With a seeded household and backend, run every core workflow in iOS and
verify its results from web, then reverse the direction. No Share Extension is needed for this test.

**Acceptance Scenarios**:

1. **Given** a new or existing installation, **When** I configure its address in iOS, complete
   initial setup or accept an invitation and sign in with my own credentials, **Then** I access
   the same household and role permissions as on web; removal revokes access on both clients.
2. **Given** an iOS session, **When** I browse/search/filter, create/edit/duplicate/archive/delete
   or favourite recipes, manage collections/tags/catalogue aliases, edit/reorder/group ingredients,
   upload an image or preview servings, **Then** each workflow preserves the same data and rules
   as Stories 1 and 6, and changes are visible in web.
3. **Given** either import method in iOS, **When** I submit a URL or pasted text, review/correct
   uncertain ingredients and measurement standards, and save, **Then** the same reviewed recipe
   is available in web; disabled AI does not disable manual or website entry.
4. **Given** iOS meal planning and shopping, **When** I exercise every action in Stories 2–3,
   including actual corrections, partial leftovers, snapshot refresh and reconciliation, **Then**
   the same calculation results, conflicts, editable snapshots and sharing behavior apply as web.
5. **Given** native navigation, forms, sheets and date controls, **When** I use Dynamic Type,
   VoiceOver and touch interaction, **Then** all critical journeys remain accessible and fully
   functional, while web remains responsive for desktop/mobile browsers.
6. **Given** an app backgrounded during another member's changes, **When** it returns to the
   foreground or reconnects, **Then** it refreshes accepted household state without silently
   overwriting unsaved edits; expired authentication requires sign-in without losing review input.

### User Story 9 - Share a recipe into import review (Priority: P1)

As an iOS user, I can select Recipe Manager in another app's share sheet and send a URL or text
into the normal import review workflow without copying it manually between applications.

**Why this priority**: Native capture is an explicit mandatory v1 workflow.

**Independent Test**: Share controlled URL/text fixtures through the system share sheet, open
Recipe Manager and review/save or cancel; verify no recipe exists before explicit save.

**Acceptance Scenarios**:

1. **Given** Safari sharing a recipe URL, **When** I select Recipe Manager and confirm handoff,
   **Then** the content is retained for the app's URL-import workflow and I can review and edit
   extracted fields before saving; the extension need not extract the recipe itself.
2. **Given** ChatGPT or another app sharing plain recipe text, **When** I send it to Recipe Manager,
   **Then** the app offers text import and correction before save, with AI availability disclosed.
3. **Given** the app is closed, signed out or cannot reach its server, **When** I share content,
   **Then** it is retained for explicit continuation after opening/signing in/reconnecting; it is
   not silently imported into a different account or installation and is never automatically saved.
4. **Given** unsupported/oversized content or cancellation, **When** sharing is attempted,
   **Then** the extension explains the limitation or cancels without creating a recipe.
5. **Given** a queued handoff, **When** the app resumes it multiple times or is interrupted,
   **Then** the same pending import is resumed rather than creating duplicate recipes; only
   explicit save commits it and cancel/discard removes it intentionally.

### Critical End-to-End Acceptance Scenario

**Given** two signed-in household members using web and native iOS, AI import configured, a known ingredient catalogue,
and two recipes whose expected quantities are recorded in a test fixture:

1. Paste a ChatGPT recipe; review and correct its structured ingredients.
2. Save it in a collection with tags.
3. Schedule four portions prepared and two eaten on Monday.
4. Explicitly allocate the remaining two portions to Tuesday leftovers.
5. Add another recipe as a Wednesday cooking event with an explicit preparation count.
6. Generate a shopping list for Monday through Wednesday inclusive.
7. Verify the result equals the sum of Monday's four prepared portions and Wednesday's
   prepared portions, consolidated by ingredient and compatible unit, with zero additional
   contribution from Tuesday.
8. Edit quantities, add a manual item, and mark an item purchased.
9. Verify the other member sees those accepted edits without refreshing and that a later
   source change requires explicit review before changing the list.

**Then** every step succeeds without hidden automatic leftover scheduling or duplicate requirements.
Run once with iOS as the initiating client and once with web; the other client verifies changes.
Also run the iOS journey starting from Share Extension URL/text handoff into import review.

### Edge Cases

- Unknown or `to taste` quantities remain explicit, scale without fabricated numbers, and appear
  as unresolved shopping requirements rather than disappearing or becoming zero.
- Zero denominators, malformed fractions, negative quantities, non-positive preparation counts,
  eaten counts above prepared counts, and allocations above availability are rejected.
- A zero same-meal eaten count is valid for batch cooking; leftover allocations must be positive.
  Fractional portions are supported with 0.5 UI increments and other positive decimal inputs.
  Zero consumption is allowed to represent batch cooking or no consumption; preparation and
  allocation quantities must be positive. This retains the existing batch-cooking exception.
- Conflicting aliases cannot silently merge ingredient identities; preparation notes alone do
  not establish an alias or an ingredient-specific conversion factor.
- A source cooking event outside the selected range contributes nothing merely because its
  leftovers are eaten inside the range; a source inside the range contributes its full prepared
  quantity even when leftovers are eaten later.
- Moving cooking after a dependent leftover meal requires conflict resolution. Cross-week
  allocations remain visible from the original event.
- Reducing confirmed production, undoing consumption, or deleting a referenced recipe/event
  cannot silently invalidate planned or confirmed records. Corrections require conflict
  resolution without automatic deletion or modification of scheduled leftover meals.
- Duplicating a cooking event creates new preparation requirements and no automatic leftover
  allocations. Repeating generation cannot mutate an existing list without explicit acceptance.
- An archived recipe remains available to existing references but is excluded from default
  search; deleting a collection/tag does not delete its recipes.
- Recipe edits after scheduling must not silently change the event's calculation basis; changes
  require an explicit update, with leftover and shopping impacts reviewed.
- Failed imports, failed saves, invalid image/source addresses, and interrupted connections give
  actionable feedback; untrusted imported content is not treated as executable application content.
- Website import rejects requests targeting private household/network resources and reports an
  unsupported source rather than treating the importer as an unrestricted network fetch tool.
- Concurrent allocation, ingredient catalogue edits, and list reconciliation must not create
  negative balances, duplicate identity mappings, or silently overwrite accepted edits.

- A native app returning from suspension refreshes server revisions; no background delivery or
  offline-first editing is assumed. Concurrent web/iOS edits use the same conflict rules.
- Share input may contain a URL plus text, multiple providers, an expired login or unknown server;
  user chooses the intended content/installation before upload. No credentials go in shared files.
- Switching account/server must not replay pending shared input against a different household
  without explicit confirmation; ordinary local share handoff is not an offline domain database.

## Requirements *(mandatory)*

### Functional Requirements

#### Recipes and organisation

- **FR-001**: Members MUST create, view, edit, duplicate, archive, delete, search, and favourite
  recipes, with each action retaining or removing data as demonstrated in Story 1.
- **FR-002**: Recipes MUST support title, description, image, positive default serving count,
  non-negative preparation and cooking times, structured ingredients, ordered instructions,
  notes, and optional source URL. A title and valid serving count are required; absent optional
  details MUST not be invented. Image upload/selection MUST allow replacement and removal.
- **FR-003**: Members MUST create, rename, and delete household-shared collections and tags;
  recipes MUST support multiple memberships in each without duplicate recipe records.
- **FR-004**: Search MUST cover recipe name, ingredient names including recognised aliases, and
  tags. Collection/tag filters MUST combine with search. Defaults for matching are in Assumptions.
- **FR-005**: Favourite choices MUST be retained per member; archive state MUST be household-shared.
- **FR-006**: Duplicating a recipe MUST copy its content, ingredients, groups, order, collections,
  and tags into an independently editable recipe without copying meal or leftover allocations.
- **FR-007**: Archiving MUST preserve content and existing plan references while excluding the
  recipe from default library results; members MUST be able to view and unarchive it.
- **FR-008**: Deletion MUST require explicit confirmation. Referenced recipe deletion MUST either
  preserve the referenced cooking details or be blocked with an explanation; it MUST NOT cascade
  into deleting meals, allocations, or shopping snapshots. The plan MUST select one safe policy.

#### Ingredients and calculations

- **FR-009**: Each ingredient entry MUST separate quantity, unit, ingredient identity/name, and
  optional preparation notes. Members MUST add, remove, duplicate, reorder, and group entries.
  Both clients MUST allow unspecified quantities without a unit selection; preserve an explicitly
  supplied unit, and continue requiring a unit for numeric quantities.
- **FR-010**: Quantity entry MUST accept decimals, simple fractions, and mixed fractions and
  support grams, kilograms, millilitres, litres, teaspoons, tablespoons, cups, and whole items.
- **FR-011**: Both clients MUST provide household ingredient catalogue browsing/search, canonical
  ingredient creation/editing/renaming, alias add/edit/remove, category assignment/change, and
  autocomplete. Names and aliases MUST share one household-unique normalized namespace; conflicting
  names MUST be rejected with actionable validation, never silently merged. Referenced ingredient
  deletion MUST be blocked with an explanation. Rename/category correction MUST preserve identity
  and existing recipe references, while event/list snapshots retain their prior data. Controlled
  ingredient merging is not an approved v1 workflow; no bulk merge endpoint is introduced.
- **FR-012**: Scaling MUST multiply numeric quantities by target servings divided by default
  servings without modifying the saved recipe; uncertainty and preparation notes MUST survive.
- **FR-013**: Consolidation MUST use catalogue identity and compatible units, convert kg/g and
  L/ml correctly, and keep incompatible dimensions separate. Weight/volume conversion MUST NOT
  be guessed; a validated ingredient-specific factor is required if such conversion is supported.
- **FR-014**: Calculation results MUST be deterministic, retain decimal precision without
  intermediate rounding, and expose unknown quantities. Each recipe MUST retain its source
  measurement standard, original quantities, and units. Manual entry MUST default to Australian
  measures: cup 250 ml, tablespoon 20 ml, teaspoon 5 ml. US measures MUST use cup 236.588 ml,
  tablespoon 14.787 ml, teaspoon 4.929 ml. Volume calculations and consolidation MUST use
  millilitres as the canonical unit. Rounding MUST affect display only, never stored quantities
  or later calculations; practical kitchen-friendly display rules and fixtures MUST be documented
  in planning and preserve access to the original editable quantities.

#### Importing

- **FR-015**: Manual entry, website URL import, and AI-assisted pasted-text import MUST be
  available in v1; imported fields MUST enter an editable review step before recipe creation.
- **FR-016**: Website import MUST extract available details, ingredient fields, and instructions,
  identify extraction failure/partial results, and allow selection when multiple recipes are found.
  Universal website compatibility is not promised; inaccessible pages MUST fail clearly.
- **FR-017**: AI text import MUST flag absent/uncertain quantities and ingredient matches instead
  of establishing guessed values as facts. The user MUST be able to correct all imported fields.
- **FR-018**: AI MUST be optional and provider-configurable; disabling or losing access MUST NOT
  disable other core workflows or website extraction. External processing MUST be disclosed and
  provider failure MUST preserve the user's review/input work for retry or manual entry.

#### Planning and leftovers

- **FR-019**: The calendar MUST show weeks with breakfast, lunch, dinner, and user-managed custom
  slots and support adding, editing, moving, duplicating, and removing scheduled meals.
- **FR-020**: Meals MUST distinguish cook recipe, eat leftovers, and other meal. Other meals MUST
  allow a descriptive label and MUST NOT create ingredient or leftover requirements.
- **FR-021**: Cooking events MUST record the selected recipe, calculation basis, date/slot,
  portions prepared, and portions eaten at that meal as separate quantities. Preparation MUST
  be positive; same-meal eating MUST be between zero and prepared portions inclusive.
- **FR-022**: Leftovers MUST be explicitly allocated from an identified earlier cooking event.
  Unallocated portions MUST NOT create calendar meals. Allocations MUST be positive and MUST
  NOT exceed the remaining balance, including under concurrent edits.
- **FR-023**: Each allocation's current commitment MUST equal confirmed consumed portions plus
  remaining reserved portions, counted exactly once. Null consumption means unconfirmed and
  contributes zero. Before source confirmation, availability MUST equal planned prepared minus
  planned same-meal eaten minus the sum of current commitments; afterward it MUST equal actual
  prepared minus actual same-meal consumed minus the same sum. Original planned allocation
  portions MUST remain for display/comparison and MUST NOT be deducted again. Releasing a
  reservation MUST reduce only its remaining reservation, never consumed portions; reload,
  correction and repeated release MUST NOT recreate the original reservation implicitly.
- **FR-024**: Planned allocation and confirmed production/consumption MUST be distinguishable.
  Confirmation MUST be optional and prefill planned prepared/eaten quantities. Members MUST
  record and later correct actual prepared and consumed quantities without an audit-history
  feature. Before production confirmation, availability MUST be labelled planned and use planned
  quantities. After confirmation, availability MUST use actual prepared minus actual same-meal
  consumed minus downstream consumed portions minus still-reserved portions. Each portion MUST
  be counted once as reserved or consumed. Partial consumption MUST explicitly retain or release
  the unconsumed reservation. Corrections MUST flag over-allocation and require user resolution
  before applying a conflicting change; scheduled leftovers MUST NOT be changed automatically.
- **FR-025**: Changes to cooking quantity, timing, or removal MUST check all dependent allocations,
  present conflicts, and require resolution or cancellation before committing an invalid state.
- **FR-026**: Recipe changes MUST NOT silently rewrite scheduled quantities or ingredient details;
  users MUST explicitly adopt updates with dependent allocation checks and shopping review.

#### Shopping lists

- **FR-027**: Members MUST generate a list from cooking events in an inclusive selected date
  range or from manually selected recipes with explicit serving counts. Both routes MUST use
  identical scaling, identity, and consolidation rules.
- **FR-028**: Planned prepared portions of cooking events MUST determine shopping requirements
  by default, whether or not actual cooking has been confirmed. Leftover and other meals MUST
  contribute zero ingredients; allocation dates MUST NOT change source inclusion. Recording or
  correcting actuals MUST NOT change saved shopping lists. Using actual prepared quantities
  instead MUST require an explicit regeneration/reconciliation choice, with differences reviewed
  and manual edits protected under FR-032.
- **FR-029**: Generated items MUST be grouped by ingredient category, including Produce, Meat,
  Dairy, and Pantry, with an explicit uncategorised group for items without a category.
- **FR-030**: Members MUST add manual items, adjust quantities, remove items, and mark/unmark
  purchased items; accepted changes MUST persist and be shared within the household.
- **FR-031**: Generated lists MUST remain editable snapshots retaining their source selections
  and generated baseline separately from user edits, sufficient to explain later reconciliation.
- **FR-032**: Members MUST request a comparison with updated source requirements and explicitly
  accept or reject changes. Manual additions, removals, quantity overrides, and purchase states
  MUST NOT be silently discarded; deleting or archiving a source MUST NOT erase a saved list.
- **FR-033**: Household shopping changes MUST appear to connected members without manual refresh;
  reconnection MUST recover current accepted state. Conflicting edits MUST be surfaced for retry
  or resolution rather than silently replacing an accepted edit with stale data.

#### Household, experience, and operation

- **FR-034**: All household-owned records and live updates MUST require authenticated, authorised
  household membership. Household A MUST NOT discover, read, modify, or receive household B's data.
- **FR-035**: Recipes, collections, tags, catalogue, meal plans, and shopping lists MUST be shared
  within a household. Both Administrator and Member MUST have full create, read, update, and
  delete permissions for this content. Only Administrators MUST manage membership and household
  settings, including AI credentials; each member MUST manage their own profile and preferences.
  These permissions and household isolation MUST be enforced on the server.
- **FR-036**: Navigation MUST provide Recipes, Meal Planner, Shopping Lists, and Settings. Ingredient
  editing and meal scheduling MUST support keyboard interaction without requiring drag gestures.
- **FR-037**: Core workflows MUST work in mobile and desktop browsers, with labelled controls,
  visible focus, accessible feedback, and usable touch targets, targeting WCAG 2.2 AA.
- **FR-038**: Operators MUST have documented setup, persistent storage, configuration, upgrades,
  migrations, backup, and restore procedures. Secrets MUST be externally configurable, never
  committed defaults; ordinary household use MUST require no paid external service.
- **FR-039**: The release MUST remain suitable for public source publication, with no private
  household fixtures or secrets; license selection and third-party attribution review are
  release prerequisites rather than additional end-user features.

- **FR-040**: A simple first-run wizard MUST create the first account, its household, and its
  Administrator membership together. Only one first registration MUST succeed, including under
  concurrent requests. After setup, public registration MUST be disabled; v1 MUST NOT provide
  public signup, social login, or complex permission management.
- **FR-041**: Administrators MUST invite users through time-limited invitation links. A valid
  invitation MUST let its recipient create personal login credentials and join the inviting
  household as a Member. Expired, invalid, or already-used links MUST NOT grant membership.
  Invitation lifetime MUST be explicit when issued; the configured default is a planning detail.
- **FR-042**: Administrators MUST remove members without deleting shared household data. Removal
  MUST revoke protected access, including active sessions and live updates. v1 MUST allow one
  household per user while preserving membership as an explicit relationship extensible to
  multiple memberships; cross-household invitation acceptance MUST NOT bypass this restriction.
  Removing or demoting the last Administrator MUST be blocked to avoid an unmanaged household.
- **FR-043**: Planned and actual prepared, consumed, and allocated portion quantities MUST support
  fractions including 0.5, 1.5, and 2.5. UI increment controls MUST step by 0.5; direct input MUST
  accept other positive decimal quantities within documented precision limits. Preparation and
  allocations MUST be positive; consumption MAY be zero but MUST NOT exceed available portions.
- **FR-044**: Imported recipes MUST preserve identifiable source measurement standards. If the
  standard is uncertain, review MUST require user confirmation before saving rather than silently
  assigning Australian measures. Mixed or inconsistent units MUST be resolved explicitly during
  review, preserving original quantities and units for editing.

#### First-class clients and native share capture

- **FR-045**: Version 1 MUST include two fully functional clients: the Angular web application
  and a native SwiftUI iOS application. Both MUST support all core workflows in FR-001–044,
  including content deletion/favourites, actual corrections, explicit refresh, reconciliation,
  and role-appropriate settings; neither client may be a prototype or deferred release item.
- **FR-046**: Both clients MUST operate on the same server-managed accounts, household data and
  business rules. iOS MUST NOT independently implement ingredient calculations, leftover balance
  decisions or shopping consolidation; offline-first operation is not required.
- **FR-047**: iOS MUST support configuring a self-hosted server address, setup, authenticated
  requests, invitations, logout, session expiry and removal. Credentials MUST be stored securely
  using Keychain and MUST NOT appear in logs or ordinary preferences/shared handoff storage.
- **FR-048**: iOS MUST provide native accessible navigation, forms, sheets, date selection and
  controls. Web MUST retain responsive desktop/mobile usability. Both MUST preserve unsaved edits
  when shared-state refresh requires conflict resolution.
- **FR-049**: Both clients MUST receive household updates while active, including shopping changes,
  and recover current state after reconnection or foreground return without manual refresh.
  Real-time transport may differ by client; the authoritative state and permissions MUST not.
- **FR-050**: Version 1 MUST include an iOS Share Extension accepting recipe URLs and plain text
  from Safari, ChatGPT and other apps that expose those content types. It MUST hand off to the
  normal import workflow without requiring extraction or AI processing in the extension.
- **FR-051**: Shared content MUST remain reviewable/editable before explicit recipe save. Pending
  handoffs MUST survive app closure or sign-in requirements, support cancellation, avoid duplicate
  commits, and require confirmation before use with a different account or server.
- **FR-052**: Release verification MUST include Swift client-state/presentation unit tests,
  shared API integration tests using native client workflows, native UI journey tests, Share
  Extension handoff tests and bidirectional web/iOS acceptance scenarios.

#### Consistency refinements approved before implementation

- **FR-053**: Logout, password change/reset, account deactivation, membership removal and any
  security-stamp rotation MUST revoke protected API and SignalR access consistently. Connections
  and hub methods MUST validate active authentication and household membership. Revocation MUST
  prevent new broadcasts to revoked connections, cancel pending dispatch and disconnect them;
  concurrent broadcast/revocation MUST have a defined ordering. Expired/revoked credentials MUST
  fail reconnection. Updates already dispatched before the revocation boundary cannot be recalled.
- **FR-054**: Share-origin imports MUST retain a durable, authenticated handoff-to-commit
  association for at least the entire seven-day handoff lifetime, independent of draft/ordinary
  retry-cache expiry. Concurrent/repeated commits for a handoff MUST create at most one recipe
  and return the original commit result. Uncommitted expired handoffs MUST fail without creation;
  an already committed result MUST remain recoverable after expiry. A deleted result MUST be
  reported as deleted, never recreated by replay. Retention/cleanup MUST preserve these guarantees.
- **FR-055**: Synchronization cache validation MUST include household content revision and the
  requesting user's personal preference revision. Favourite/preference changes MUST invalidate
  that user's devices without changing other members' personal cache validators. Personal
  notifications MUST be sent only to the authenticated user's connections, never household-wide.
- **FR-056**: The ASP.NET Core modular monolith MUST implement authoritative authentication,
  permissions, recipe/catalogue lifecycle, import/retry rules, serving/unit calculations, meal
  and leftover accounting, shopping consolidation/reconciliation, concurrency and notifications.
  Both clients MUST consume the same APIs/results; neither may implement separate domain rules.

### Key Entities *(include if feature involves data)*

- **Member / Household / Membership**: Authenticated person, isolated shared workspace, and the
  relationship granting Administrator or Member access; one household per user in v1.
- **Invitation**: Inviting household, recipient membership role, expiry, and acceptance state;
  creates no access until a valid invitation is accepted with a personal account.
- **Recipe**: Household-owned content, default servings, timings, image, instructions, notes,
  source, measurement standard, original quantities/units, archive state, collection/tag links;
  favourite is a member-to-recipe preference.
- **Ingredient catalogue entry / Alias / Category**: Household ingredient identity, alternative
  names mapping to that identity, and shopping grouping; distinct identities remain distinct.
- **Recipe ingredient / Ingredient group**: Ordered quantity, unit, identity, preparation note,
  uncertainty indicator, and optional grouping within a recipe.
- **Collection / Tag**: Household-managed organisers with many-to-many recipe membership.
- **Import handoff commit receipt**: Household/user-bound durable identity, expiry and original
  recipe commit outcome; independent of the transient review draft.
- **Import review draft**: Extracted editable fields and uncertainties awaiting explicit save;
  not yet a saved library recipe.
- **Meal slot / Scheduled meal**: Household calendar slot and dated entry of one of three meal types.
- **Cooking event**: Recipe calculation basis, prepared/eaten portions, planning state, and
  production/consumption confirmations; origin of all dependent leftovers.
- **Leftover allocation**: Source cooking event, destination date/slot, allocated portions, and
  planned/consumed state; never an independent shopping source.
- **Shopping list / Item / Source baseline**: Editable household snapshot, ingredient/category,
  quantity and purchase state, original source selection/calculation, and manual changes.
- **Local share handoff**: Device-local URL/text capture awaiting explicit import continuation;
  separate from saved recipes and never a second household database.
- **Reconciliation proposal**: Comparison of prior baseline, current requirements, and edits;
  applying chosen changes updates the snapshot without discarding unrelated edits.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Both initial members independently complete the nine-step critical scenario with
  zero incorrect consolidated quantities, zero automatically scheduled leftover meals, and zero
  duplicate ingredient requirements from leftovers.
- **SC-002**: All acceptance fixtures for kg/g, L/ml, fractions, serving scaling, recognised
  aliases, distinct ingredients, unknown quantities, and incompatible dimensions produce the
  expected results in 100% of runs, including Australian and US volume definitions, mixed-source
  consolidation, and original-quantity preservation through display rounding.
- **SC-003**: In all over-allocation and simultaneous-allocation tests, total reserved/consumed
  portions never exceed the applicable source balance and failed changes leave valid prior state.
- **SC-004**: In a household with 500 recipes and two active members, 95% of tested searches and
  weekly calendar opens show usable results within two seconds on the documented reference
  deployment; this is a proposed acceptance target, not a claim about an existing system.
- **SC-005**: Using measurement procedure MP-SYNC-01, at least 95% of foreground shared-update
  samples MUST appear within two seconds in each Angular→Angular, Angular→iOS, iOS→Angular and
  iOS→iOS pairing. Measure source commit acknowledgement to visible accepted state in the observer.
  The same procedure MUST verify reconnection and foreground-return catch-up within two seconds
  after the observer regains an authenticated, reachable server. No background delivery is required.
- **SC-006**: In every reconciliation fixture, zero manual additions, removals, overrides, or
  purchased states are lost without explicit user acceptance.
- **SC-007**: All cross-household and signed-out access scenarios deny access without disclosing
  protected content, including search, importing, direct record access, and live updates.
- **SC-008**: Both initial members can add ten structured ingredient rows, change serving preview,
  and schedule cooking plus leftovers without assistance on phone-sized and desktop screens;
  each reports the resulting quantities and leftover availability as clear in observed testing.
- **SC-009**: A clean deployment, restart, and backup/restore exercise preserves every test recipe,
  plan, allocation, and shopping edit using only the published instructions and no paid service.
- **SC-010**: The critical workflow can be completed using keyboard-only navigation; accessibility
  review finds zero unresolved blocking issues against the stated accessibility target.

- **SC-011**: Every core acceptance scenario in Stories 1–7 passes on both clients; no core
  workflow is web-only or native-only except platform-specific share capture.
- **SC-012**: URL and text shares from controlled Safari/ChatGPT-compatible fixtures reach editable
  native import review with zero recipes saved before confirmation and zero duplicate commits on retry.
- **SC-013**: Recovery scenarios from MP-SYNC-01 MUST converge to current accepted state without
  duplicate writes or silent loss of dirty edits, including cross-device favourites/preferences
  for the same user. SC-005 owns the single shared-update latency threshold; this criterion
  checks state correctness rather than duplicating a performance target.
- **SC-014**: Native critical journeys complete with VoiceOver and large accessibility text sizes,
  with zero unresolved blocking accessibility issues; logout/removal exposes no protected data.

## Assumptions

- Scope comprises all nine stories, not only P1. Native iOS and its Share Extension are mandatory
  in v1 alongside web. Nutrition tracking,
  pantry inventory, grocery price comparisons, supermarket integrations, social video extraction,
  advanced AI recommendations, subscription billing, and public recipe sharing are excluded.
- Users have connected web or native sessions; offline editing/synchronisation is not a v1 capability.
  Internet is needed for remote websites and external AI, not ordinary local household workflows.
- All shared content belongs to the household, not private member folders. Favourites are personal
  (proposed default). Collections are flat folders; nested hierarchies are not requested.
- Proposed search defaults: case-insensitive text matching; text matches any supported field;
  selected collection/tag filters are combined with AND, with OR within each selected filter group.
  Archived records require an explicit inclusion/view option.
- Meal dates use the household's configured local calendar date; Monday starts the weekly view.
  Multiple meals may share a date and slot. Custom slots are household-shared. Expiry estimates,
  food safety advice, waste tracking, and automatic leftover scheduling are excluded.
- Repeating a recipe in a manual source selection increases its requested quantity; no pantry
  stock or already-purchased goods are subtracted from freshly generated requirements.
- Unknown quantities may be saved after explicit review and must remain visibly unresolved.
  Unitless numeric quantities represent whole-item counts; descriptive quantity text is retained
  without fabricated numeric conversion.
- No ingredient density editor or AI-supplied conversion database is assumed for v1; unsupported
  cross-dimension conversions remain separate. Simple image upload is the proposed image workflow;
  image generation, editing tools, and media extraction are not included.
- Operator reference hardware/network, supported browser versions, image limits, numeric precision
  limits, provider compatibility, and website extraction fixtures are planning deliverables. They
  must be documented before the corresponding acceptance checks run; they cannot relax invariants.
- Authentication mechanism and deployment internals belong in the implementation plan under the
  constitution, not in this product specification. Open-source license selection remains an
  owner decision before distribution and does not add a licensing UI or public sharing feature.

### Resolved Product Decisions

- **D1 — Account and household lifecycle**: First-run setup creates an Administrator and the
  initial household. Further accounts join through time-limited Administrator invitations.
  Both roles edit all shared content; only Administrators manage membership and household-wide
  settings. Members manage personal preferences. Removal revokes access without deleting shared
  content. One household per user applies in v1; public signup, social login, and complex roles
  are excluded. Technical authentication and recovery mechanisms remain planning work.
- **D2 — Customary volume units**: Explicit recipe source standards with Australian defaults for
  manual recipes and exact supplied Australian/US factors in FR-014. Uncertain imports require
  confirmation. Millilitres are canonical for volume calculations; only display is rounded.
  Practical display formatting is a planning detail, not permission to alter original quantities.
- **D3 — Actual cooking and consumption**: Optional prefilled actual confirmation and later
  corrections, fractional portions, and no correction audit history. Planned balances support
  scheduling before confirmation; actual balances apply afterward. Reservations do not assert
  consumption and corrections cannot silently alter scheduled leftovers. Shopping uses planned
  preparation unless the user explicitly chooses actuals during regeneration/reconciliation.
- **Consistency interpretation**: Zero same-meal consumption remains valid for batch cooking;
  other portion entries are positive. Invitation reuse and last-Administrator safeguards follow
  safe defaults. Partial consumption requires an explicit retain/release choice for any remainder.
  These details support the stated workflow without introducing automatic leftover scheduling.

### Approved Scope Amendment — Native iOS in v1

The project owner explicitly replaces the former native-iOS deferral with mandatory SwiftUI and
Share Extension delivery. All previously agreed domain rules remain in force. Platform constraints
(SwiftUI, shared REST backend, secure Keychain storage) are explicit owner requirements, not an
invitation to duplicate business logic or add offline-first behavior. iOS availability is a release
gate; application distribution/signing details are addressed by the technical plan.

### Additional Independently Testable Acceptance Scenarios

- **A1 / FR-053**: For each revocation cause, establish authenticated hub connections, pause a
  broadcast before dispatch and race it with revocation. After the revocation boundary no new
  dispatch is allowed, queued sends are cancelled and connections close. Old credentials fail
  API/hub invocation/reconnection; expired credentials fail likewise. Re-login with valid active
  membership restores access. Already-dispatched frames are not misreported as post-revocation sends.
- **I1 / FR-054**: Commit shared input, lose the success response, expire/delete its 24-hour draft
  and ordinary retry record, then retry at day six. Return the original recipe/commit identity
  without another recipe. Two simultaneous commits produce one recipe. Uncommitted expired
  handoff returns an expiry error; committed retry after expiry returns its original outcome;
  deleted recipe returns a deleted-result error and is never recreated.
- **I2 / FR-055**: From two devices of user A and one device of user B, observe unchanged 304.
  Change A's favourite/preference: A gets 200/new personal revision on the other device and B
  still gets 304 if household content is unchanged. A household change invalidates both users.
  Revoked authentication fails before conditional cache evaluation.
- **C1 / FR-011,045**: In each client, browse/search, create, rename an ingredient, add/edit/remove
  aliases and change category. Conflicting canonical/alias names fail with field feedback; a
  referenced ingredient cannot be deleted. Correcting its name preserves recipe identity and
  all original cooking/list snapshots. No merge workflow is assumed.
- **A2 / FR-023**: With source capacity two, allocation planned two, consumed 1.5 and reservation
  0.5, availability is zero. Release the 0.5: availability becomes 0.5, consumed stays 1.5 and
  planned stays two. Allocate the freed 0.5 to another meal: availability returns to zero; repeat
  release/reload cannot free another 0.5. Corrections and simultaneous writes validate atomically.

### MP-SYNC-01 — Shared Update and Recovery Measurement

Use the plan's 2-vCPU/4-GiB SSD reference backend, 500 synthetic recipes, two active observer/writer
sessions and network RTT <50ms. For each of four client pairings collect 100 warmed samples per
mode: connected update, network reconnect, foreground return. Exercise shopping, recipe and meal
changes; add same-user personal changes for cache correctness. Measure connected latency from
successful write acknowledgement to observer rendering the acknowledged revision. For recovery,
changes happen while disconnected/backgrounded and timing starts when the observer becomes active
with a reachable authenticated server. Use one test-controller monotonic clock and instrumented
observer revision/render acknowledgements, not unsynchronized device wall clocks. Report p95 for
each pairing/mode separately against SC-005, never an average masking failures. Preserve dirty
forms with explicit conflicts and require eventual accepted-state convergence under SC-013.
