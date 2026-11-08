# Structured recipe migration

`20261101170957_StructuredRecipes` adds recipes, ordered groups/ingredient rows/instructions,
personal favourites and private image metadata. It preserves existing households and catalogue
records. Entered decimal quantities/servings use numeric(18,6); fractions use nullable bigint
numerator/denominator columns. Database checks enforce mutually exclusive decimal/fraction forms,
unknown numeric nulls, nonnegative quantities, bounded magnitudes, units and positive servings.
Application validation rejects overprecision before PostgreSQL can round numeric input.

Composite tenant foreign keys prevent recipes from referencing another household's ingredients,
groups, images or memberships. Recipe deletion cascades only owned content/favourite links, never
the ingredient catalogue or image files. Referenced recipe deletion is checked transactionally by
the owning modules; archiving remains available. Group membership is constrained to the same
recipe. Whole-aggregate edits replace owned rows inside one transaction while preserving supplied
stable IDs; this allows position swaps under unique ordering indexes without transient duplicates.

Back up database, private images, key ring and its encryption material before production migration.
Do not use the destructive down migration to recover after recipe creation; restore the verified
backup. Existing cooking snapshots (added by the planner migration) are immutable until explicit
refresh, and are never rewritten by ordinary recipe edits or catalogue corrections.
