using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CreationRetries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CreationRetries",
            schema: "households",
            columns: table => new
            {
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Key = table.Column<Guid>(type: "uuid", nullable: false),
                PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Body = table.Column<string>(type: "text", nullable: false),
                Location = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                ETag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CreationRetries", x => new { x.HouseholdId, x.UserId, x.Route, x.Key });
                table.ForeignKey(
                    name: "FK_CreationRetries_Memberships_HouseholdId_UserId",
                    columns: x => new { x.HouseholdId, x.UserId },
                    principalSchema: "households",
                    principalTable: "Memberships",
                    principalColumns: new[] { "HouseholdId", "UserId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CreationRetries_ExpiresAt",
            schema: "households",
            table: "CreationRetries",
            column: "ExpiresAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CreationRetries",
            schema: "households");
    }
}
