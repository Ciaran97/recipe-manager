using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ImportReviewDraftsAndDurableHandoffs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "imports");

        migrationBuilder.CreateTable(
            name: "Handoffs",
            schema: "imports",
            columns: table => new
            {
                HandoffId = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                CommitHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                OriginalRecipeId = table.Column<Guid>(type: "uuid", nullable: true),
                CommittedRevision = table.Column<long>(type: "bigint", nullable: true),
                CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Handoffs", x => new { x.HouseholdId, x.UserId, x.HandoffId });
                table.CheckConstraint("CK_ImportHandoff_CommitHash", "\"CommitHash\" IS NULL OR \"CommitHash\" ~ '^[0-9A-F]{64}$'");
                table.CheckConstraint("CK_ImportHandoff_InputHash", "\"InputHash\" ~ '^[0-9A-F]{64}$'");
                table.CheckConstraint("CK_ImportHandoff_Lifetime", "\"ExpiresAt\" = LEAST(\"CapturedAt\" + interval '7 days', \"RegisteredAt\" + interval '7 days')");
                table.CheckConstraint("CK_ImportHandoff_Result", "(\"State\" = 'Committed' AND \"CommitHash\" IS NOT NULL AND \"OriginalRecipeId\" IS NOT NULL AND \"CommittedRevision\" IS NOT NULL AND \"CommittedRevision\" >= 1 AND \"CommittedAt\" IS NOT NULL) OR (\"State\" <> 'Committed' AND \"CommitHash\" IS NULL AND \"OriginalRecipeId\" IS NULL AND \"CommittedRevision\" IS NULL AND \"CommittedAt\" IS NULL)");
                table.CheckConstraint("CK_ImportHandoff_State", "\"State\" IN ('Open', 'Committed', 'Expired')");
                table.ForeignKey(
                    name: "FK_Handoffs_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Handoffs_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Drafts",
            schema: "imports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                HandoffId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceKind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                OriginalInput = table.Column<string>(type: "text", nullable: false),
                ExtractedJson = table.Column<string>(type: "jsonb", nullable: false),
                UncertaintyJson = table.Column<string>(type: "jsonb", nullable: false),
                MeasurementStandard = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Drafts", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_ImportDraft_Input", "octet_length(\"OriginalInput\") <= 102400");
                table.CheckConstraint("CK_ImportDraft_Kind", "\"SourceKind\" IN ('Url', 'Text')");
                table.CheckConstraint("CK_ImportDraft_Lifetime", "\"ExpiresAt\" = \"CreatedAt\" + interval '24 hours'");
                table.CheckConstraint("CK_ImportDraft_Revision", "\"Revision\" >= 1");
                table.CheckConstraint("CK_ImportDraft_Standard", "\"MeasurementStandard\" IS NULL OR \"MeasurementStandard\" IN ('AU', 'US')");
                table.ForeignKey(
                    name: "FK_Drafts_Handoffs_HouseholdId_RequestedBy_HandoffId",
                    columns: x => new { x.HouseholdId, x.RequestedBy, x.HandoffId },
                    principalSchema: "imports",
                    principalTable: "Handoffs",
                    principalColumns: new[] { "HouseholdId", "UserId", "HandoffId" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Drafts_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Drafts_Users_RequestedBy",
                    column: x => x.RequestedBy,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_ExpiresAt",
            schema: "imports",
            table: "Drafts",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_HouseholdId_RequestedBy_HandoffId",
            schema: "imports",
            table: "Drafts",
            columns: new[] { "HouseholdId", "RequestedBy", "HandoffId" });

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_RequestedBy",
            schema: "imports",
            table: "Drafts",
            column: "RequestedBy");

        migrationBuilder.CreateIndex(
            name: "IX_Handoffs_UserId",
            schema: "imports",
            table: "Handoffs",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Drafts",
            schema: "imports");

        migrationBuilder.DropTable(
            name: "Handoffs",
            schema: "imports");
    }
}
