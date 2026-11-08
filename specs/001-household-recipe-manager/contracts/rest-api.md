# REST API Contract Design

Base `/api/v1`, JSON over HTTPS for same-origin web and configured-origin native clients. This is the design contract; implementation must
publish a generated OpenAPI 3.1 document at `/openapi/v1.json`, commit its artifact, generate typed
Angular and Swift clients, and validate parity in CI. The checked-in generated document grows with implemented endpoints; the complete planned endpoint inventory below remains the acceptance contract.

## Common transport

Authenticated cookie required except setup status/completion, login, antiforgery bootstrap and
invitation acceptance. Anonymous setup status exposes only setupRequired. Unsafe endpoints require
antiforgery header `X-XSRF-TOKEN` obtained from bootstrap (including login/setup); rate-limit auth,
setup, invite and import actions. API auth failures are 401/403, not redirects. Cross-household
resource IDs return 404; unauthenticated requests return 401. Administrator policies apply as below.

IDs UUID strings; dates YYYY-MM-DD; instants ISO UTC. Enumerations are strings. Decimal amounts and
rational numerator/denominator are strings; no JSON numeric business quantities. Amount input accepts
`quantityText` with `kind: numeric|unknown`, `unit: g|kg|ml|l|tsp|tbsp|cup|item`, uncertainty and notes.
Numeric text accepts decimal, simple fraction and mixed fraction; portions accept decimal strings.
Responses contain exact numerator/denominator, displayText and isApproximate; display is never an
update value. Recipe standard `AU|US`, ingredient/source stable IDs and policyVersion accompany
calculation inputs. Nullable unknown numeric values are not zero.

GET detail returns a strong aggregate-revision ETag by default; user-personalized responses
(RecipeRead with favourite, favourite-filtered lists and profile) additionally bind user/personal
revision so personal changes cannot receive a stale 304. Sync uses the compound validator below. PUT/PATCH/DELETE and aggregate commands require
`If-Match`; 428 when absent, 412 for stale target revision. Domain conflict 409 contains affected
IDs/current balances; 422 contains field/path validation issues; malformed JSON 400, oversized 413,
unsupported media 415, throttled 429. RFC Problem Details fields: type, title, status, detail,
instance, code, traceId, errors[], conflicts[] (resourceId, expected/current revision, reason,
available, requested). Never include unrelated tenant content or secrets. Creation returns 201
and Location; updates 200 representation/new ETag; delete 204. Lists use cursor/limit (default 25,
max100) and nextCursor; calendar ranges maximum 366 days.

Ordinary creation/generation/non-handoff import-commit/confirmation accepts `Idempotency-Key` UUID;
store household/user/route/key, payload hash and response for 24h within transaction. Same key+body
returns prior outcome; changed body returns 409. Shared-import commits use the durable HandoffId
contract below instead of this expiring cache. No retries blindly repeat unsafe commands.

## Endpoint inventory

All paths below are relative to `/api/v1`; CRUD means GET collection, POST collection, GET detail,
PUT detail and DELETE detail unless a narrower set is stated. Owner household comes from session,
not request trust. Collections always tenant-filtered.

| Surface | Methods/path | Request → response / permission |
| --- | --- | --- |
| Setup | GET `/setup`; POST `/setup` | status; setupToken,email,password,householdName,timezone → current session; atomic initial Admin |
| Session | GET `/auth/csrf`; POST `/auth/login`, `/auth/logout`; GET `/auth/me` | CSRF bootstrap; credentials → user,role,household; logout revokes cookie |
| Profile | GET/PUT `/profile`; POST `/profile/password` | own name/preferences; current/new password; rotate sessions |
| Household | GET/PUT `/household/settings` | timezone, AI enabled/provider/model/base URL; PUT Admin only, credentials write-only |
| Membership | GET `/household/members`; DELETE `/household/members/{id}` | members; Admin removal, 409 last Admin |
| Invitations | POST/GET `/household/invitations`; DELETE `/household/invitations/{id}` | Admin issues/revokes; issue returns link once and expiry |
| Accept invite | POST `/invitations/accept` | token,email,password,name → personal session and Member; invalid/expired 422, used 409 |
| Ingredients | CRUD `/ingredients` | canonicalName,categoryId; search query includes aliases; referenced delete 409 |
| Aliases | POST/PUT/DELETE `/ingredients/{id}/aliases[/{aliasId}]` | alias; normalized collision 409 across catalogue |
| Categories | GET/POST `/ingredient-categories`; GET/PUT/DELETE `/{id}` | name/order; delete moves references to uncategorised |
| Recipes | CRUD `/recipes` | RecipeWrite/RecipeRead below; referenced delete 409, archive alternative |
| Recipe actions | POST `/{recipeId}/duplicate`; PUT `/{recipeId}/archive`; PUT `/{recipeId}/favourite` under `/recipes` | duplicate full content; archived bool; favourite bool (personal) |
| Organisers | CRUD `/collections`, `/tags` | name; deletion removes links only |
| Images | POST `/images`; GET/DELETE `/images/{id}` | multipart file → asset ID; authenticated household access; used delete blocked |
| Preview | POST `/recipes/{id}/serving-preview` | targetServings → exact scaled lines; no saved recipe mutation |
| Imports | POST `/imports/url`, `/imports/text`; GET/PUT/DELETE `/imports/{id}`; POST `/imports/{id}/commit` | URL or text/provider → review draft; corrected RecipeWrite → recipe |
| Slots | GET/POST `/meal-slots`; PUT/DELETE `/meal-slots/{id}` | name/order; used deletion archives; chronology conflict 409 |
| Calendar | GET `/meals?from=&to=`; GET `/meals/{id}` | inclusive date range → typed scheduled meals and balances; individual read returns meal ETag |
| Cook | POST `/meals/cooking`; PUT `/meals/{id}/cooking` | recipeId,date,slotId,plannedPrepared,plannedEaten; frozen recipe basis |
| Other | POST `/meals/other`; PUT `/meals/{id}/other` | date,slotId,label |
| Leftover | POST `/meals/leftovers`; PUT `/meals/{id}/leftovers` | sourceCookingEventId,sourceRevision,date,slotId,plannedPortions |
| Meal actions | DELETE `/meals/{id}`; POST `/meals/{id}/duplicate`; PUT `/meals/{id}/position` | explicit requested date/slot; duplicate cook creates new source, no dependent copies |
| Source basis | POST `/meals/{id}/recipe-refresh` | mode preview/apply, expected recipe/event revisions → frozen snapshot replacement only on explicit apply |
| Availability | GET `/cooking-events/{id}/leftovers` | planned/actual basis, remaining, reservations/consumed IDs and source revision |
| Confirm source | PUT/DELETE `/cooking-events/{id}/actuals` | actualPrepared,actualEaten,dependentResolutions[] → validated aggregate |
| Confirm leftovers | PUT/DELETE `/meals/{id}/consumption` | consumedPortions, reservedRemainder,sourceRevision, optional sourceActuals → aggregate |
| Atomic resolution | POST `/cooking-events/{id}/resolve` | expected revisions, proposed source change, explicit dependent edit/move/release/delete commands |
| Shopping | GET/POST `/shopping-lists`; GET/PUT/DELETE `/shopping-lists/{id}` | create manual list/name; detail includes baseline summary and item edit state |
| Generate | POST `/shopping-lists/generate` | source selection + basis → new snapshot list, never overwrite an existing list |
| Items | POST `/shopping-lists/{id}/items`; PUT/DELETE `/shopping-lists/{id}/items/{itemId}` | manual item; quantity/override,purchased; list ETag protects aggregate |
| Reconcile | POST `/shopping-lists/{id}/reconciliation/preview`; POST `/shopping-lists/{id}/reconciliation/apply` | source/basis override → revision-tagged preview DTO; versions and choices → revised list |

Recipe list query supports q, collectionIds[], tagIds[], archived=exclude|include|only,
favouriteOnly, cursor, limit. Search case-insensitive across title, catalogue canonical/alias names
and tags; OR within collection IDs, OR within tag IDs, AND between q/collection/tag filters.
Parameter values are escaped/parameterized; substring matching uses PostgreSQL ILIKE initially.
Add indexes based on reference-host measurements; do not introduce a separate search service.

## Principal DTOs and command semantics

**RecipeWrite**: title, description?, imageAssetId?, defaultServings string, prepMinutes?,
cookMinutes?, notes?, sourceUrl?, measurementStandard AU|US, groups[{id?,title,position}],
ingredients[{id?,groupId?,position,ingredientId,quantityText,kind,unit,preparation?,uncertain}],
instructions[{id?,position,text}], collectionIds[], tagIds[]. Unknown or uncertain entries require `acknowledgeUncertainIngredients: true` after explicit
review; identity creation is a separate catalogue action. RecipeRead adds id,
revision, archived, favourite, original/exact amounts, policyVersion. Full write preserves stable
row IDs and checks they belong to the recipe; arrays define one atomic ordered aggregate.
For `kind: unknown`, client unit selection is optional. The existing non-null wire/storage unit
uses `item` when no source unit was supplied; this is a neutral marker, not an inferred quantity
or a conversion basis. Preserve an explicitly supplied unit. Numeric quantities still require
a selected unit. Unknown quantities remain excluded from unit arithmetic.

**ScheduledMealRead**: id, revision, date, slotId, type; cooking has sourceRecipeId,
recipeSnapshotRevision, planned/actual prepared/eaten strings, confirmedAt and availabilityBasis;
leftover has sourceCookingEventId, plannedPortions, consumedPortions?, reservedRemainder, sourceRevision;
other has label. Moving/duplicating references validates the source transaction and chronology.
Source-related commands carry current source revision; 409 reports dependent conflicts, never an
automatic cascade. Multi-aggregate resolution carries all affected revisions and is atomic.

**ShoppingSourceSelection**: kind mealRange|recipes; for mealRange from/to; for recipes
entries[{recipeId,servings}]; basis planned(default)|actual. Actual only allowed for mealRange with
all selected cooks confirmed; missing actuals return field-level validation rather than fallback.

Implemented shopping request details: generation is `{name, selection, basis?}`. Manual lists use
POST `/shopping-lists` with `{name}`. Manual item creation accepts `{label, quantityText?, kind?,
unit?, measurementStandard?, categoryId?}`; omitted quantity is unresolved rather than zero.
Item PUT accepts optional `quantityOverride` (quantityText/kind/unit/measurementStandard),
`clearOverride`, `purchased`, `removed` and a manual-only `label`. Override replacement and clearing
cannot be combined. Known generated rows require compatible override dimensions; resolving an
unknown row requires an explicit numeric amount/unit. Item writes return the whole list and its ETag.
Manual item deletion removes that item; generated deletion retains a tombstone. Manual item retry
keys replay the original result before checking the original parent revision, preventing duplicates
after a successful response was lost.

Preview is `{selection?, basis?}` under the list ETag. Apply sends the returned list/source versions,
policy, selection/basis and `choices[{key,choice,purchased?}]`, where choices are `acceptNew`,
`keepUser` or `delete`. Every changed row must be reviewed once; unsupported/duplicate choices fail
before mutation. Accepting increased purchased requirements requires an explicit purchase-state
decision. Source changes return 409 `shopping_review_required`; list-version changes return 412.
Business validation, including unconfirmed actuals, uses the existing 422 field-error response.
List detail includes independent unresolved rows, effective and baseline exact amounts, source
contributions and a summary of known totals/unresolved counts. Shopping list responses contain no
personal state, so their resource ETags remain shared; personalized recipe/profile validators retain
their user preference revision.

**ShoppingItemRead**: id, ingredientId?, category snapshot, display label, baseline exact amount,
override?, effective amount/text, purchased, manual, removed, contributions[], unresolvedCount.
Unknown contributions are distinct checkable rows with stable source-row keys and original text;
known totals and their purchased states cannot hide additional unresolved rows. Update never accepts
calculated display as an authoritative amount implicitly. Deleting generated item stores tombstone;
manual deletion removes only that item. **ReconciliationPreview**: listRevision, householdSourceRevision,
sourceRevisions, policyVersion, selection/basis, changes[{key,oldBaseline,newBaseline,userState,conflict,allowedChoices}],
choices default keep-user for overrides. Apply sends explicit choice per changed row; unspecified
conflicts rejected. Any list or household source-revision change after preview returns 409/412 and requires re-review.
No proposal record or client-supplied calculated totals are trusted; recompute during apply under
household then list lock. Repeated apply with stale revision fails safely; refetch after lost response.

## Authorization contract

Admin and Member share full content CRUD. Profile is own-user only. Household settings/invite/member
removal require Admin and active membership; no role promotion/demotion API or public household
creation in v1. Role is part of membership, not a client supplied trust claim. Backend policies,
database tenant constraints and hub methods all enforce the same rule. Authentication credentials
and provider secrets never appear in GET responses; settings expose configured status only.

## Native client and household synchronization amendment

All existing content endpoints serve both Angular and SwiftUI without `/ios` duplicates. Preserve
DTO/ETag/Problem Details behavior, string quantities, snapshots and authoritative calculations.
OpenAPI-generated Swift and TypeScript clients must pass the same contract fixtures. Native uploads
use the same multipart image API; native previews/calculations use the server endpoints.

Native uses the same cookie login/setup/invitation response and CSRF bootstrap, over an HTTPS
self-hosted base URL. Its ephemeral URLSession cookie jar is restored from Keychain and updated
from Set-Cookie. Session cookies are never JSON payload fields and passwords are never persisted.
Native must reject cross-origin authenticated redirects. Include credentials only for the configured
origin/path and bootstrap a fresh CSRF token after session restore/renewal. All unsafe endpoints,
including native requests, retain antiforgery protection; native User-Agent is not a bypass.

Server validates current Identity security stamp and active membership for every protected request.
Login returns expiry metadata through `/auth/me` plus user/role/household; cookie is HttpOnly for
web and securely managed by URLSession for native. Session timeout is 14 days maximum with renewal
subject to validation. Logout invalidates all user sessions via security-stamp rotation and deletes
the issuing client's cookie; clients clearly label it as signing out all sessions. 401 requires new
login; no custom access/refresh token API is introduced. Rate limits apply identically to both clients.

| Surface | Method/path | Contract |
| --- | --- | --- |
| Foreground sync | GET `/household/sync` | If-None-Match user-bound (householdId,userId,ContentRevision,PreferencesRevision) ETag → 304 only after authorization, otherwise 200 {contentRevision,recipesRevision,plannerRevision,shoppingRevision,settingsRevision,membershipRevision,ownPreferencesRevision} |

Revisions are decimal strings to avoid JavaScript integer precision loss. Server bumps relevant
counters in the same transaction as each shared write. A sync reply contains no private content
or another member's preferences. On change, native refetches affected visible resources with existing
ETags; deleted resources invalidate cached views. On cold launch/foreground, validate `/auth/me` and
get sync revisions before using previously visible protected state. Account/server switch clears
session caches and requires confirmation for pending import handoffs. New invite acceptance and
setup work in either client; time-limited links can be pasted into native, with origin confirmation.
No arbitrary self-hosted domain is assumed to have universal-link association configured.

Import URL/text endpoints require the bound HandoffId plus stable retry key when resuming a
Share Extension handoff; durable outcome lookup takes precedence over ordinary cache expiry. URL/text input is not saved as a recipe until explicit draft commit. Extension does not
call these endpoints; the containing app does after destination selection and authentication.

## Refinement contracts: revocation, preferences, catalogue and handoffs

All six revocation causes use SessionRevocationService and the dispatch boundary in data-model.md.
POST logout and successful password change/reset rotate security stamp and disconnect all user
sockets; membership removal also revokes its membership; operator-only deactivation also sets
IsActive=false. Every protected request/hub operation checks account, stamp, expiry and membership.
There is no new public account-deactivation API. Reset/deactivation maintenance is offline with the
host stopped, or through the running host’s 0600 Unix socket with mounted maintenance secret; never a public
TCP endpoint. Responses do not
claim success before the revocation barrier; no subsequent household/personal send can start for
revoked credentials. Already-dispatched frames are outside that boundary.

Sync strong ETag represents exactly (HouseholdId, UserId, ContentRevision, PreferencesRevision).
Return 304 only if this whole tuple matches after authentication; 200 includes mandatory
ownPreferencesRevision otherwise. Use Cache-Control: private, no-cache and Vary: Cookie. Updating
/profile preferences or personal favourite increments only the requesting user's PreferencesRevision
and not household ContentRevision. Notify only that user's authorized connections; other members'
validators remain unchanged. Native device poll and Angular personal notification both refetch
protected own-state, preserving dirty local preferences and stale-write conflicts.

Catalogue detail includes canonicalName, aliases[{id,name}], categoryId and ETag. GET /ingredients?q=
searches canonical/alias names; POST/PUT /ingredients manages canonical identity/category with an
optional explicit retainOldNameAsAlias boolean. POST /ingredients/{id}/aliases adds, PUT
/ingredients/{id}/aliases/{aliasId} renames, DELETE removes. Every change requires target ETag and
valid household identity; alias update returns the revised ingredient ETag. A normalized name
collision returns 409 name_conflict and errors for canonicalName/aliasName; referenced delete
returns 409 ingredient_referenced with no cascade. Category reassignment preserves identity;
snapshots are immutable. No merge endpoint or implicit ingredient merge is supported in v1.

| Operation | Method/path | Request → result |
| --- | --- | --- |
| Register handoff | POST `/import-handoffs` | handoffId,capturedAt,kind,inputHash → bound status/ExpiresAt; repeat same input returns existing status, different input 409 |
| Resolve handoff | GET `/import-handoffs/{handoffId}` | owner-authorized Open/Expired/Committed and immutable CommitResult if committed; no draft body required |
| Extract/retry | POST `/imports/url` or `/imports/text` | optional handoffId (required for share-origin), normal input and retry key; committed result is resolved before provider work |
| Commit handoff | POST `/import-handoffs/{handoffId}/commit` | draftId,expectedDraftRevision,reviewedRecipe,commitHash inputs → 201 CommitResult first time, 200 original CommitResult on replay |

Handoff identifiers are tenant/user scoped and never authorize access themselves. Capture time
validation, immutable expiry and retention follow data-model.md. Server computes commit hash from
canonical reviewed DTO, compares body on retries and validates at first commit. CommitResult is
{recipeId,committedRevision,committedAt}; GET recipe supplies current content. Changed retry payload
returns 409 handoff_already_committed. After a lost response, GET status can retrieve original
outcome without reconstructing the body. Open expired or expired captured input returns 410
handoff_expired; committed retry after expiry returns original result unless recipe was deleted,
then 410 committed_recipe_deleted with original identity. Expired/missing review draft for Open
valid handoff returns 410 draft_expired and permits explicit new extraction under same handoff.

Normal /imports/{id}/commit must delegate to durable commit when draft has HandoffId; omitting or
changing its binding is 422 handoff_required/409, never a duplicate-creation fallback. First handoff
commit carries expectedDraftRevision; replay/status lookup is checked before stale-draft and expiry
conditions, so a deleted 24-hour draft cannot invalidate an existing committed result. Recipe create
and receipt persist atomically; parallel commits serialize. Cleanup never removes committed receipt
or expired-key tombstones. Owner authorization is checked even on replays and deleted-result replies.

Availability/allocation DTOs expose plannedPortions (display/comparison only), consumedPortions
nullable, reservedRemainder and derived commitment=consumed+reserved. Consumption PUT replaces
absolute values; explicit reservation release sets remainder to zero without altering consumed or
planned values. Original planned portions are never deducted in availability. A source revision
and aggregate If-Match protect every create/edit/release/confirm/correct path; stale edits fail and
all dependent changes are validated under the household/source transaction before commit.

The maintenance-only contract is local POST /maintenance/password-reset,
/maintenance/accounts/{id}/deactivate and /maintenance/accounts/{id}/rotate-stamp, bound solely
to the protected Unix socket listener, authenticated by mounted maintenance secret. These routes
are not mapped on the public listener or advertised as end-user APIs. Constant-time secret checking
uses established framework primitives; no custom token encryption. All invoke the common revocation
service. Keep socket and secret out of web/native clients and ordinary application responses; only the
backend host and operator maintenance invocation can access them.

Personalized recipe/list representations use ETags derived from resource/query revision plus
UserId/PreferencesRevision, with private caching; recipe writes retain resource revision validation
and may conservatively return 412 if a personalized validator became stale. Do not refetch a changed
favourite using an unchanged resource-only validator and incorrectly accept 304. Own-profile ETag
uses PreferencesRevision; profile/favourite commands update that counter and notify only the user.
On handoff re-registration, look up authenticated existing receipt before applying first-registration
capture-time checks: Committed returns its original result (or deleted-result error), Expired stays
410, Open retains fixed original expiry. First-registration capture checks cannot reopen an old ID.

Maintenance password-reset request is {userId,newPassword}; the operator command reads the new
password from protected input, not command-line arguments/logs. The host generates and consumes a
short-lived Identity reset token internally, then applies common revocation; no reset token or
password is returned. Deactivate/rotate requests identify the target user and preserve last-Admin
protection. If host is stopped, equivalent offline maintenance runs before its next startup.
InputHash is SHA-256(kind UTF-8 + NUL separator + original content UTF-8); no text normalization.
The server recomputes it when URL/text extraction is submitted and rejects a mismatch. It is a
content-binding checksum, not an authorization credential; all receipt operations require login.


## Implemented recipe/catalogue/image transport details

Quantity kinds serialize as `numeric|unknown`; unit enum members serialize using the documented
`g|kg|ml|l|tsp|tbsp|cup|item` codes. Ordinary manual recipes default to AU when measurementStandard
is omitted. Row IDs may be omitted for new rows; supplied row IDs must be new or owned by the same
recipe. New groups referenced by ingredient rows need a client-generated UUID in the write.
Whole-aggregate edits retain supplied stable IDs while replacing ordered owned rows transactionally.
Original quantity text is bounded to 200 characters; preparation notes retain the 20,000-character
note limit. Unknown text may be empty to represent an explicitly reviewed missing quantity.

Recipe detail and list ETags bind the requesting user and personal preference revision in addition
to shared recipe revisions. Preserve validators verbatim. A favourite change does not invalidate
another member's personal cache. Catalogue canonical/category corrections advance affected live
recipe revisions; existing cooking snapshots remain unchanged. All version checks occur again in
the household transaction. Alias mutations return the updated parent ingredient with HTTP 200 and
its new ETag; alias creation accepts the ordinary retry key. Category details expose a validator;
category deletion assigns referenced ingredients to the null/uncategorised fallback.

Image POST is multipart field `file` with a supported media type and optional retry UUID; return
201 {id,mediaType,size}. The uploaded filename is ignored. Images are private, immutable assets;
GET supplies a private validator and DELETE requires it. JPEG/PNG/WebP input is limited to 10 MiB
and 20 megapixels, decoded, oriented and re-encoded without original metadata. Recipe and snapshot
references block asset deletion with 409 `image_referenced`.


Planner transport refinement: `ScheduledMealRead` uses `type: cook|leftovers|other`, with nullable
`cookingEvent` and `leftovers` payloads and the Other `label`. Cooking payload includes `id`,
`sourceRecipeId`, source recipe revision, `recipeSnapshot`, planned/actual portions, confirmation
time and source revision. Leftovers payload includes allocation/source IDs, `plannedPortions`,
`consumedPortions`, `reservedRemainder`, derived commitment and source revision. Slot reads use
`items` with id/name/position/isDefault/archived/revision. Availability includes `basis: planned|actual`,
`availablePortions`, dependent allocations and `sourceRevision`; its ETag validates source commands.
Individual meal ETags validate meal and consumption commands. Allocation creation carries the
source revision in the request body; a stale source revision returns 412. Default slot display
names are Breakfast, Lunch and Dinner. These names are seeded for existing and new households.


Implemented planner concurrency and payload details:
- Source availability includes its planned and actual prepared/eaten quantities, source recipe ID,
  recipe revision, title/date and current allocations in one versioned response. Leftover calendar
  rows include the frozen source title/date even when cooking falls outside the requested week.
- GET `/meal-slots/{id}` returns its opaque slot ETag. Calendar ranges are inclusive and bounded to 366 days.
- Meal ETags include both meal and source revisions; source availability ETags fence source commands.
  Treat all ETags as opaque. Malformed/missing preconditions resolve the household-scoped target first.
- Updating a cooking meal's portions/position with the same recipe ID keeps its snapshot. Explicitly
  selecting a different recipe replaces its basis; refreshing the same recipe uses preview/apply.
- Refresh returns `{mode, preview?, meal?}`. Apply requires the preview's event and recipe revisions.
- Leftover edits accept explicit `reservedRemainder`. A changed partially consumed or released plan
  cannot infer its reservation from planned portions. Reassignment also requires `previousSourceRevision`.
- DELETE `/meals/{id}` has an optional `{sourceRevision?, confirmConsumedRemoval?}` body. Recorded
  consumption cannot be removed without explicit confirmation. DELETE source actuals optionally
  carries `dependentResolutions`; DELETE consumption requires explicit remaining reservation/source revision.
- Inline actuals resolutions affect that source's own dependents. Reassignment across sources uses
  `/cooking-events/{id}/resolve` with `expectedSources` for every affected source. Source changes
  and dependent edit/move/release/delete/reassign actions validate atomically and never cascade silently.
- A `plan` resolution can include the proposed date/slot and an explicitly selected different recipe
  plus its expected revision, so resolving a conflicted cooking edit does not partially save changes.
- Optional DELETE bodies are documented as optional typed objects, allowing omission rather than
  nullable unions that the pinned Swift generator drops. Nullable decimals/revisions remain nullable
  strings in both clients. Decimal output removes storage-only trailing zeroes without rounding;
  original ingredient quantity text remains unchanged.

Shopping reconciliation changes include the backend-computed `purchaseDecisionRequired` boolean. When true, accepting the new requirement requires an explicit purchased-state choice. Both clients display this flag rather than comparing or calculating ingredient amounts independently.

Import capture/extraction input bounds: website URL strings allow at most 2,048 UTF-16 code units; pasted recipe text allows at most 100 KiB of UTF-8. The native capture envelope and backend URL/text validation use these same bounds and retain the original content rather than normalizing it. Over-limit input is rejected with visible feedback before extraction.

### Import coordination request shapes

Handoff commit sends `CommitImport {draftId, expectedDraftRevision, review}` where `review`
is `{recipe: RecipeWrite, confirmedMeasurementStandard: AU|US|null, reviewConfirmed: boolean}`.
The first commit requires an explicit non-null measurement selection matching the recipe and
`reviewConfirmed=true`; the standard cannot be supplied accidentally by RecipeWrite's manual-entry
default. Missing/uncertain ingredient acknowledgement still follows ordinary recipe validation.
The backend computes the commit hash; clients do not supply an authoritative digest. The hash
covers the reviewed recipe and explicit confirmation fields, and excludes transient draft ID and
revision. Decimal serving values are normalized without rounding before hashing.

POST `/import-handoffs/{id}/commit` returns the bare immutable CommitResult: 201 first commit,
200 identical replay. Replay authorization and hash checks precede draft existence, draft revision
and expiry checks. Owner-only GET/PUT/DELETE `/imports/{id}` exposes a strong draft ETag; PUT and
DELETE require If-Match. Draft GET validates the owner and fixed expiry before considering 304.
Unknown source input and uncertainty data remain available in the private draft response.

The API reports 410 `draft_expired`, `handoff_expired` or `committed_recipe_deleted`; changed
handoff input and reviewed retry content return 409. An unknown/foreign handoff returns 404.
Draft IDs that cannot resolve to a live owner-authorized draft return the same `draft_expired`
error, without exposing another user's draft. Returned commit timestamps and expiry values use
PostgreSQL microsecond precision so first responses and persisted replays match exactly.

The maintenance worker runs at startup and every five minutes in bounded batches: up to 100
households, then up to 100 expired drafts, retry records and open handoffs per household. It uses
the household command lock with SKIP LOCKED, explicit household predicates and a rotating cursor.
Busy households are revisited on a subsequent pass. It removes expired temporary records and marks
expired Open handoffs; it never deletes committed receipts or expired-key tombstones. No broker is
required. Integration tests invoke the same maintenance service explicitly for deterministic timing.

Ordinary POST `/imports/{id}/commit` accepts ImportReview with the draft If-Match and optional
Idempotency-Key. A successful ordinary commit atomically removes its temporary draft and stores
the original result in the existing 24-hour retry store when a key is supplied. The same key/body
returns that result before requiring a live draft; a new key cannot recreate a removed draft. A bound draft
always delegates to the durable handoff receipt and creates no ordinary retry cache entry. If a
bound draft has already expired or been cleaned up, the client resolves/commits its known handoff
ID rather than retrying via an unbound fallback. Deleted-result errors include optional
`commitResult {recipeId, committedRevision, committedAt}` in the common problem response.


### URL and pasted-text extraction

POST `/imports/url` accepts `{url, handoffId?}`. POST `/imports/text` accepts `{text, handoffId?}`.
Both accept an optional Idempotency-Key and return ImportStartResult `{draft, commitResult}`:
exactly one is non-null. A new draft returns 201 and its Location/ETag; a resumed draft or durable
committed result returns 200. ETag is present when the response contains a draft. Source content
must match the handoff's original SHA-256 binding. A committed handoff is resolved before network
work, draft expiry or provider configuration checks. An uncommitted live handoff resumes its live
owner draft; expiry permits explicit re-extraction only while the handoff itself remains live.

Ordinary extraction retry keys use the existing 24-hour encrypted retry store. Replays validate
source-body identity, resolve the current live owner draft and avoid repeated extraction. Concurrent
initial requests may both do external processing before either draft is stored; the household
transaction still persists a single idempotent draft result. No provider calls run under database
locks. Authentication/membership are revalidated before persisting the external result.

Draft reads expose originalInput, typed extraction candidates, optional corrected reviewedRecipe,
source/measurement status, uncertaintyFields, revision and fixed expiry. Candidate ingredients expose
verbatim source text, separately parsed quantity/unit/name/preparation/group, unresolved flags and
optional exact catalogue-match suggestions. These are suggestions for explicit review. Unsupported
or missing units/quantities remain unresolved. Source measurement standards remain unknown unless
explicitly declared; country/cuisine do not determine measures. Normal recipe commit validation
still requires explicit AU/US confirmation and uncertainty acknowledgement.

Import failures use stable codes: 422 for unsafe URLs, missing recipes, unsupported/invalid content,
invalid provider schemas or input/output limits; 409 for disabled/misconfigured AI; 502 for website
or provider request failure; 504 for the 15-second website or 60-second AI deadline. Caller
cancellation propagates and writes no new draft. Upstream response bodies and credentials are not
included in problems or logs. Clients retain their original entry text on failure.

Optional provider base URLs include the API base path, such as `/v1`; the adapter appends
`/chat/completions`, uses the administrator's configured model/key and disables redirects, cookies
and ambient proxies. Explicit provider URLs may be local. Arbitrary recipe website URLs always use
the public-address validation/pinning policy. No image URL is fetched automatically.

### Shared collections and tags

`GET /collections` and `/tags` accept `q`, `cursor`, `limit` and return
`{items:[{id,name,revision}],nextCursor}`. Detail GET supports a private conditional ETag.
POST accepts `{name}` with an optional creation retry key; PUT accepts `{name}` under the
detail ETag; DELETE requires the detail ETag and removes associations while retaining recipes.
Both household roles can use these operations. Names share the existing Unicode/whitespace/case
normalization policy and are unique per household within each organiser type; conflicts return
409 `name_conflict`. Assignment IDs on recipe writes must resolve in the current household and
duplicate IDs create one association. Duplication preserves memberships. Changed organiser names
or memberships invalidate affected recipe validators and household recipe/content revisions.
Search ORs collection IDs, ORs tag IDs, ANDs those groups with text/archive/favourite conditions;
text searches titles, canonical ingredient names, aliases and assigned tag names. Literal SQL
wildcard characters are escaped. Cursors are applied after filters.
