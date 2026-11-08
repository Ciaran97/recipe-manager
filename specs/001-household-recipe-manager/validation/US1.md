# Structured recipes implementation verification

T036–T054 are complete. The recipe/catalogue foundation is verified on both clients; US6 still
owns collection/tag management and combined organiser search. Planner, shopping and import work
is not implied by these results.

| Gate | Verified result |
| --- | --- |
| Backend | 143 passing tests: 49 unit, five architecture, 89 real PostgreSQL integration; no skips |
| Angular | 27 passing component/state tests; production build, lint and formatting pass |
| Native | 28 passing unit/state tests; app plus embedded extension compile with strict Swift 6 |
| Browser recipe journey | Pass, 2.9 seconds / 11.7-second runner |
| Browser catalogue journey | Pass, 2.8 seconds |
| Native recipe journey | Full create/edit/scale/favourite/duplicate/archive/delete passes; fresh household setup and invitations also pass, no skips |
| Cross-client check after native recipe run | Angular reads the native household profile update; 875 ms / 1.5-second runner |
| Native catalogue journey | Pass, 157.938 seconds; setup/invitations repeated in that run |
| Contracts and migrations | OpenAPI plus generated TypeScript/Swift models updated; persistence, tenant FKs and additive category backfill verified |

The exact calculator retains decimal/fraction originals, AU/US standards and unknown quantities.
It scales and consolidates without intermediate rounding, using g/ml/item dimensions and no
mass-volume guesses. Decimal inputs and portions enforce six decimal places and approved bounds;
computed exact components are bounded to 4096 digits with 1000-line batches. There is no EF
BigInteger mapping. Display rounding never turns a positive small amount into zero.

Recipe tests cover lifecycle, stable row IDs/order during full edits, foreign/stale rows, exact
serving previews without source mutation, unknown review, personal favourite cache isolation,
lost create/duplicate responses and replay after source deletion. Catalogue tests cover normalized
canonical/alias uniqueness, correction and old-name retention, alias search, category fallback,
referenced deletion and tenant isolation. A deleted-parent alias retry still replays its result.
Frozen event/list catalogue protections are explicitly extended by T069/T085 after those owners land.

Private image tests cover 10 MiB/20 MP bounds, decoded JPEG/PNG/WebP MIME, re-encoding and metadata
removal, EXIF orientation, private caching, retry replay, member/outsider access, foreign assignment
and referenced deletion. SkiaSharp and native assets are pinned at 4.154.0. Private file/metadata
commit ordering and conservative orphan repair are documented in docs/development.md. Linux codec
execution remains part of the deployment gate.

Both editors provide catalogue autocomplete, explicit ingredient creation, string quantities,
unknown acknowledgement, groups, ordered instructions, duplication/reordering and private photo
selection. Both libraries use backend serving previews; native and web expose exact detail and
label rounded displays. There are no independent client ingredient calculations.

Regression tests first reproduced and then verified fixes for late Angular writes crossing session
generations, stale serving previews after recipe navigation, old native drafts sending requests
after server/account changes, and unchanged native name commits clearing canonical selections on
focus loss. Native autocomplete buttons have independent tap targets; picker/multiline tests use
stable accessibility identifiers. No UI failure was waived. The final native recipe fixture and
its Angular follow-up both passed and cleaned their owned simulator/processes/data.

Browser feature specs run in separate disposable installations, independent of filename order;
CI runs them all. Native fixtures use a dedicated simulator and its own trusted TLS certificate,
ad hoc signing for real Keychain behaviour, and real API requests. No application bypass endpoints
were added. Hosted CI and public distribution have not been claimed or performed.
