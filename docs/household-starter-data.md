# Kitchen measures and household starter data

Recipes, imports, manual shopping items and serving previews support `pinch`, `dash`, `handful`,
`bunch`, `clove` and `slice`, alongside the existing mass, volume and whole-item units.
Each kitchen measure has its own calculation dimension: two pinches can consolidate with other
pinches of the same ingredient, but never with grams, teaspoons, handfuls or whole items.
Serving scaling retains the original unit and exact fraction. No mass or volume is inferred.
Update the iOS app with this server version before entering the new units or creating combined
shopping lists; earlier native clients do not know these enum values.

New households receive a starter catalogue of common produce, meat, dairy and pantry ingredients,
plus Breakfast, Lunch, Dinner, Snacks, Desserts and Sides collections and a small set of tags.
These are ordinary household-owned records: rename, categorise, delete and add your own as usual.
The upgrade migration adds missing defaults to existing households once, preserving matching
canonical names, aliases, categories and IDs. Deleted defaults are not re-created on every startup.
The migration does not remove starter records on rollback, since they may have been edited or used.
Restore a matched backup and server image when rolling back a deployed database.

The isolated API/browser fixtures disable new-household starter ingredients and organisers through
`StarterData:Enabled=false` so their existing scenarios can explicitly arrange names. Dedicated
integration tests exercise enabled setup and migration over existing household data. Categories
remain seeded in either fixture mode. Normal installations enable the starter data by default.

# Shopping from recipes and the planner

On web and iOS, open a recipe and choose **Add ingredients to shopping list**, or open the planner
and choose **Add this week's ingredients to shopping list**. Recipe serving previews carry their
chosen portions into the list. Planner sources use portions prepared, preserving cooking snapshots;
leftovers do not add cooking ingredients.

Choose a new list or an existing list using planned quantities. Existing-list additions preview the
combined requirements before applying them, retaining manual items, quantity overrides, removed
items and purchase choices through the usual reconciliation flow. Lists using actual preparation
remain available for their original actuals workflow; additions from these entry points explicitly
require a planned list or a new list. Separate weeks stay separate ranges, overlapping cooking
events count once, and intentionally adding the same recipe again adds its selected servings.
Combined lists can be reviewed again after recipe or planner changes.

# Proxy validators

API responses now add `Cache-Control: no-transform` after endpoint handling,
including reads with ETags, preserving their existing private/cache-control directives. This prevents compliant intermediaries from transforming responses and
weakening strong validators used for edits. Cloudflare documents that `no-transform` disables
compression even when a Compression Rule matches:
[Compression Rules](https://developers.cloudflare.com/rules/compression-rules/).
Keep the existing API/hub cache bypass and compression exclusions. Real stale writes still return
412; clients never strip `W/`, guess a new revision or silently retry a conflicting edit.
