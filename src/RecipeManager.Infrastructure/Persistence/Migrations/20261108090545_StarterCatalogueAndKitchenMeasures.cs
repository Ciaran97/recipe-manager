using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class StarterCatalogueAndKitchenMeasures : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "Unit", schema: "recipes", table: "RecipeIngredients",
            type: "character varying(16)", maxLength: 16, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(4)", oldMaxLength: 4);
        migrationBuilder.DropCheckConstraint(
            name: "CK_RecipeIngredients_Unit",
            schema: "recipes",
            table: "RecipeIngredients");

        migrationBuilder.AddCheckConstraint(
            name: "CK_RecipeIngredients_Unit",
            schema: "recipes",
            table: "RecipeIngredients",
            sql: "\"Unit\" IN ('g', 'kg', 'ml', 'l', 'tsp', 'tbsp', 'cup', 'item', 'pinch', 'dash', 'handful', 'bunch', 'clove', 'slice')");

        // Add only missing names. Preserve household-owned identities, aliases and categorisation.
        foreach (var (name, index) in StarterCatalogueV1.Categories.Select((name, index) => (name, index)))
            migrationBuilder.Sql($"""
                INSERT INTO ingredients."Categories" ("Id", "HouseholdId", "Name", "NormalizedName", "SortOrder", "Revision")
                SELECT gen_random_uuid(), h."Id", '{Quote(name)}', '{Quote(name.ToUpperInvariant())}', {index}, 1
                FROM households."Households" h WHERE NOT EXISTS
                (SELECT 1 FROM ingredients."Categories" c WHERE c."HouseholdId" = h."Id" AND c."NormalizedName" = '{Quote(name.ToUpperInvariant())}');
                """);
        foreach (var group in StarterCatalogueV1.Ingredients)
            foreach (var name in group.Names)
                migrationBuilder.Sql($"""
                    WITH added AS (
                        INSERT INTO ingredients."Ingredients" ("Id", "HouseholdId", "CanonicalName", "NormalizedName", "CategoryId", "Revision")
                        SELECT gen_random_uuid(), h."Id", '{Quote(name)}', '{Quote(name.ToUpperInvariant())}', c."Id", 1
                        FROM households."Households" h JOIN ingredients."Categories" c ON c."HouseholdId" = h."Id" AND c."NormalizedName" = '{Quote(group.Category.ToUpperInvariant())}'
                        WHERE NOT EXISTS (SELECT 1 FROM ingredients."IngredientNames" n WHERE n."HouseholdId" = h."Id" AND n."NormalizedName" = '{Quote(name.ToUpperInvariant())}')
                        RETURNING "HouseholdId", "Id", "NormalizedName")
                    INSERT INTO ingredients."IngredientNames" ("HouseholdId", "IngredientId", "NormalizedName", "IsCanonical")
                    SELECT "HouseholdId", "Id", "NormalizedName", TRUE FROM added;
                    """);
        foreach (var (table, names) in new[] { ("Collections", StarterCatalogueV1.Collections), ("Tags", StarterCatalogueV1.Tags) })
            foreach (var name in names)
                migrationBuilder.Sql($"""
                    INSERT INTO recipes."{table}" ("Id", "HouseholdId", "Name", "NormalizedName", "Revision")
                    SELECT gen_random_uuid(), h."Id", '{Quote(name)}', '{Quote(name.ToUpperInvariant())}', 1
                    FROM households."Households" h WHERE NOT EXISTS
                    (SELECT 1 FROM recipes."{table}" o WHERE o."HouseholdId" = h."Id" AND o."NormalizedName" = '{Quote(name.ToUpperInvariant())}');
                    """);
        migrationBuilder.Sql("""
            UPDATE households."Households" SET "ContentRevision" = "ContentRevision" + 1,
            "RecipesRevision" = "RecipesRevision" + 1, "ShoppingSourcesRevision" = "ShoppingSourcesRevision" + 1;
            """);
    }

    private static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    // Starter rows are household-owned after insertion and must never be deleted on rollback.
    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "Unit", schema: "recipes", table: "RecipeIngredients",
            type: "character varying(4)", maxLength: 4, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(16)", oldMaxLength: 16);
        migrationBuilder.DropCheckConstraint(
            name: "CK_RecipeIngredients_Unit",
            schema: "recipes",
            table: "RecipeIngredients");

        migrationBuilder.AddCheckConstraint(
            name: "CK_RecipeIngredients_Unit",
            schema: "recipes",
            table: "RecipeIngredients",
            sql: "\"Unit\" IN ('g', 'kg', 'ml', 'l', 'tsp', 'tbsp', 'cup', 'item')");
    }
}
