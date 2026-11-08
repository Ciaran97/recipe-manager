# Organisers and recipe search — in progress

The backend supports shared collection/tag CRUD and many-to-many assignment ownership through the
Recipes module. Both clients have management, multi-assignment and combined library filter screens.
Browser and signed native Story 6 acceptance now pass. The broader US8 final cross-client release gate remains tracked separately.

## Backend evidence

The initial organiser contract run failed with missing endpoints. After implementation, all 324
backend tests pass without skips: 129 unit, five architecture and 190 integration tests.
Five new PostgreSQL/API scenarios cover:

- Both roles managing organisers, normalized-name conflicts, current/stale validators and deletion
  removing links while retaining recipes.
- OR within collection/tag groups, AND across groups/text, case-insensitive ingredient aliases and
  tag names, filter reset and cursor pagination.
- Duplicate assignment IDs remaining unique, duplication retaining all memberships and concurrent
  same-name creation producing one success and one conflict.
- Archive include/exclude/only with personal favourite isolation and literal `%` search.
- Fail-closed tenant filters, household-specific uniqueness and composite foreign keys rejecting
  foreign organiser links.

Migration `20261103112039_RecipeOrganisers` is checked in. Organiser changes join the existing
household transaction/revision coordinator; no new infrastructure or background service is used.
OpenAPI and Angular/Swift generated types have been regenerated.

## Client evidence

Angular passes 59 tests, including retained input after name conflicts, current detail validators
for renames, later-page options and retained unavailable selections pending explicit user removal.
The initial real browser journey passes shared creation, duplicate-name feedback, multi-assignment,
alias/group filtering and organiser deletion preserving the recipe and its other memberships.
The strengthened browser journey passes in 4.1 seconds and excludes an unfiled recipe while retaining the correctly assigned alias match.

The native regression passes 88 tests (69 application plus 19 handoff), including retained filter
selection, paging reset on changed filters and cancellation preventing late result installation.
The signed native authentication + RecipeSearchUITests fixture passes without skips. It creates
and renames a collection, creates a tag, assigns both through the native editor, excludes an
unfiled recipe with combined filters, searches an ingredient alias, deletes the collection and
verifies the shared recipe and tag remain. The subsequent Angular observer passes in 904 ms.
This closes Story 6 acceptance (T123); full staged cross-client and accessibility release evidence
remains T092/T093/T129/T132.

Both management screens show remote-change notices without replacing an unsaved name or its
original validator. Reloading the list preserves that edit; a concurrent rename is rejected by
the backend rather than silently overwritten. Physical-device Local Network permission and
VoiceOver checks remain separate release gates.

Final catalogue regression: the browser case passes after updating its old conflict-message assertion
and retains the rejected alias text. The signed native catalogue case passes in 186.994 seconds.
A remote update had briefly disabled an open clean ingredient editor; background index refresh now
waits for editor dismissal, preserving editing state and original validators. Input readiness is
checked before typing. Both clients still reject conflicting aliases and protect referenced items.
