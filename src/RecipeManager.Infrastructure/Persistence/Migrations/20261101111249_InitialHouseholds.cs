using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RecipeManager.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialHouseholds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "households");

        migrationBuilder.EnsureSchema(
            name: "identity");

        migrationBuilder.CreateTable(
            name: "Households",
            schema: "households",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                TimeZone = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                ContentRevision = table.Column<long>(type: "bigint", nullable: false),
                RecipesRevision = table.Column<long>(type: "bigint", nullable: false),
                PlannerRevision = table.Column<long>(type: "bigint", nullable: false),
                ShoppingRevision = table.Column<long>(type: "bigint", nullable: false),
                SettingsRevision = table.Column<long>(type: "bigint", nullable: false),
                MembershipRevision = table.Column<long>(type: "bigint", nullable: false),
                ShoppingSourcesRevision = table.Column<long>(type: "bigint", nullable: false),
                ProtectedAiSettings = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Households", x => x.Id);
                table.CheckConstraint("CK_Households_Revision", "\"Revision\" >= 0");
            });

        migrationBuilder.CreateTable(
            name: "Installation",
            schema: "households",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SetupCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Installation", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PreferencesJson = table.Column<string>(type: "jsonb", nullable: false),
                PreferencesRevision = table.Column<long>(type: "bigint", nullable: false),
                UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                PasswordHash = table.Column<string>(type: "text", nullable: true),
                SecurityStamp = table.Column<string>(type: "text", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                PhoneNumber = table.Column<string>(type: "text", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
                table.CheckConstraint("CK_Users_PreferencesObject", "jsonb_typeof(\"PreferencesJson\") = 'object'");
            });

        migrationBuilder.CreateTable(
            name: "Memberships",
            schema: "households",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Memberships", x => new { x.HouseholdId, x.Id });
                table.UniqueConstraint("AK_Memberships_HouseholdId_UserId", x => new { x.HouseholdId, x.UserId });
                table.CheckConstraint("CK_Memberships_Revision", "\"Revision\" >= 0");
                table.CheckConstraint("CK_Memberships_Role", "\"Role\" IN ('Administrator', 'Member')");
                table.ForeignKey(
                    name: "FK_Memberships_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Memberships_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserClaims",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserClaims_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserLogins",
            schema: "identity",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                ProviderKey = table.Column<string>(type: "text", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_UserLogins_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserTokens",
            schema: "identity",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_UserTokens_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Invitations",
            schema: "households",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Invitations", x => new { x.HouseholdId, x.Id });
                table.CheckConstraint("CK_Invitations_TokenHash", "length(\"TokenHash\") = 64");
                table.ForeignKey(
                    name: "FK_Invitations_Households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalSchema: "households",
                    principalTable: "Households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invitations_Memberships_HouseholdId_CreatedBy",
                    columns: x => new { x.HouseholdId, x.CreatedBy },
                    principalSchema: "households",
                    principalTable: "Memberships",
                    principalColumns: new[] { "HouseholdId", "UserId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            schema: "households",
            table: "Installation",
            columns: new[] { "Id", "Revision", "SetupCompletedAt" },
            values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), 1L, null });

        migrationBuilder.CreateIndex(
            name: "IX_Invitations_HouseholdId_CreatedBy",
            schema: "households",
            table: "Invitations",
            columns: new[] { "HouseholdId", "CreatedBy" });

        migrationBuilder.CreateIndex(
            name: "IX_Invitations_TokenHash",
            schema: "households",
            table: "Invitations",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Memberships_UserId",
            schema: "households",
            table: "Memberships",
            column: "UserId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserClaims_UserId",
            schema: "identity",
            table: "UserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserLogins_UserId",
            schema: "identity",
            table: "UserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            schema: "identity",
            table: "Users",
            column: "NormalizedEmail",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            schema: "identity",
            table: "Users",
            column: "NormalizedUserName",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Installation",
            schema: "households");

        migrationBuilder.DropTable(
            name: "Invitations",
            schema: "households");

        migrationBuilder.DropTable(
            name: "UserClaims",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "UserLogins",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "UserTokens",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "Memberships",
            schema: "households");

        migrationBuilder.DropTable(
            name: "Households",
            schema: "households");

        migrationBuilder.DropTable(
            name: "Users",
            schema: "identity");
    }
}
