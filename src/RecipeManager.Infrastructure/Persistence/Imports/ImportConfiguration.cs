using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Infrastructure.Persistence.Imports;

public sealed class ImportDraftConfiguration : IEntityTypeConfiguration<ImportDraft>
{
    public void Configure(EntityTypeBuilder<ImportDraft> entity)
    {
        entity.ToTable("Drafts", "imports", table =>
        {
            table.HasCheckConstraint("CK_ImportDraft_Revision", "\"Revision\" >= 1");
            table.HasCheckConstraint("CK_ImportDraft_Lifetime", "\"ExpiresAt\" = \"CreatedAt\" + interval '24 hours'");
            table.HasCheckConstraint("CK_ImportDraft_Standard", "\"MeasurementStandard\" IS NULL OR \"MeasurementStandard\" IN ('AU', 'US')");
            table.HasCheckConstraint("CK_ImportDraft_Kind", "\"SourceKind\" IN ('Url', 'Text')");
            table.HasCheckConstraint("CK_ImportDraft_Input", "octet_length(\"OriginalInput\") <= 102400");
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(8);
        entity.Property(x => x.MeasurementStandard).HasConversion<string>().HasMaxLength(8);
        entity.Property(x => x.ExtractedJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.UncertaintyJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.OriginalInput).IsRequired();
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasIndex(x => x.ExpiresAt);
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ImportHandoff>().WithMany().HasForeignKey(x => new { x.HouseholdId, UserId = x.RequestedBy, x.HandoffId })
            .HasPrincipalKey(x => new { x.HouseholdId, x.UserId, x.HandoffId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ImportHandoffConfiguration : IEntityTypeConfiguration<ImportHandoff>
{
    public void Configure(EntityTypeBuilder<ImportHandoff> entity)
    {
        entity.ToTable("Handoffs", "imports", table =>
        {
            table.HasCheckConstraint("CK_ImportHandoff_State", "\"State\" IN ('Open', 'Committed', 'Expired')");
            table.HasCheckConstraint("CK_ImportHandoff_Lifetime", "\"ExpiresAt\" = LEAST(\"CapturedAt\" + interval '7 days', \"RegisteredAt\" + interval '7 days')");
            table.HasCheckConstraint("CK_ImportHandoff_Result", "(\"State\" = 'Committed' AND \"CommitHash\" IS NOT NULL AND \"OriginalRecipeId\" IS NOT NULL AND \"CommittedRevision\" IS NOT NULL AND \"CommittedRevision\" >= 1 AND \"CommittedAt\" IS NOT NULL) OR (\"State\" <> 'Committed' AND \"CommitHash\" IS NULL AND \"OriginalRecipeId\" IS NULL AND \"CommittedRevision\" IS NULL AND \"CommittedAt\" IS NULL)");
            table.HasCheckConstraint("CK_ImportHandoff_InputHash", "\"InputHash\" ~ '^[0-9A-F]{64}$'");
            table.HasCheckConstraint("CK_ImportHandoff_CommitHash", "\"CommitHash\" IS NULL OR \"CommitHash\" ~ '^[0-9A-F]{64}$'");
        });
        entity.HasKey(x => new { x.HouseholdId, x.UserId, x.HandoffId });
        entity.Ignore(x => x.Result);
        entity.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
        entity.Property(x => x.CommitHash).HasMaxLength(64);
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
