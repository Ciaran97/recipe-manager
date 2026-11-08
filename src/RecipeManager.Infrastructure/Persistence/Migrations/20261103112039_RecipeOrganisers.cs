using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RecipeOrganisers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Collections",
            schema: "recipes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Collections", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_Collections_Revision", "\"Revision\" >= 1");
                table.ForeignKey(
                    name: "FK_Collections_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Tags",
            schema: "recipes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_Tags_Revision", "\"Revision\" >= 1");
                table.ForeignKey(
                    name: "FK_Tags_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "RecipeCollections",
            schema: "recipes",
            columns: table => new
            {
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                CollectionId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecipeCollections", x => new { x.HouseholdId, x.RecipeId, x.CollectionId });
                table.ForeignKey(
                    name: "FK_RecipeCollections_Collections_HouseholdId_CollectionId",
                    columns: x => new { x.HouseholdId, x.CollectionId },
                    principalSchema: "recipes",
                    principalTable: "Collections",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_RecipeCollections_Recipes_HouseholdId_RecipeId",
                    columns: x => new { x.HouseholdId, x.RecipeId },
                    principalSchema: "recipes",
                    principalTable: "Recipes",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RecipeTags",
            schema: "recipes",
            columns: table => new
            {
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                TagId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecipeTags", x => new { x.HouseholdId, x.RecipeId, x.TagId });
                table.ForeignKey(
                    name: "FK_RecipeTags_Recipes_HouseholdId_RecipeId",
                    columns: x => new { x.HouseholdId, x.RecipeId },
                    principalSchema: "recipes",
                    principalTable: "Recipes",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_RecipeTags_Tags_HouseholdId_TagId",
                    columns: x => new { x.HouseholdId, x.TagId },
                    principalSchema: "recipes",
                    principalTable: "Tags",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Collections_HouseholdId_NormalizedName",
            schema: "recipes",
            table: "Collections",
            columns: new[] { "HouseholdId", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RecipeCollections_HouseholdId_CollectionId",
            schema: "recipes",
            table: "RecipeCollections",
            columns: new[] { "HouseholdId", "CollectionId" });

        migrationBuilder.CreateIndex(
            name: "IX_RecipeTags_HouseholdId_TagId",
            schema: "recipes",
            table: "RecipeTags",
            columns: new[] { "HouseholdId", "TagId" });

        migrationBuilder.CreateIndex(
            name: "IX_Tags_HouseholdId_NormalizedName",
            schema: "recipes",
            table: "Tags",
            columns: new[] { "HouseholdId", "NormalizedName" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RecipeCollections",
            schema: "recipes");

        migrationBuilder.DropTable(
            name: "RecipeTags",
            schema: "recipes");

        migrationBuilder.DropTable(
            name: "Collections",
            schema: "recipes");

        migrationBuilder.DropTable(
            name: "Tags",
            schema: "recipes");
    }
}
