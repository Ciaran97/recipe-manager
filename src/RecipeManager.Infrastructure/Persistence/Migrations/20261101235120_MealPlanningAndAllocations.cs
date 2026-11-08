using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class MealPlanningAndAllocations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "leftovers");

        migrationBuilder.EnsureSchema(
            name: "planning");

        migrationBuilder.CreateTable(
            name: "MealSlots",
            schema: "planning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                Archived = table.Column<bool>(type: "boolean", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MealSlots", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_MealSlots_Position", "\"Position\" >= 0");
                table.ForeignKey(
                    name: "FK_MealSlots_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ScheduledMeals",
            schema: "planning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                SlotId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScheduledMeals", x => new { x.HouseholdId, x.Id });
                table.UniqueConstraint("AK_ScheduledMeals_HouseholdId_Id_Type", x => new { x.HouseholdId, x.Id, x.Type });
                table.CheckConstraint("CK_ScheduledMeals_Type", "(\"Type\" = 'Other' AND \"Label\" IS NOT NULL AND length(trim(\"Label\")) > 0)\nOR (\"Type\" IN ('Cook', 'Leftovers') AND \"Label\" IS NULL)");
                table.ForeignKey(
                    name: "FK_ScheduledMeals_MealSlots_HouseholdId_SlotId",
                    columns: x => new { x.HouseholdId, x.SlotId },
                    principalSchema: "planning",
                    principalTable: "MealSlots",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CookingEvents",
            schema: "planning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduledMealId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipeRevision = table.Column<long>(type: "bigint", nullable: false),
                RecipeSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                PlannedPrepared = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                PlannedEaten = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                ActualPrepared = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                ActualEaten = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                MealType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Cook")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CookingEvents", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_CookingEvents_Actual", "(\"ActualPrepared\" IS NULL AND \"ActualEaten\" IS NULL AND \"ConfirmedAt\" IS NULL)\nOR (\"ActualPrepared\" IS NOT NULL AND \"ActualEaten\" IS NOT NULL AND \"ConfirmedAt\" IS NOT NULL\n    AND \"ActualPrepared\" > 0 AND \"ActualPrepared\" <= 1000000 AND \"ActualEaten\" >= 0 AND \"ActualEaten\" <= \"ActualPrepared\")");
                table.CheckConstraint("CK_CookingEvents_Planned", "\"PlannedPrepared\" > 0 AND \"PlannedPrepared\" <= 1000000 AND \"PlannedEaten\" >= 0 AND \"PlannedEaten\" <= \"PlannedPrepared\"");
                table.CheckConstraint("CK_CookingEvents_Snapshot", "\"RecipeRevision\" > 0 AND jsonb_typeof(\"RecipeSnapshotJson\") = 'object'");
                table.CheckConstraint("CK_CookingEvents_Type", "\"MealType\" = 'Cook'");
                table.ForeignKey(
                    name: "FK_CookingEvents_Recipes_HouseholdId_RecipeId",
                    columns: x => new { x.HouseholdId, x.RecipeId },
                    principalSchema: "recipes",
                    principalTable: "Recipes",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CookingEvents_ScheduledMeals_HouseholdId_ScheduledMealId_Me~",
                    columns: x => new { x.HouseholdId, x.ScheduledMealId, x.MealType },
                    principalSchema: "planning",
                    principalTable: "ScheduledMeals",
                    principalColumns: new[] { "HouseholdId", "Id", "Type" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Allocations",
            schema: "leftovers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduledMealId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceCookingEventId = table.Column<Guid>(type: "uuid", nullable: false),
                PlannedPortions = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                ConsumedPortions = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                ReservedRemainder = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                MealType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Leftovers")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Allocations", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_Allocations_Portions", "\"PlannedPortions\" > 0 AND \"PlannedPortions\" <= 1000000\nAND \"ReservedRemainder\" >= 0 AND \"ReservedRemainder\" <= 1000000\nAND ((\"ConsumedPortions\" IS NULL AND \"ConfirmedAt\" IS NULL)\n  OR (\"ConsumedPortions\" IS NOT NULL AND \"ConfirmedAt\" IS NOT NULL AND \"ConsumedPortions\" >= 0 AND \"ConsumedPortions\" <= 1000000))");
                table.CheckConstraint("CK_Allocations_Type", "\"MealType\" = 'Leftovers'");
                table.ForeignKey(
                    name: "FK_Allocations_CookingEvents_HouseholdId_SourceCookingEventId",
                    columns: x => new { x.HouseholdId, x.SourceCookingEventId },
                    principalSchema: "planning",
                    principalTable: "CookingEvents",
                    principalColumns: new[] { "HouseholdId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Allocations_ScheduledMeals_HouseholdId_ScheduledMealId_Meal~",
                    columns: x => new { x.HouseholdId, x.ScheduledMealId, x.MealType },
                    principalSchema: "planning",
                    principalTable: "ScheduledMeals",
                    principalColumns: new[] { "HouseholdId", "Id", "Type" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Allocations_HouseholdId_ScheduledMealId",
            schema: "leftovers",
            table: "Allocations",
            columns: new[] { "HouseholdId", "ScheduledMealId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Allocations_HouseholdId_ScheduledMealId_MealType",
            schema: "leftovers",
            table: "Allocations",
            columns: new[] { "HouseholdId", "ScheduledMealId", "MealType" });

        migrationBuilder.CreateIndex(
            name: "IX_Allocations_HouseholdId_SourceCookingEventId",
            schema: "leftovers",
            table: "Allocations",
            columns: new[] { "HouseholdId", "SourceCookingEventId" });

        migrationBuilder.CreateIndex(
            name: "IX_CookingEvents_HouseholdId_RecipeId",
            schema: "planning",
            table: "CookingEvents",
            columns: new[] { "HouseholdId", "RecipeId" });

        migrationBuilder.CreateIndex(
            name: "IX_CookingEvents_HouseholdId_ScheduledMealId",
            schema: "planning",
            table: "CookingEvents",
            columns: new[] { "HouseholdId", "ScheduledMealId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CookingEvents_HouseholdId_ScheduledMealId_MealType",
            schema: "planning",
            table: "CookingEvents",
            columns: new[] { "HouseholdId", "ScheduledMealId", "MealType" });

        migrationBuilder.CreateIndex(
            name: "IX_MealSlots_HouseholdId_Position",
            schema: "planning",
            table: "MealSlots",
            columns: new[] { "HouseholdId", "Position" });

        migrationBuilder.CreateIndex(
            name: "IX_ScheduledMeals_HouseholdId_Date_SlotId",
            schema: "planning",
            table: "ScheduledMeals",
            columns: new[] { "HouseholdId", "Date", "SlotId" });

        migrationBuilder.CreateIndex(
            name: "IX_ScheduledMeals_HouseholdId_SlotId",
            schema: "planning",
            table: "ScheduledMeals",
            columns: new[] { "HouseholdId", "SlotId" });
        migrationBuilder.Sql("""
            INSERT INTO planning."MealSlots" ("Id", "HouseholdId", "Name", "Position", "IsDefault", "Archived", "Revision")
            SELECT gen_random_uuid(), h."Id", s.name, s.position, true, false, 1
            FROM households."Households" AS h
            CROSS JOIN (VALUES ('Breakfast', 0), ('Lunch', 1), ('Dinner', 2)) AS s(name, position);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Allocations",
            schema: "leftovers");

        migrationBuilder.DropTable(
            name: "CookingEvents",
            schema: "planning");

        migrationBuilder.DropTable(
            name: "ScheduledMeals",
            schema: "planning");

        migrationBuilder.DropTable(
            name: "MealSlots",
            schema: "planning");
    }
}
