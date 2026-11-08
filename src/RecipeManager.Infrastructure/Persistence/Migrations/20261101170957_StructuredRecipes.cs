using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class StructuredRecipes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "recipes");

        migrationBuilder.CreateTable(
            name: "ImageAssets",
            schema: "recipes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                StorageKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                MediaType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Size = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImageAssets", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_ImageAssets_Media", "\"MediaType\" IN ('image/jpeg', 'image/png', 'image/webp')");
                table.CheckConstraint("CK_ImageAssets_Size", "\"Size\" > 0");
                table.ForeignKey(
                    name: "FK_ImageAssets_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Recipes",
            schema: "recipes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                ImageAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                DefaultServings = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                PrepMinutes = table.Column<int>(type: "integer", nullable: true),
                CookMinutes = table.Column<int>(type: "integer", nullable: true),
                Notes = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                MeasurementStandard = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                Archived = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Recipes", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_Recipes_Revision", "\"Revision\" >= 1 AND \"PolicyVersion\" >= 1");
                table.CheckConstraint("CK_Recipes_Servings", "\"DefaultServings\" > 0 AND \"DefaultServings\" <= 1000000");
                table.CheckConstraint("CK_Recipes_Standard", "\"MeasurementStandard\" IN ('AU', 'US')");
                table.CheckConstraint("CK_Recipes_Times", "(\"PrepMinutes\" IS NULL OR \"PrepMinutes\" >= 0) AND (\"CookMinutes\" IS NULL OR \"CookMinutes\" >= 0)");
                table.ForeignKey(
                    name: "FK_Recipes_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Recipes_ImageAssets_HouseholdId_ImageAssetId",
                    columns: x => new { x.HouseholdId, x.ImageAssetId },
                    principalSchema: "recipes",
                    principalTable: "ImageAssets",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Favourites",
            schema: "recipes",
            columns: table => new
            {
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipeId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Favourites", x => new { x.HouseholdId, x.UserId, x.RecipeId });
                table.ForeignKey(
                    name: "FK_Favourites_Memberships_HouseholdId_UserId",
                    columns: x => new { x.HouseholdId, x.UserId },
                    principalSchema: "households",
                    principalTable: "Memberships",
                    principalColumns: new[] { "HouseholdId", "UserId" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Favourites_Recipes_HouseholdId_RecipeId",
                    columns: x => new { x.HouseholdId, x.RecipeId },
                    principalSchema: "recipes",
                    principalTable: "Recipes",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "IngredientGroups",
            schema: "recipes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IngredientGroups", x => new { x.HouseholdId, x.Id });
                table.UniqueConstraint("AK_IngredientGroups_HouseholdId_RecipeId_Id", x => new { x.HouseholdId, x.RecipeId, x.Id });
                table.CheckConstraint("CK_IngredientGroups_Position", "\"Position\" >= 0");
                table.ForeignKey(
                    name: "FK_IngredientGroups_Recipes_HouseholdId_RecipeId",
                    columns: x => new { x.HouseholdId, x.RecipeId },
                    principalSchema: "recipes",
                    principalTable: "Recipes",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Instructions",
            schema: "recipes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                Text = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Instructions", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_Instructions_Position", "\"Position\" >= 0");
                table.ForeignKey(
                    name: "FK_Instructions_Recipes_HouseholdId_RecipeId",
                    columns: x => new { x.HouseholdId, x.RecipeId },
                    principalSchema: "recipes",
                    principalTable: "Recipes",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RecipeIngredients",
            schema: "recipes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                Position = table.Column<int>(type: "integer", nullable: false),
                QuantityText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                DecimalValue = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                FractionNumerator = table.Column<long>(type: "bigint", nullable: true),
                FractionDenominator = table.Column<long>(type: "bigint", nullable: true),
                Unit = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                Preparation = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                Uncertain = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecipeIngredients", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_RecipeIngredients_Position", "\"Position\" >= 0");
                table.CheckConstraint("CK_RecipeIngredients_Quantity", "(\"Kind\" = 'Unknown' AND \"DecimalValue\" IS NULL AND \"FractionNumerator\" IS NULL AND \"FractionDenominator\" IS NULL)\nOR (\"Kind\" = 'Numeric' AND (\n  (\"DecimalValue\" IS NOT NULL AND \"DecimalValue\" >= 0 AND \"DecimalValue\" <= 1000000000\n    AND \"FractionNumerator\" IS NULL AND \"FractionDenominator\" IS NULL)\n  OR (\"DecimalValue\" IS NULL AND \"FractionNumerator\" IS NOT NULL AND \"FractionDenominator\" IS NOT NULL\n    AND \"FractionNumerator\" >= 0 AND \"FractionDenominator\" > 0 AND \"FractionDenominator\" <= 1000000000\n    AND \"FractionNumerator\" <= 1000000000::bigint * \"FractionDenominator\")))");
                table.CheckConstraint("CK_RecipeIngredients_Unit", "\"Unit\" IN ('g', 'kg', 'ml', 'l', 'tsp', 'tbsp', 'cup', 'item')");
                table.ForeignKey(
                    name: "FK_RecipeIngredients_IngredientGroups_HouseholdId_RecipeId_Gro~",
                    columns: x => new { x.HouseholdId, x.RecipeId, x.GroupId },
                    principalSchema: "recipes",
                    principalTable: "IngredientGroups",
                    principalColumns: new[] { "HouseholdId", "RecipeId", "Id" });
                table.ForeignKey(
                    name: "FK_RecipeIngredients_Ingredients_HouseholdId_IngredientId",
                    columns: x => new { x.HouseholdId, x.IngredientId },
                    principalSchema: "ingredients",
                    principalTable: "Ingredients",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_RecipeIngredients_Recipes_HouseholdId_RecipeId",
                    columns: x => new { x.HouseholdId, x.RecipeId },
                    principalSchema: "recipes",
                    principalTable: "Recipes",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Favourites_HouseholdId_RecipeId",
            schema: "recipes",
            table: "Favourites",
            columns: new[] { "HouseholdId", "RecipeId" });

        migrationBuilder.CreateIndex(
            name: "IX_ImageAssets_HouseholdId_StorageKey",
            schema: "recipes",
            table: "ImageAssets",
            columns: new[] { "HouseholdId", "StorageKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IngredientGroups_HouseholdId_RecipeId_Position",
            schema: "recipes",
            table: "IngredientGroups",
            columns: new[] { "HouseholdId", "RecipeId", "Position" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Instructions_HouseholdId_RecipeId_Position",
            schema: "recipes",
            table: "Instructions",
            columns: new[] { "HouseholdId", "RecipeId", "Position" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RecipeIngredients_HouseholdId_IngredientId",
            schema: "recipes",
            table: "RecipeIngredients",
            columns: new[] { "HouseholdId", "IngredientId" });

        migrationBuilder.CreateIndex(
            name: "IX_RecipeIngredients_HouseholdId_RecipeId_GroupId",
            schema: "recipes",
            table: "RecipeIngredients",
            columns: new[] { "HouseholdId", "RecipeId", "GroupId" });

        migrationBuilder.CreateIndex(
            name: "IX_RecipeIngredients_HouseholdId_RecipeId_Position",
            schema: "recipes",
            table: "RecipeIngredients",
            columns: new[] { "HouseholdId", "RecipeId", "Position" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Recipes_HouseholdId_ImageAssetId",
            schema: "recipes",
            table: "Recipes",
            columns: new[] { "HouseholdId", "ImageAssetId" });

        migrationBuilder.CreateIndex(
            name: "IX_Recipes_HouseholdId_Title",
            schema: "recipes",
            table: "Recipes",
            columns: new[] { "HouseholdId", "Title" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Favourites",
            schema: "recipes");

        migrationBuilder.DropTable(
            name: "Instructions",
            schema: "recipes");

        migrationBuilder.DropTable(
            name: "RecipeIngredients",
            schema: "recipes");

        migrationBuilder.DropTable(
            name: "IngredientGroups",
            schema: "recipes");

        migrationBuilder.DropTable(
            name: "Recipes",
            schema: "recipes");

        migrationBuilder.DropTable(
            name: "ImageAssets",
            schema: "recipes");
    }
}
