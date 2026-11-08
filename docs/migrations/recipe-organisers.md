# Shared recipe organisers migration

`20261103112039_RecipeOrganisers` adds household-scoped Collections, Tags, RecipeCollections and
RecipeTags. Existing recipes retain their content and begin with no organiser links. No household,
recipe, snapshot, ingredient, plan, shopping or authentication rows are rewritten by this migration.

The source-controlled migration creates composite household keys, foreign keys, normalized-name
uniqueness within each type and unique assignment keys. The upgrade test starts at the previous
migration with an existing recipe, applies the new migration, verifies that recipe and creates a
collection. The pending-model check passes.

Stop application writes, take a database/files/key backup, run the explicit one-shot migrator, then
start the application on the upgraded schema. Repeating the migrator is safe. Do not run schema
migration implicitly in multiple web workers. The down migration removes organiser/link tables and
loses organiser assignments; prefer restoring the pre-upgrade backup when rolling back application
versions. Validate a restore in a separate database/volumes before changing a live installation.
