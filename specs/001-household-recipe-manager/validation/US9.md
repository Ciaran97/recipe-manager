# Share Extension capture and import handoff — implementation in progress

T094–T098 pass capture, storage, provider and signed fixture checks. The extension, signing configuration
and share-sheet fixture are implemented. Main-app inbox/destination handling and server-backed import routing now pass signed review/commit and lost-response restart acceptance; physical-device checks remain open.

| Gate | Evidence |
| --- | --- |
| Envelope/provider/storage | 19 tests pass: verbatim URL/text, bounds, credential-free schema, genuine NSItemProvider URL/text loading, explicit mixed representation choice, unsupported media, cancellation, concurrent twenty-item capacity, duplicate identity, seven-day expiry, destination/draft binding, interrupted writes, corrupt/future schema preservation and explicit discard |
| Exact input identity | A regression first demonstrated Swift canonical Unicode equality could accept different UTF-8 bytes for one handoff; comparison now preserves exact original bytes, matching the import content-binding contract |
| Native regression | Current baseline: 75 application and 19 handoff tests pass; physical app/extension signing and shared App Group entitlements are verified |
| Signed fixture | Authentication prerequisite, URL/text/mixed share-sheet capture, cancellation and signed-out/terminated-main-app persistence pass with no skips |
| Full review/commit | T099/T100/T116 pass real signed capture-to-review/save and lost-response restart recovery; physical T101 checks remain |

The app, Share Extension and nonshipping fixture host use the same configurable App Group. No
Keychain group or session/provider credentials are shared. Protected staged writes and coordinated
atomic rename retain the prior queue on interruption. The source input is bounded to the agreed
technical URL/text limits and no extraction or external request occurs in the extension. Signing
identifiers and team overrides are documented in `ios/Signing.xcconfig.example`; physical provisioning now passes; Safari/ChatGPT compatibility remains final T101 acceptance work.

The first signed fixture run saved a capture but failed to read it in the fixture host: an
arbitrary `INFOPLIST_KEY_RecipeManagerAppGroup` build setting was not emitted into its generated
plist. Application/extension built plists already contained the correct value. The nonshipping
host now has an explicit plist, and acceptance checks shared-storage availability before sharing.
The final signed rerun passes all capture scenarios without skips.

Cross-process representation regression: the signed fixture exposed a host-advertised URL representation that could not load while a text representation was present. The reader now validates advertised representations independently and supports typed `loadItem` String/UTF-8 data fallback. Synthetic provider coverage and the account-bound inbox model pass in the combined 81-test native/handoff run; the final signed fixture also verifies actual text capture.

Final signed fixture passes. The extension saves real URL and plain-text payloads from the fixture host, mixed representations require explicit selection, Cancel leaves the queue unchanged, and the shared queue is readable while the main app is signed out and terminated. The fixture uses real matching App Group entitlements and explicit plist values. At that capture-only checkpoint, full main-app import acceptance was still pending; the later results below close routing/save/retry, while physical compatibility remains open. The final native-to-Angular profile observer also passes in 1.0 seconds.

## Main-app routing and signed import acceptance

T099/T100 are implemented in PendingImportInbox, PendingCaptureInboxModel and the shared
ImportReviewViewModel. Normal and shared URL/text input use the same editor and backend draft
workflow. Destination confirmation binds the capture to the explicit origin, account and household.
A changed session invalidates confirmation; a foreign binding hides source content. Interrupted
extraction retains the exact source and stable identity, and committed status is resolved before
checking source/draft expiry or AI configuration. Successful save removes the local envelope.

The combined signed AuthenticationUITests + ImportReviewUITests + ShareImportUITests fixture
passes without skips. It uses the real system share sheet and matching App Groups, then confirms
the destination, imports against the production adapter/local provider fixture, corrects source
measures and unknown units, saves exactly one new recipe and verifies an empty inbox. An owning
member can read the committed handoff result while the administrator gets 404. The final Angular
observer passes (884 ms). Native/handoff regression passes 86 tests.

Physical Safari/ChatGPT and VoiceOver checks remain T101 release gates; development provisioning and signed installation now pass.
T116 now passes complete client retry acceptance: the signed fixture drops the real committed response, restarts with its draft deleted and AI disabled, then recovers the same receipt and clears its capture. Browser day-six presentation and backend clock-controlled day-six/post-expiry tests cover the lifetime boundaries.

## Owner Safari check — 2026-11-04

The owner confirmed that Safari → Recipe Manager capture succeeds on the designated physical
phone and that starting import reports an error. This accepts the observed capture handoff only,
not website extraction/review/save. The isolated backend records `import_fetch_failed`; the exact
message and source URL have been requested to distinguish network/HTTP failures from unsupported
page data. Retain the capture for retry. The ChatGPT provider handoff and physical VoiceOver
checks remain pending, and T101 stays open.

The owner also confirmed that ChatGPT's Share action supplies only a conversation URL. Record
this as URL-provider behaviour, not successful recipe-text capture or extraction. Recipe text
must reach the text-import workflow through copy/paste or a host-provided selected-text share.
Conversation-page extraction has not been added or counted as approved recipe-site support.

The owner subsequently confirmed that Safari capture from the accessible RecipeTin Eats URL
opens editable review with ingredients/instructions and saved the recipe. Its reported extraction
quality defects were corrected in the backend and, with explicit owner approval, in the existing
saved recipe. Read-back confirms the corrected structure and no duplicate commit. Physical Safari
capture/review/save now has owner evidence. Taste's refused source and ChatGPT's URL-only Share
representation are recorded separately; physical plain-text and VoiceOver checks still keep T101
open. See validation/US5.md for the importer regression evidence.
