# Data Model and Invariants

All application/domain IDs are UUIDs; all tenant-owned roots include HouseholdId and bigint Revision.
Framework-owned internal Identity claim keys retain standard integer storage and are not public
household resource IDs. Timestamps are
UTC instants; meal dates are PostgreSQL date/DateOnly in household IANA timezone. Strings have
explicit bounds (names 200, descriptions/notes 20,000, URLs 2,048). Tenant-aware unique keys and
foreign keys use (HouseholdId, Id); indexes start with HouseholdId for normal queries. Delete
behaviors are restrictive unless an owned child has an explicitly reviewed cascade.

## Tables and ownership

| Owner / Entity | Fields and relations | Constraints / indexes |
| --- | --- | --- |
| Households / Installation | Singleton Id, SetupCompletedAt | Pre-seeded row; locked for first setup |
| Households / User | Identity user, email, normalized email, password hash, security stamp, IsActive, preferences, PreferencesRevision | Unique normalized email; no public registration |
| Households / Household | Name, timezone, configuration revision, ShoppingSourcesRevision, ContentRevision/domain counters, protected AI settings | One initial household; config Admin-only |
| Households / Membership | UserId, HouseholdId, role Administrator/Member, active state, revision | Unique UserId in v1, unique household/user pair; removal deactivates access |
| Households / Invitation | HouseholdId, token hash, expiry, accepted/revoked times, createdBy | Unique token hash; single-use transaction; role Member |
| Ingredients / Ingredient | Canonical display name, normalized name, CategoryId | Household normalized name unique; referenced deletions blocked |
| Ingredients / IngredientAlias | IngredientId, normalized alias, display alias | Unified name lookup uniqueness across canonical names and aliases within household |
| Ingredients / IngredientName | NormalizedName, IngredientId, isCanonical | Unique (HouseholdId, NormalizedName), enforces previous row's unified lookup |
| Ingredients / Category | Name, sort order | Seed Produce/Meat/Dairy/Pantry; uncategorised fallback |
| Recipes / Recipe | Title, description, image asset ID, default servings, prep/cook minutes, notes, source URL, standard AU/US, unit-policy version, archived | Positive servings; times nonnegative; title/search indexes |
| Recipes / IngredientGroup | RecipeId, heading, position | Unique recipe/position; optional group |
| Recipes / RecipeIngredient | RecipeId, IngredientId, group, position, quantity kind, original text, decimalValue or fraction numerator/denominator, unit code, preparation notes, uncertainty | Decimal numeric(18,6) or bigint fraction pair; unknown has no numeric value; stable row ID |
| Recipes / Instruction | RecipeId, position, text | Unique recipe/position |
| Recipes / Collection, Tag | Name, normalized name, Revision | Unique household name within each type |
| Recipes / RecipeCollection, RecipeTag | RecipeId, organiser ID | Unique association; removal deletes only link |
| Recipes / Favourite | UserId, RecipeId | Household membership required, unique user/recipe |
| Recipes / ImageAsset | Storage key, validated media type, size, owner household | Private authorized download; random key, no user file path |
| MealPlanning / MealSlot | Name, position, default/custom | Defaults breakfast/lunch/dinner; used slots archived, not cascade-deleted |
| MealPlanning / ScheduledMeal | Date, SlotId, type Cook/Leftovers/Other, label, revision | Index household/date; multiple per slot |
| MealPlanning / CookingEvent | ScheduledMealId, RecipeId, recipe revision, snapshot JSONB, plannedPrepared/Eaten, actualPrepared/Eaten nullable, confirmedAt, revision | One per cook meal; actual pair both null or both set; prepared>0, 0<=eaten<=prepared |
| Leftovers / Allocation | ScheduledMealId, SourceCookingEventId, plannedPortions, consumedPortions nullable, reservedRemainder, confirmation time | Source/date and balance enforced transactionally; nonnegative actuals/remainder |
| Shopping / ShoppingList | Name, source type, source selection JSONB, baseline JSONB, policy version, basis planned/actual, revision | No cascade from recipe/event; immutable baseline between explicit reconciliations |
| Shopping / ShoppingItem | ListId, ingredient/category snapshots, identity+dimension key, generated exact quantity, override exact quantity/text, manual flag, removed tombstone, purchased, position | Stable IDs; manual items never disappear on regeneration |
| Imports / ImportHandoff | HandoffId, HouseholdId, UserId, CapturedAt, RegisteredAt, ExpiresAt, InputHash, state Open/Committed/Expired, CommitHash, OriginalRecipeId, CommittedRevision, CommittedAt | Unique (HouseholdId, UserId, HandoffId); one immutable commit outcome; no raw text/credentials; retained independently of drafts |
| Imports / ImportDraft | RequestedBy, optional HandoffId, source kind/URL, extracted JSONB, uncertainty fields, standard status, expiry, revision | Household-private; 24h expiry; explicit commit creates recipe atomically |

AI settings contain provider kind/base URL/model plus encrypted credentials, never plaintext
returned to clients. Image limits: 10 MiB uploaded image, JPEG/PNG/WebP only, maximum 20 megapixels,
validate signature and decode/re-encode to strip metadata; reject SVG/HTML. Serve through authorized
asset endpoint with correct media type and nosniff. Backup includes assets and key ring.

Cross-module references use contracts and tenant-aware constraints, not arbitrary entity navigation
mutation. A single DbContext is infrastructure, not permission for modules to edit others' tables.
Snapshot JSONB has schemaVersion and immutable original ingredient IDs/names/units/factors, serving
basis and exact values. Alias edits do not retroactively remap snapshots; explicit recipe-basis
refresh can adopt updated identities; manual-recipe reconciliation can adopt current recipe identities.

## Numeric representation and v1 complexity decision

Use .NET decimal/PostgreSQL numeric(18,6) for entered decimal ingredient quantities, manual
numeric overrides and all portion quantities. Accept at most six decimal places; quantities
<=10^9 and portions <=10^6. Preserve original input text; reject invalid/overprecision input
rather than silently rounding. Fractions retain their original text and reduced bigint numerator
and denominator (input components <=10^9); decimal and fraction representations are mutually
exclusive, and unknown has neither. Numeric quantities are nonnegative and denominators positive. No numeric(78,0) columns or
custom EF BigInteger mapping.

Unknown quantities require no unit selection in either UI. Their existing non-null unit column
retains a supplied source unit or uses `item` as a neutral storage marker; no numeric count or
conversion is inferred from it. No migration or nullable-unit propagation is needed.

A small pure exact-fraction calculator is retained only for ingredient scaling/consolidation:
convert decimal inputs/factors to exact fractions, multiply by target/default servings, sum, then
format once. BigInteger is an implementation detail of this bounded calculator, not a general
numeric framework. Computed exact values in existing baseline/snapshot JSON use string numerator
and denominator; they are not relational numeric columns. Bound each computed component to 4096
digits and input batches to 1000 lines; reject an exceeded calculation limit without rounding or
partial persistence. Preserve source operands so every calculation can be reproduced.

A decimal-only calculator was evaluated and rejected under current FR-014: finite decimal cannot
represent 1/3, or scaling 1 gram from three servings to one, without intermediate approximation.
Three such contributions must consolidate to exactly 1 gram, independent of evaluation order.
AU/US conversion factors alone are finite decimals; arbitrary serving division and fractions are
the reason a small exact calculator remains necessary. A decimal-only implementation would require
owner approval of a numeric error tolerance and relaxing the display-only rounding requirement.
The constitution allows decimal, but that does not override the stricter approved specification.

Canonical mass is g, volume ml, count item; kg=1000g and L=1000ml. AU cup/tbsp/tsp=250/20/5 ml;
US=236.588/14.787/4.929 ml exactly. No density records or mass-volume conversion in v1. Display
rounding follows research.md; calculated display text is never reused as input. Source standards,
original text, unit and policy versions remain preserved. No binary floating-point business math.

## Recipe snapshots and explicit refresh

Creating a cooking event copies the current recipe revision into a complete immutable calculation
snapshot: title, default servings, ingredient row IDs/identities/names, original quantities/units,
measurement standard/factors, groups, instructions, notes and policy version. Planned/actual portion
counts are event fields, not mutations to that recipe snapshot. Persist protected image references
while snapshots use them. No historical recipe-version service is required; snapshot JSON is owned
by the event. The live RecipeId is a link, never the calculation input for an existing event.

Editing, archiving, renaming an ingredient/alias, or changing the live recipe does not change the
event snapshot, serving basis or list requirements generated from that event. Reopening/moving an
event or generating/reconciling its list does not implicitly refresh it. Manual recipe selections
use current recipe revisions, whereas meal-range lists always use event snapshots.

Only explicit `Refresh from latest recipe` previews a replacement snapshot. Apply requires expected
event and recipe revisions, preserves planned/actual counts and allocation links, validates dependent
balances/chronology, and bumps event revision atomically. Stale apply requires a fresh preview.
It replaces the current snapshot; no audit/version history is required. Existing shopping snapshots
remain unchanged and need separate explicit reconciliation. Referenced recipe deletion is blocked;
archiving remains available. Explicit refresh may adopt new ingredient identities; shopping
reconciliation alone cannot reinterpret an unchanged event snapshot.

## Leftover allocation invariants and transitions

For one source cooking event let P/E be planned prepared/same-meal eaten, A/C be confirmed
actual prepared/same-meal consumed. Source capacity K=P-E until confirmed, then K=A-C.
Preparation is positive, 0<=same-meal consumption<=preparation. Availability before confirmation
is labelled planned, not actual stock. Never sum planned and actual production together.

For each allocation i:
- plannedPortions q_i >0 is the currently scheduled meal intention, retained for comparison.
- consumedPortions c_i >=0 is the confirmed absolute amount, or null meaning unconfirmed
  (treated as zero in balance math). Correction replaces this value; it is not an increment.
- reservedRemainder r_i >=0 is the amount still reserved, disjoint from confirmed consumption.
- Its only balance commitment is c_i+r_i. q_i is never an additional subtraction.
- Available=K-sum(c_i+r_i) MUST stay >=0 under every committed command.

Create: set q=Q,c=null,r=Q after checking capacity under source lock. Partial confirmation takes
absolute consumed amount and explicit retained remainder: e.g. q=2, c=1.5, r=0.5 retains commitment
2; choosing release gives c=1.5,r=0 and releases 0.5. Release-all changes r to zero, leaves c and
q unchanged, and frees only the old r. Repeating release is a no-op on balances. q-c-r is not stored
or subtracted as a third value; planned intention may differ from actual after explicit correction.
Actual consumption above q is permitted only by explicit input and capacity validation.

No reservation is inferred afresh from q after consumption, correction, reload, release or shopping
generation. Editing planned portions after confirmation must explicitly specify the desired r;
it never erases actual consumption or restores released portions implicitly. The UI shows planned,
consumed and still reserved amounts separately, with an unfulfilled/released notice when appropriate.
Fully consumed allocations have r=0. Duplicate creates a fresh reservation and no consumed amount;
it cannot copy the original allocation's consumed commitment.

Confirming actual leftover consumption requires confirmed source production (the UI may offer a
combined explicit confirmation). Users may continue planning and generating shopping without any
actual confirmations. Clearing an allocation's consumption requires explicit desired r; it cannot
silently recreate the original reservation. Correcting source actuals, consumption or reservations
rechecks all dependents. Conflicts list affected meals; reject all proposed changes unless the user
supplies a valid explicit resolution. No automatic meal deletion or mutation. Deleting an allocation
requires explicit confirmation of any removal of consumed state; only its c+r is freed.

Source chronology requires earlier date or same date/later slot according to household slot order.
Moving sources locks both source rows sorted by ID. Every allocation create/edit/delete/confirmation/
release and source quantity change locks Household first, then CookingEvent with SELECT FOR UPDATE, checks revisions,
validates the complete proposed balance and bumps source revision, including child-only writes.
A stale revision fails, not an implicit retry of user intent. Source deletion is blocked until
explicit dependent removal/reassignment. Clearing source actuals is blocked while downstream
consumption remains. Revisions detect stale edits; no audit ledger or background stock calculation.

## Shopping generation, unresolved quantities and revision-based reconciliation

Generation freezes a consistent source selection: inclusive dates select only cooking dates, or
manual recipe IDs plus positive servings. Read all sources under the common authenticated household
transaction lock, then persist source revisions and baseline. The existing READ COMMITTED boundary
is consistent here because every requirement-affecting writer takes that same household lock;
do not open nested snapshot transactions. Default uses planned prepared amounts; explicit actual
basis requires confirmed actuals for every selected cooking source (otherwise validation error).

Group by snapshot ingredient identity and canonical dimension, not text. Different notes do not
split identical catalogue ingredients; notes remain visible as contributing-source detail. Unknown
amounts are nonnumeric requirements with stable source-selection/ingredient-row IDs, original
text, preparation note and source label. Display them in the ingredient's category as separate
checkable rows with an `Amount unresolved` badge, never a numeric zero. For salt requiring 5 g
plus `to taste`, show `Salt — 5 g` AND `Salt — to taste (amount unresolved)` with a summary badge
`Additional amount unresolved`. Neither known subtotal nor purchased state hides the unresolved
row. Multiple unknown source contributions remain inspectable; mark/remove each explicitly.
Manual numeric override of a known row does not resolve an unknown contribution; user must
explicitly supply its quantity/unit or remove it. Resolving it during review/reconciliation is
an explicit action. Known totals and unresolved counts are both included in list summaries. Category is display metadata, never a consolidation key.
Store contribution details per baseline item for explanation and later diff. Manual items have
independent IDs even if their labels resemble generated rows.

For old baseline B, new N and editable state U:
- No user override: propose N, with additions/removals shown before acceptance.
- User quantity override or removed tombstone: retain U by default; conflict displays B/N/U and
  permits explicit accept-new, keep-user or delete. No automatic delta onto an overridden quantity.
- Manual item: preserve unchanged unless user explicitly removes it.
- Purchased state: preserve on stable items; if increased requirements affect a purchased item,
  surface that choice rather than silently unchecking it. Removed generated rows remain visible
  in preview; item removal is explicit when applying approved choices.

Preview/apply is stateless: no Reconciliation table, persisted proposals, expiry service, signed
proposal token or general merge engine. Keep the existing list baseline and item edit flags.
Preview returns current list revision, source selection/basis, source revision vector, calculator
policy version, and deterministic B/N/U row differences. The apply request sends those versions
and per-row choices; the server recomputes all differences and validates allowed choices instead
of trusting any client-supplied totals or baseline. The preview is a DTO, not a persistent entity.

Use a coarse household ShoppingSourcesRevision on Household: every recipe/catalogue/category/
meal/actuals change that might affect requirements increments it in the mutation transaction.
This intentionally invalidates some unrelated previews; cheap re-preview is preferable to a
complex dependency index. It catches inserted/deleted/moved events within a date range, not just
revision changes to sources previously selected. Do not increment it on shopping-list item edits;
those bump their list revision. Include snapshot/recipe revisions for explanation, not extra
infrastructure. Unit-policy version mismatch also requires new preview.

Apply locks household first, then list, checks active membership, list ETag and source revision,
recomputes authoritative requirements and choices, and atomically stores baseline/items plus one
new list revision. Requirement-affecting writers acquire the same household lock before their
source locks and revision increment. No source can change between validation and commit. Send
notifications only after commit. Source/list changes return new-review-required (409/412) without
partial changes. Repeating apply with the old revision fails safely, so persistent apply-idempotency
records are not required; a lost-response client refetches current state. Creation retry keys may
remain separately scoped. No distributed locks, outbox or event ledger are added.

## Tenant and lifecycle integrity

Global query filters are defense in depth, not the only isolation mechanism. Every use case and
hub invocation checks active Membership; every tenant FK includes household ID; bulk operations
and raw SQL require explicit tenant predicates. v1 unique UserId membership can later be replaced
by a controlled migration; no multi-household selector is built. Removing members revokes session
and active connection access while preserving content attribution IDs. Membership removal and
last-admin checks lock household row; invitation acceptance locks household then invitation in
consistent command order. Use a single agreed lock order for all commands (household, sorted
aggregate IDs, invitation where applicable) to prevent opposite ordering.

## Native client and synchronization additions

No duplicate native household store or business tables. Angular and iOS use the same Identity user,
Membership, recipes, events, allocations, list snapshots and permissions. No calculations move into
Swift. Sessions are the existing Identity protected cookie ticket, with security-stamp and active
membership checks on every protected request. Native Keychain stores only the origin-scoped session
credential; CSRF is obtained per session and kept in memory. No custom OAuth/refresh-token tables.
Logout/password reset rotates security stamp to invalidate existing sessions across clients.

Add Household.ContentRevision (bigint) and domain counters RecipesRevision, PlannerRevision,
ShoppingRevision, SettingsRevision, MembershipRevision. Every shared create/update/delete increments
ContentRevision and the corresponding domain counter under the household row lock in the mutation
transaction; cross-domain writes increment each affected counter once. Counters contain no record
content; they let native conditional sync and Angular notifications trigger authorized resource
reads. Member personal preferences/favourites increment the same user’s PreferencesRevision transactionally
and never reveal another member’s preferences or increment household ContentRevision. ShoppingSourcesRevision remains
separate and narrower: shopping changes do not invalidate requirement-preview sources. No generic
change-log table, sync ledger or per-device replication cursor is required.

`GET /household/sync` checks current membership/security stamp before evaluating If-None-Match;
revoked users never get a misleading 304. Return authorized household/domain counters and the mandatory requesting-user
preference revision. The validator is the user-bound tuple (HouseholdId, UserId, ContentRevision,
PreferencesRevision), encoded as an opaque strong ETag. Never use ContentRevision alone. Personal
changes increment only PreferencesRevision and send PersonalChanged only to that user’s devices;
other members keep their validators. Use Cache-Control: private, no-cache and Vary: Cookie, with
client caches partitioned by origin/household/user. A household change invalidates all members. Personalized recipe/list responses containing own
favourite state also vary by UserId/PreferencesRevision; unchanged resource-only ETags cannot
hide changed personal fields during the subsequent resource refetch. Profile writes use own revision. Invalidating every dependent recipe/planner cache is conservative but safe.
Notifications sent after commit use these counters; reconnect/background return gets current state,
not event replay. Client dirty drafts retain their original ETags and must resolve stale writes.

### Device-local share envelope (not an EF entity)

App Group file: schemaVersion, handoffId UUID, kind URL|text, raw bounded content, createdAt,
optional confirmed server origin/user ID binding, status pending|submitted|reviewing, serverDraftId?
and stable idempotencyKey, capturedAt and registered handoff expiry/outcome metadata. File protection and coordinated atomic writes prevent partial handoffs.
No password/cookie/AI key is present. The app confirms destination before upload; different account
or server cannot inherit the prior binding silently. A serverDraftId is scoped to its origin and
household and is never replayed to a different installation.

Input limits mirror backend URL/text limits: URLs 2,048 UTF-16 code units and pasted text 100 KiB UTF-8. Queue is bounded to 20 pending envelopes with clear
full-queue feedback, seven-day expiry disclosed in UI and explicit discard. Handoff survives main
app termination/sign-in needs; it is removed after explicit discard or successful reviewed recipe
commit (or stated expiry), not merely on read. Retry always resolves the durable handoff status before draft recreation or commit. A 24-hour
draft or ordinary retry-cache expiry never erases a committed handoff result. An uncommitted
expired handoff cannot be registered again; expired drafts may be recreated under the same still
valid handoff only after explicit retry. Successful commit receipts outlive local queue deletion.
No domain offline storage, background imports or shared extension credentials are required.
The main app alone uses the authenticated handoff register/status/commit APIs described below. App Group entitlement/signing identifiers are configuration, never household IDs.

## Common session revocation and SignalR dispatch boundary (A1)

User.IsActive defaults true; ordinary account deactivation is an operator-local maintenance action
(no new public account-management role system). Block deactivation/removal of the last active
Administrator without another valid Administrator. Established Identity cookies remain the only
session mechanism. Every cookie carries user/security-stamp identity; registry entries also retain
user, membership, ticket expiry and connection IDs. No custom cryptography/session token service.

One SessionRevocationService handles logout, password change/reset, explicit stamp rotation,
membership removal and operator account deactivation. All six causes rotate the user's security
stamp; removal additionally deactivates membership, account deactivation sets IsActive=false.
This v1 one-household policy intentionally invalidates all that user's sessions. Validate IsActive,
current stamp, ticket expiry, active membership and role at connection establishment, every hub
invocation, every recipient dispatch and protected REST request. Never equate an open socket with
current authorization. Reconnection repeats validation; expired/revoked cookies fail closed.

Single API process uses a per-user async authorization/dispatch gate shared by registry and service.
Revocation acquires that gate before database locks; then cancels queued sends and drains/cancels
already-started dispatches with two-second per-send timeouts; cancel/drain completion precedes the durable revocation boundary. Under the gate it commits stamp/activity/
membership changes, marks all user connections revoked and aborts/removes them before returning.
All later dispatches acquire the same gate and revalidate committed DB state before sending. Thus
no send begins after the durable revocation boundary. Frames dispatched before that boundary may
arrive later due to transport buffering and cannot be recalled; tests instrument dispatch ordering,
not impossible network retraction. No broadcast path may bypass this dispatcher, including personal
notifications. Password maintenance commands invoke this same application service in the running
host via a host-local Unix socket (0600 plus mounted maintenance secret; no public TCP listener),
not a second process updating Identity
silently; an offline maintenance run requires stopping the API first so no live sockets remain.

Gate order: per-user revocation/dispatch gate (if needed) → household row → sorted aggregate rows.
Normal domain mutations recheck account/stamp/expiry/membership inside their transaction after
acquiring the household lock, so a write waiting behind revocation cannot use a stale pre-check.
They do not acquire the dispatch gate while holding DB locks; notifications
happen after commit. If a revocation fails before commit, no success is returned; after commit,
recipient checks remain fail-closed even if connection abort errors. A process crash destroys its
connections; restart revalidates persisted state. No broker, distributed locks, custom session DB
or multi-replica support is introduced. Registry timer closes expired tickets even when idle.

## Durable shared-import identity, transaction and retention (I1)

A device capture creates a UUID HandoffId retained across all draft/create/commit retries. On first
authenticated continuation the main app registers it with original CapturedAt and input hash under
current HouseholdId/UserId. Validate CapturedAt is not older than seven days or more than five
minutes in the future (clock correction required otherwise). ExpiresAt=min(CapturedAt+7 days,
RegisteredAt+7 days), fixed and server-returned; never extend on retry. Destination/account change
requires explicit confirmation and a new HandoffId, never rebinding an existing record. IDs do not
replace authentication. Enforce a tenant/user-bound lookup and unique key. Repeated registration resolves the existing
receipt before first-registration capture-time checks; expiry is never reset or identity reused.

Before extraction/retry, resolve handoff status. Open+unexpired permits creating/replacing an expired
24-hour review draft. Committed returns its immutable CommitResult {recipeId, committedRevision,
committedAt}; it never invokes extraction or creates a new recipe. Open expired becomes Expired and
returns 410 handoff_expired; replay cannot reopen it. A committed retry still returns original result
after expiry, because that is retrieval, not a new expired operation. If its recipe was deliberately
deleted, return 410 committed_recipe_deleted with original identity, never recreate it. Receipt
OriginalRecipeId has no cascading recipe FK; no receipt blocks recipe deletion.

At commit: lock household then handoff; authenticate owner and check bound input identity. Check
Committed before expiry/draft/precondition checks. For an already committed matching commit hash,
return original outcome; conflicting body returns 409 handoff_already_committed. If still Open,
require valid expiry, draft/review revision and reviewed recipe validation, create one recipe and
set immutable result/commit hash in the same EF/PostgreSQL transaction. A unique handoff key plus
row serialization prevents two recipes under concurrent commits. Atomic insert-on-conflict handles
concurrent initial registration. Canonical hashes use server-normalized input/review DTOs, not
client-calculated totals. A recipe committed under a handoff cannot be committed again through a
non-handoff draft endpoint: require its bound handoff on every commit path.

Retention: uncommitted draft bodies/external text expire at 24 hours; ordinary non-handoff retry
cache expires at 24 hours. Handoff receipt/outcome and expired-key tombstone metadata persist for
the installation's lifetime (no raw text or credentials); mark stale Open entries Expired after
seven days. Never purge committed result or identity tombstones with draft/retry cleanup. This
small durable index is justified by retry correctness; no history ledger/job broker is needed.
Backup/restore includes receipts. Local seven-day queue expiry discards input, not server outcomes.

## Catalogue correction contract (C1)

Both clients expose catalogue list/search/detail/create/edit/rename, alias add/edit/remove and
category assignment/change. Normalize Unicode/case/trim consistently and enforce IngredientName
uniqueness across canonical and alias names within a household, including concurrency. Rename
updates canonical display and lookup atomically while preserving IngredientId, recipe references
and alias identities; old canonical text becomes an alias only if explicitly requested and valid.
Alias edits update the unified lookup in the same transaction. Collision returns 409 name_conflict
with field-level feedback and no partial edits. Referenced ingredient delete returns 409
ingredient_referenced; no silent reassignment, cascade or bulk merge. If unused, delete its owned
lookup/alias rows. Category deletion keeps ingredients and moves them to uncategorised. Cooking
and list snapshots keep original identity/name/category details until explicit permitted refresh.

Creation retry response bodies use standard Data Protection encryption scoped to household, user
and route. This protects one-time invitation links in the 24-hour retry cache; permanent Invitation
records retain only token hashes. Status/location/revision metadata contains no token. Never store
raw credentials in the retry record. Expiry and the T111 cleanup policy remain unchanged.


## Implemented planner storage details

Cooking events and allocations carry fixed, explicitly initialised meal-type discriminators in
composite foreign keys to ScheduledMeal. This enforces Cook/Leftovers ownership in PostgreSQL and
lets EF order inserts of a new meal and its child correctly; a store-generated shadow default is
not used as the only in-memory discriminator. Cooking snapshots store per-line canonical factors
and dimensions alongside original exact amounts. Snapshot reference checks use household-scoped
JSONB containment rather than an extra dependency-index table.

Meal response validators combine the meal revision with its cooking source revision. An allocation
or source update therefore invalidates derived data even when another dependent meal's own revision
has not changed. Source validators separately fence aggregate corrections. Calendar/slot collections
use the household planner revision. Source-related mutations retain the existing household-first,
sorted-source locking order and increment planner/shopping-source counters in the same transaction.


## Implemented import draft representation

The existing extracted JSONB field retains the original typed `ImportExtraction` with all recipe
candidates. Review updates store an envelope containing that original extraction and the corrected
RecipeWrite. No new history table or snapshot infrastructure is required. Source input, groups,
original ingredient lines and units remain available; current review flags are stored separately.
The REST read DTO exposes typed `extraction`, `reviewedRecipe` and `uncertaintyFields`, avoiding a
duplicate serialized-JSON payload. PostgreSQL storage fields are not exposed as raw JSON strings.

Canonical/alias ingredient suggestions are computed after external extraction within the household
transaction through the Ingredients module's matching contract. Only exact normalized canonical or
alias matches receive a suggested ID/name. Missing and distinct names remain unmatched; review must
select or explicitly create a valid household catalogue entry. Provider-supplied ingredient IDs and
quantities are never authoritative. All committed recipe references use ordinary recipe validation.

### Collection/tag persistence and revisions

Collections and tags have separate household-scoped tables and normalized-name uniqueness within
each type. Composite household/recipe/organiser keys make assignments unique and prevent foreign
household links. Deleting a recipe or organiser cascades only its assignment rows. Renaming or
deleting an organiser advances every affected recipe revision plus household recipe/content
revisions, so validators and real-time invalidation reflect membership/name changes. Cooking-event
recipe snapshots remain immutable. Duplicate assignment IDs are normalized to one link; recipe
duplication preserves all distinct collection/tag memberships.
