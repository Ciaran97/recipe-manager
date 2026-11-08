# Real-time and Import Boundary Contracts

## SignalR: `/hubs/shopping`

Cookie-authenticated, same-origin connections; disallow cross-origin hub access. Validate Origin
on WebSocket upgrade as well as HTTP CORS policy. Check account active state, current security stamp, ticket expiry and active membership at
connection, on every invocation and before every recipient dispatch. Client cannot choose a household group by supplying an ID.

Client methods: `SubscribeList(listId)` verifies list access and registers connection;
`UnsubscribeList(listId)` removes it. No writes through hub; all mutations use REST/antiforgery.
Server event `ShoppingListChanged` contains {listId, revision, changeId}; event
`ShoppingListDeleted` contains {listId, revision}; neither contains recipe or item text.
Order/duplicate delivery is not guaranteed: ignore lower/equal revisions, GET accepted state for
new revisions, preserve dirty local form state and show conflict rather than replacing it.

Use withAutomaticReconnect for established connections plus bounded initial-start retry. On
reconnect re-authorize/re-subscribe and GET current state; on 401/403 clear protected cached state.
Refetch on focus and 30-second visible-list interval to recover a lost post-commit notification.
SignalR is an acceleration path, not the durable store or an audit stream.

A generic source-change hint can arrive after a reconciliation preview already includes that
change. It is not itself proof that the preview's source fence is stale. Keep the user's entered
choices and explicit review of the same preview. Clients may attempt apply with its unchanged
list/source revisions; the backend's transactional fence is authoritative. A genuine 409/412
requires a fresh preview and explicit review, preserving existing list data and local choices.
Notifications never authorize silently replacing a snapshot or changing a purchased/manual item.

Track connections by user/membership/stamp/expiry in process. SessionRevocationService handles
logout, password change/reset, account deactivation, membership removal and security-stamp rotation
through one per-user async dispatch/revocation gate. Queued sends are cancelled and already-started
dispatches drained/cancelled before committing the revoke; after that boundary no send starts for
the revoked connection. Abort/remove all matching connections before returning success. Check the
committed user/membership state on every send, not cached claims/group membership. Gate ordering
and bounded sends follow data-model.md; failed checks drop sends and close sockets. Already-sent
frames may arrive later and cannot be recalled. Expiry timer closes idle expired connections;
reconnection with expired/revoked cookies fails; valid fresh login may reconnect.
Scale-out/multiple API replicas and backplanes are not supported in initial Compose topology.

## Import provider contract

`IRecipeWebsiteImporter`: validated public URL → zero/one/multiple extracted recipe drafts plus
warnings/source metadata. schema.org JSON-LD Recipe extraction only; partial fields retained.
`IRecipeTextImporter`: pasted text + provider settings + cancellation → extracted recipe draft.
`IAiRecipeProvider`: configured endpoint/model, text and output schema → candidate JSON or typed
unavailable/timeout/invalid-schema error. Domain writes are not available to providers.

Draft schema: title?, description?, imageCandidateUrl?, defaultServings?, prep/cookMinutes?,
measurementStandard AU|US|unknown, ingredients[{quantityText?,unit?,name,preparation?,group?,
uncertainties[]}], ordered instructions[], notes?, sourceUrl?, warnings[]. No trusted catalogue
IDs from external input: matching suggestions are resolved household-locally and confirmed.
AI confidence does not authorize invented quantities. Missing source standard blocks draft commit
until user selects it. Mixed-standard lines must be normalized explicitly during review while
retaining original source text; never silently reinterpret an Australian tablespoon as US.

Use provider schema/structured-output capability when available, always independently validate
returned JSON. Treat all content as untrusted text; no tool execution, remote code, hidden prompts,
or cross-household retrieval. AI errors preserve pasted text/draft. Provider credentials are supplied
server-side, never passed to the browser. Provider selection is admin-configurable and optional;
initial OpenAI-compatible adapter and fake adapter, no automatic provider failover or recommendations.

URL fetch restrictions: HTTP(S), DNS/public-target validation per connection and redirect, no ambient
credentials/proxy, bounded timeout/body/redirects, reject private/loopback/link-local/metadata targets
including IPv6 forms and DNS rebinding. Do not fetch candidate images automatically: image candidates
must undergo the same restrictions and image validation on explicit user adoption. AI admin endpoint
configuration uses a separate trusted configuration boundary to support local model servers.
No external URL is rendered as trusted HTML. JSON-LD microdata fallback/browser automation is not
promised. Controlled supported/unsupported website fixtures define extractor acceptance coverage.

## Background work

No job broker: bounded cancellable import requests return a review draft, with explicit timeout
and retry. A small in-process periodic cleanup deletes expired drafts and idempotency
records; startup also runs bounded cleanup. Persistent writes use authenticated household context.
No external provider request is performed while holding allocation/list database locks. Cleanup
never removes durable handoff results or expired-key tombstones.

## Mandatory native client and household invalidation amendment

The existing shopping events remain compatible. Add authenticated household subscription
`SubscribeHousehold()` derived from current membership, never an arbitrary client household ID.
Server event `HouseholdChanged` carries {contentRevision, domainRevisions}; no content payload.
Emit after shared recipe/catalogue/organiser/planner/leftover/shopping/settings changes commit.
Apply the same connection revocation guarantees as shopping subscriptions. Angular uses this to
refresh affected views; preserved local drafts retain stale revision conflicts rather than being
silently replaced. Personal preferences are not household broadcasts.

iOS v1 uses the permitted equivalent REST synchronization: one structured foreground task polls
`/api/v1/household/sync` conditionally every second, fetches changed visible resources, and refreshes
immediately after foreground return/network recovery. No Swift SignalR dependency, raw socket
protocol or push provider is required. Polling stops in background and on logout, backs off network
failures and resumes without manual refresh. Authentication failure clears protected caches and
requires sign-in; network failure alone does not claim an edit was saved. No offline-first write
queue or background delivery is promised. The same two-second foreground cross-client criterion
applies regardless of transport. Both clients remain attached to the same household revisions.

Share Extension handoff contract: URL/plain-text NSItemProvider → validated local envelope in
App Group → explicit main-app continuation → normal URL/text import → review/edit → draft commit.
Multiple representations require user selection; unsupported media/oversize input reports an error.
Extension completion acknowledges local handoff only, not backend extraction or recipe creation.
If main app is not running, handoff waits for next explicit open/foreground; do not rely on an
unsupported extension API to force it open. Signed-out users log in and confirm destination before
sending. Raw content is never sent to an AI provider until the disclosed normal import action.

## Personal revisions and common performance procedure

Subscribe to personal updates only through server-derived authenticated user membership, not a
client-supplied UserId. `PersonalChanged` contains {ownPreferencesRevision}; send it only to that
user's currently authorized connections through the same revocation gate. It is never a household
broadcast. Profile/favourite changes bump only personal revision. Conditional sync uses the
user-bound (HouseholdId,UserId,ContentRevision,PreferencesRevision) ETag; a match on household
revision alone is insufficient. Counter updates are transactional; client dirty forms keep their
original revisions and require explicit conflict resolution.

SC-005 and [MP-SYNC-01](../spec.md) own the single latency procedure for all four Angular/iOS
pairings, connected updates, reconnect and foreground return. SC-013 adds correctness/convergence
rather than a second threshold. Foreground/connection recovery immediately GETs compound sync,
then affected resources. No background delivery or offline-first requirement is introduced.

Share handoff registration/status/durable commit follow rest-api.md. Extension still only captures
local input; the containing app registers HandoffId once and resolves its outcome before extraction
or retry. A seven-day handoff never depends on a 24-hour retry cache or live draft for an already
committed result. No provider work occurs on an already committed replay; no recipe is re-created
for expired/deleted committed outcomes. All new handoff operations retain owner/household checks.
