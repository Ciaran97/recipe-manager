# Import implementation — in progress

The complete backend import slice is implemented: bounded website and optional AI extraction,
private typed drafts, catalogue suggestions, review/commit validation, durable handoff coordination
and cleanup. Angular and SwiftUI import review are implemented and have passed real client import
journeys. Full Share Extension capture-to-commit and delayed client retry acceptance are tracked separately.

## Verified storage foundation

- Nine new domain cases were written before their corresponding models, then passed.
- PostgreSQL persistence tests apply the checked-in migration, round-trip committed receipts
  after draft deletion, reject a draft bound to another user's handoff and verify fail-closed
  household query filters.
- Input hashing uses exact original UTF-8 bytes with a lowercase kind and NUL separator;
  canonically equivalent Unicode, whitespace changes and different source kinds remain distinct.
  Invalid Unicode and text above 100 KiB are rejected.
- Receipt expiry is fixed at first capture/registration. Draft expiry remains fixed at 24 hours
  after review. Unknown source measures require explicit confirmation, and known measures do not
  bypass review. Invalid/oversized JSON cannot partially mutate an existing draft.
- Import schema/migration and recovery considerations are in `docs/migrations/import-handoffs.md`.

## Coordination and commit acceptance

Eight PostgreSQL service scenarios cover:

- Lost success response, actual cleanup, day-six retry and committed replay after seven days.
- Concurrent registration and four concurrent commits: exactly one new recipe and one receipt.
- Missing measurement confirmation, unreviewed input and foreign ingredient failure, with complete
  rollback before a later valid commit succeeds.
- Expired draft re-extraction while the handoff remains valid, and safe failure after fixed expiry.
- Deleted recipe results retaining their original identity; another member cannot read or commit
  the receipt. Changed retry bodies cannot replace the committed result.
- Cleanup preserving committed receipts, converting expired Open receipts to retained tombstones
  and leaving another household's live draft intact.
- Ordinary commits returning the original retry result after draft removal, rejecting changed
  retry bodies and preventing a second creation under a different retry key.
- Ordinary-route commits for bound drafts always delegating to the durable receipt, without an
  ordinary 24-hour retry record. Revoked security stamps and expired credentials fail before replay.

Two HTTP scenarios additionally cover authenticated registration/status/commit; explicit source
standard/review validation; private draft read and conditional GET; required and stale ETags;
first-commit 201 versus identical replay 200; owner/anonymous denial; deleted-result 410 with its
original commit identity; and ordinary-route revision/retry behavior.

A precision regression first demonstrated that PostgreSQL's microsecond timestamp storage could
make a persisted replay differ from its first response. Returned timestamps now use storage
precision and the deliberately submicrosecond test passes.

Recipe creation reuses the Recipes module's normal validation/reference/creation contract inside
one household transaction. No provider calls or external fetching occur under database locks.
Shared recipe revision notifications are published only after successful transaction commit.
The maintenance worker uses bounded batches at startup and five-minute intervals, explicit
household predicates and the household command lock. Tests call the same maintenance service
explicitly rather than rely on background timing.

## Extraction acceptance

Thirty-nine focused parser/address cases cover supported ingredient forms, visible unknowns,
public/non-public IPv4/IPv6, JSON-LD graphs/multiple recipes, source-standard uncertainty, instruction
sections, missing servings, row limits and structured unit codes. Unsupported/missing structured
units cannot silently become whole-item counts.

Ten unsafe URL cases and three real HTTP wire cases cover private/metadata/IPv6/numeric URLs,
credential-bearing targets, mixed DNS answers, rebinding, pinned addresses, disabled proxies/cookies,
redirect limits, declared/streamed size bounds, compressed/chunked expansion, Host preservation and
no image requests. Seven fake/compatible AI cases cover strict schemas, exact source evidence,
configured model/key/endpoint, absent tools, disabled AI, 100 KiB input and 1 MiB response bounds,
sanitary provider failures and rejected fabricated ingredient quantities. No paid API calls are made.

Three authenticated extraction journeys demonstrate AI-off URL imports, protected provider settings,
multiple candidates and unknowns, canonical/alias suggestions without merging distinct ingredients,
source retry identity and four concurrent starts persisting one draft. A complete backend
URL → typed draft → corrected review → commit → cleanup → day-six retry journey retains original
candidates and resolves the original recipe without refetching. The isolated lock probe verifies
external fetches do not hold the request's household transaction; concurrent transactions are
measured separately rather than mistaken for an owned lock.

## Regression results

The final backend run passes all 319 tests: 129 unit, five architecture and 185 integration tests,
with no skips/exclusions. Both final generated clients compile. Angular build, lint and formatting
checks pass. Angular passes 55 tests. The signed native unit run passes 67 application tests and
19 ShareHandoff tests, with no skips.

## Release gates still open

T116 delayed client/share retry acceptance now passes. T101 retains physical Safari/ChatGPT/VoiceOver checks. Pending-inbox routing and signed capture-to-commit now pass. Collections/tags management remains the US6 slice;
import commits already use normal recipe organiser validation through its module contract.

## Client review evidence

The browser import journey passes against the production HTTP adapter and a nonshipping, local
compatible-provider fixture. It verifies AI-off availability, private URL rejection, retained input
after provider failure, explicit source-standard and unknown-unit correction, catalogue selection,
and reviewed commit. Dropping the successful commit response then checking the previous save
returns the original recipe, with exactly one persisted recipe. Manual recipe lifecycle regression
also passes after sharing the editor.

The signed native import UI journey passes against the same adapter/fixture: provider failure
retains input; extraction creates no recipe; explicit Australian measures, unknown salt units and
review acknowledgement precede saving; the stored rice fraction and unknown salt text survive.
The first combined run's final Angular observer did not launch because Chromium's custom cache
path was omitted; native success alone does not count as a passing combined fixture.

Both clients disclose provider processing before explicit extraction. Missing source measures and
units stay unset during import rather than inheriting manual-entry defaults. Native state tests
cover stable extraction retry identity and receipt-first recovery before source/draft expiry or AI
configuration checks; the latter clears the account-bound local capture without an unbound commit.

Final combined signed fixture: authentication, native text review and real Share Extension
capture → explicit destination confirmation → reviewed commit → capture removal all pass without
skips. The owning member retrieves the committed receipt; the administrator receives 404 for
that private handoff. The subsequent Angular shared-household observer passes in 884 ms. The
updated native regression passes 86 tests (67 application plus 19 handoff), including AI-off input
retention with no extraction request. This does not replace physical-device acceptance or the
separate day-six/lost-response client journey.

## Final client retry acceptance

The browser bound-handoff journey passes after the real 201 commit response is dropped. The owner
removes its temporary draft (410 afterward), disables AI and advances the browser clock to day six.
Checking the previous save reads the durable receipt, returns the original recipe and performs no
unbound commit, second handoff commit or draft read. Exactly one recipe remains. Server time is not
changed by the browser clock; server-side day-six/cleanup/post-expiry behavior is covered by the
PostgreSQL clock-controlled tests above.

The signed native ImportRetryUITests fixture also passes without skips. A guarded development-only
proxy hook drops the real successful handoff response; the fixture requires proof that it fired.
iOS displays Check previous save, then the app is terminated. The owner deletes the draft and an
administrator disables AI. On relaunch, explicit destination confirmation resolves the original
committed receipt before extraction/draft handling, clears the local capture and retains one recipe.
The final Angular household observer passes in 1.0 seconds. Full native/handoff regression now
passes 91 tests (72 application plus 19 handoff), including startup restoration races.

## Website import quality regression — 2026-11-04

A physical Safari import exposed alternate measures entering canonical ingredient names, broken
parenthetical preparation, a range incorrectly treated as a definite whole-item quantity, missing
ingredient groups and omitted recipe notes referenced by the cooking steps.

The shared backend parser now retains the explicit primary quantity/unit, moves a supplied
alternative measure into preparation text without inferring a conversion, separates balanced
parenthetical notes and top-level commas, and preserves numeric ranges as unresolved quantities.
Original input remains available for review. Optional visible WP Recipe Maker card enrichment
requires a unique matching recipe title and matching ordered ingredient identities/counts before
attaching groups or semantic preparation. Unrelated/misaligned cards cannot bind quantities to
other ingredients. Recipe notes preserve paragraph/list boundaries; original ordered instruction
text is retained. JSON-LD remains the general extraction path.

Eleven synthetic import quality regressions cover alternate measures, nested notes, ranges,
unquantified items, grouping/notes, unchanged instruction order and unrelated-card safety. A new
PostgreSQL/API test verifies a reviewed range remains visible and unresolved through recipe
persistence, serving preview and shopping generation. All 348 backend tests pass (140 unit, five
architecture, 203 integration), with no skips; targeted formatting and the production image build
also pass. No dependency, API shape or database migration changed.

The exact publicly accessible source was also checked privately: 14 ingredients, three groups,
ten original steps and the referenced recipe notes are extracted. The updated isolated LAN image
is healthy, and an authenticated HTTPS draft check verifies those fields and unresolved range.
The previously saved recipe revision is unchanged; the owner has been asked whether to apply
corrections to it or review a fresh import. Diagnostic drafts were removed without committing a
recipe. Captured/committed handoff retry identity is unchanged; an existing committed handoff still
returns its original recipe, while a fresh capture uses the revised extractor.

The owner's Taste page returns upstream HTTP 403 to ordinary requests from both Mac and deployed
network. Capture succeeds, but extraction remains refused by that publisher. No browser cookies,
challenge bypass or relaxed URL/TLS security were introduced. A physical Safari capture from an
accessible RecipeTin Eats page reaches editable review; ChatGPT Share supplies a conversation URL,
so its plain-text handoff still needs a host-provided text share/copy-paste check.

The owner approved updating the existing saved recipe. The application API accepted the original
opaque ETag and advanced its revision once. Read-back verifies clean names/preparation, three
groups, source notes and an unresolved range; the title, metadata, servings, measurement standard,
image, tags, collections and all ten instruction identities/order/text are unchanged. No duplicate
recipe was committed. Existing malformed catalogue entries were not destructively merged/deleted.

## Unspecified quantity needs no unit selection

The owner's clarification is recorded in FR-009 and the wire/storage conventions. Both editors
allow an unselected unit for unknown quantities, retain an explicit source unit and require a unit
again when the quantity becomes numeric. A missing unknown-unit selection serializes the existing
neutral `item` marker; no quantity/conversion is inferred and unknowns stay outside arithmetic.
Angular has 61 passing tests plus lint/format/production build. Native unit/handoff has 95 passing
tests (76 application, 19 handoff). The signed native import journey saves without touching the
salt unit picker (106.102 s); the browser import/save/lost-response retry journey passes (2.7 s),
retaining the unspecified qualifier and creating one recipe. The LAN web image is healthy; the
new signed iOS app/extension build succeeds but is not installed while the owner's phone is locked.
