using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class IngredientCatalogue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "ingredients");

        migrationBuilder.CreateTable(
            name: "Categories",
            schema: "ingredients",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Categories", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_Categories_Revision", "\"Revision\" >= 1");
                table.CheckConstraint("CK_Categories_SortOrder", "\"SortOrder\" >= 0");
                table.ForeignKey(
                    name: "FK_Categories_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Ingredients",
            schema: "ingredients",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                CanonicalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Ingredients", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_Ingredients_Revision", "\"Revision\" >= 1");
                table.ForeignKey(
                    name: "FK_Ingredients_Categories_HouseholdId_CategoryId",
                    columns: x => new { x.HouseholdId, x.CategoryId },
                    principalSchema: "ingredients",
                    principalTable: "Categories",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Ingredients_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IngredientAliases",
            schema: "ingredients",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IngredientAliases", x => new { x.HouseholdId, x.Id });
                table.ForeignKey(
                    name: "FK_IngredientAliases_Ingredients_HouseholdId_IngredientId",
                    columns: x => new { x.HouseholdId, x.IngredientId },
                    principalSchema: "ingredients",
                    principalTable: "Ingredients",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "IngredientNames",
            schema: "ingredients",
            columns: table => new
            {
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                IsCanonical = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IngredientNames", x => new { x.HouseholdId, x.NormalizedName });
                table.ForeignKey(
                    name: "FK_IngredientNames_Ingredients_HouseholdId_IngredientId",
                    columns: x => new { x.HouseholdId, x.IngredientId },
                    principalSchema: "ingredients",
                    principalTable: "Ingredients",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Categories_HouseholdId_NormalizedName",
            schema: "ingredients",
            table: "Categories",
            columns: new[] { "HouseholdId", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IngredientAliases_HouseholdId_IngredientId",
            schema: "ingredients",
            table: "IngredientAliases",
            columns: new[] { "HouseholdId", "IngredientId" });

        migrationBuilder.CreateIndex(
            name: "IX_IngredientAliases_HouseholdId_NormalizedName",
            schema: "ingredients",
            table: "IngredientAliases",
            columns: new[] { "HouseholdId", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IngredientNames_HouseholdId_IngredientId_IsCanonical",
            schema: "ingredients",
            table: "IngredientNames",
            columns: new[] { "HouseholdId", "IngredientId", "IsCanonical" },
            unique: true,
            filter: "\"IsCanonical\"");

        migrationBuilder.CreateIndex(
            name: "IX_Ingredients_HouseholdId_CategoryId",
            schema: "ingredients",
            table: "Ingredients",
            columns: new[] { "HouseholdId", "CategoryId" });

        migrationBuilder.CreateIndex(
            name: "IX_Ingredients_HouseholdId_NormalizedName",
            schema: "ingredients",
            table: "Ingredients",
            columns: new[] { "HouseholdId", "NormalizedName" },
            unique: true);
        // Seed defaults for installations created before the catalogue module was added.
        migrationBuilder.Sql("""
            INSERT INTO ingredients."Categories" ("Id", "HouseholdId", "Name", "NormalizedName", "SortOrder", "Revision")
            SELECT gen_random_uuid(), h."Id", c.name, upper(c.name), c.position, 1
            FROM households."Households" h
            CROSS JOIN (VALUES ('Produce', 0), ('Meat', 1), ('Dairy', 2), ('Pantry', 3)) AS c(name, position);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "IngredientAliases",
            schema: "ingredients");

        migrationBuilder.DropTable(
            name: "IngredientNames",
            schema: "ingredients");

        migrationBuilder.DropTable(
            name: "Ingredients",
            schema: "ingredients");

        migrationBuilder.DropTable(
            name: "Categories",
            schema: "ingredients");
    }
}
