# Ingredient catalogue migration

`20261101165320_IngredientCatalogue` adds the ingredients schema and its four tables without
rewriting household or Identity data. Existing households receive Produce, Meat, Dairy and Pantry;
new households receive the same defaults inside initial setup. An unset category is the
uncategorised fallback. No ingredient names are inferred or merged during migration.

Back up the database and protected key material together before production migration. Apply with
the application stopped or the deployment migrator. The down migration removes catalogue data;
after users create ingredients, restore a verified backup rather than using down as a recovery
strategy. Parent deletion cascades only owned aliases/name-index rows. Category and household
foreign keys restrict cross-household links; referenced-ingredient deletion is checked by the
owning modules before a command is committed. Recipe and event snapshots are not rewritten by
catalogue corrections.
