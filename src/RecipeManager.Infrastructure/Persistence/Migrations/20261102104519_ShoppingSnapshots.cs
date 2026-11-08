using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ShoppingSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "shopping");

        migrationBuilder.CreateTable(
            name: "ShoppingLists",
            schema: "shopping",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                Basis = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                SelectionJson = table.Column<string>(type: "jsonb", nullable: true),
                BaselineJson = table.Column<string>(type: "jsonb", nullable: false),
                SourceRevisionsJson = table.Column<string>(type: "jsonb", nullable: false),
                HouseholdSourceRevision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShoppingLists", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_ShoppingLists_Basis", "\"Basis\" IN ('Planned','Actual') AND (\"Basis\" = 'Planned' OR \"SelectionJson\" IS NOT NULL)");
                table.CheckConstraint("CK_ShoppingLists_Snapshots", "jsonb_typeof(\"BaselineJson\") = 'object' AND jsonb_typeof(\"SourceRevisionsJson\") = 'array' AND (\"SelectionJson\" IS NULL OR jsonb_typeof(\"SelectionJson\") = 'object')");
                table.CheckConstraint("CK_ShoppingLists_Versions", "\"Revision\" >= 1 AND \"PolicyVersion\" >= 1 AND \"HouseholdSourceRevision\" >= 0");
                table.ForeignKey(
                    name: "FK_ShoppingLists_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ShoppingItems",
            schema: "shopping",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                ListId = table.Column<Guid>(type: "uuid", nullable: false),
                RequirementKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                IngredientId = table.Column<Guid>(type: "uuid", nullable: true),
                Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                CategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Manual = table.Column<bool>(type: "boolean", nullable: false),
                Purchased = table.Column<bool>(type: "boolean", nullable: false),
                Removed = table.Column<bool>(type: "boolean", nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                GeneratedJson = table.Column<string>(type: "jsonb", nullable: true),
                OverrideJson = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShoppingItems", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_ShoppingItems_Identity", "(\"Manual\" AND \"RequirementKey\" IS NULL AND \"IngredientId\" IS NULL AND \"GeneratedJson\" IS NULL AND \"OverrideJson\" IS NOT NULL)\nOR (NOT \"Manual\" AND \"RequirementKey\" IS NOT NULL AND \"IngredientId\" IS NOT NULL AND \"GeneratedJson\" IS NOT NULL)");
                table.CheckConstraint("CK_ShoppingItems_Position", "\"Position\" >= 0");
                table.CheckConstraint("CK_ShoppingItems_Values", "(\"GeneratedJson\" IS NULL OR jsonb_typeof(\"GeneratedJson\") = 'object') AND (\"OverrideJson\" IS NULL OR jsonb_typeof(\"OverrideJson\") = 'object')");
                table.ForeignKey(
                    name: "FK_ShoppingItems_Ingredients_HouseholdId_IngredientId",
                    columns: x => new { x.HouseholdId, x.IngredientId },
                    principalSchema: "ingredients",
                    principalTable: "Ingredients",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShoppingItems_ShoppingLists_HouseholdId_ListId",
                    columns: x => new { x.HouseholdId, x.ListId },
                    principalSchema: "shopping",
                    principalTable: "ShoppingLists",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShoppingItems_HouseholdId_IngredientId",
            schema: "shopping",
            table: "ShoppingItems",
            columns: new[] { "HouseholdId", "IngredientId" });

        migrationBuilder.CreateIndex(
            name: "IX_ShoppingItems_HouseholdId_ListId_Position",
            schema: "shopping",
            table: "ShoppingItems",
            columns: new[] { "HouseholdId", "ListId", "Position" });

        migrationBuilder.CreateIndex(
            name: "IX_ShoppingItems_HouseholdId_ListId_RequirementKey",
            schema: "shopping",
            table: "ShoppingItems",
            columns: new[] { "HouseholdId", "ListId", "RequirementKey" },
            unique: true,
            filter: "\"RequirementKey\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_ShoppingLists_HouseholdId_Name",
            schema: "shopping",
            table: "ShoppingLists",
            columns: new[] { "HouseholdId", "Name" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ShoppingItems",
            schema: "shopping");

        migrationBuilder.DropTable(
            name: "ShoppingLists",
            schema: "shopping");
    }
}
