using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Recipes;

namespace RecipeManager.Infrastructure.Persistence.MealPlanning;

public sealed class MealSlotConfiguration : IEntityTypeConfiguration<MealSlot>
{
    public void Configure(EntityTypeBuilder<MealSlot> entity)
    {
        entity.ToTable("MealSlots", "planning", table => table.HasCheckConstraint("CK_MealSlots_Position", "\"Position\" >= 0"));
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasIndex(x => new { x.HouseholdId, x.Position });
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ScheduledMealConfiguration : IEntityTypeConfiguration<ScheduledMeal>
{
    public void Configure(EntityTypeBuilder<ScheduledMeal> entity)
    {
        entity.ToTable("ScheduledMeals", "planning", table => table.HasCheckConstraint("CK_ScheduledMeals_Type", """
            ("Type" = 'Other' AND "Label" IS NOT NULL AND length(trim("Label")) > 0)
            OR ("Type" IN ('Cook', 'Leftovers') AND "Label" IS NULL)
            """));
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.HasAlternateKey(x => new { x.HouseholdId, x.Id, x.Type });
        entity.Property(x => x.Date).HasColumnType("date");
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.Label).HasMaxLength(200);
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasIndex(x => new { x.HouseholdId, x.Date, x.SlotId });
        entity.HasOne<MealSlot>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.SlotId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class CookingEventConfiguration : IEntityTypeConfiguration<CookingEvent>
{
    public void Configure(EntityTypeBuilder<CookingEvent> entity)
    {
        entity.ToTable("CookingEvents", "planning", table =>
        {
            table.HasCheckConstraint("CK_CookingEvents_Planned", "\"PlannedPrepared\" > 0 AND \"PlannedPrepared\" <= 1000000 AND \"PlannedEaten\" >= 0 AND \"PlannedEaten\" <= \"PlannedPrepared\"");
            table.HasCheckConstraint("CK_CookingEvents_Actual", """
                ("ActualPrepared" IS NULL AND "ActualEaten" IS NULL AND "ConfirmedAt" IS NULL)
                OR ("ActualPrepared" IS NOT NULL AND "ActualEaten" IS NOT NULL AND "ConfirmedAt" IS NOT NULL
                    AND "ActualPrepared" > 0 AND "ActualPrepared" <= 1000000 AND "ActualEaten" >= 0 AND "ActualEaten" <= "ActualPrepared")
                """);
            table.HasCheckConstraint("CK_CookingEvents_Type", "\"MealType\" = 'Cook'");
            table.HasCheckConstraint("CK_CookingEvents_Snapshot", "\"RecipeRevision\" > 0 AND jsonb_typeof(\"RecipeSnapshotJson\") = 'object'");
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.RecipeSnapshotJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.Ignore(x => x.Portions);
        entity.Property(x => x.PlannedPrepared).HasPrecision(18, 6);
        entity.Property(x => x.PlannedEaten).HasPrecision(18, 6);
        entity.Property(x => x.ActualPrepared).HasPrecision(18, 6);
        entity.Property(x => x.ActualEaten).HasPrecision(18, 6);
        entity.HasIndex(x => new { x.HouseholdId, x.ScheduledMealId }).IsUnique();
        entity.Property(x => x.MealType).HasConversion<string>().HasMaxLength(16).HasDefaultValue(MealType.Cook);
        entity.HasOne<ScheduledMeal>().WithMany().HasForeignKey("HouseholdId", "ScheduledMealId", "MealType")
            .HasPrincipalKey(x => new { x.HouseholdId, x.Id, x.Type }).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Recipe>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.RecipeId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AllocationConfiguration : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> entity)
    {
        entity.ToTable("Allocations", "leftovers", table =>
        {
            table.HasCheckConstraint("CK_Allocations_Portions", """
                "PlannedPortions" > 0 AND "PlannedPortions" <= 1000000
                AND "ReservedRemainder" >= 0 AND "ReservedRemainder" <= 1000000
                AND (("ConsumedPortions" IS NULL AND "ConfirmedAt" IS NULL)
                  OR ("ConsumedPortions" IS NOT NULL AND "ConfirmedAt" IS NOT NULL AND "ConsumedPortions" >= 0 AND "ConsumedPortions" <= 1000000))
                """);
            table.HasCheckConstraint("CK_Allocations_Type", "\"MealType\" = 'Leftovers'");
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Ignore(x => x.Commitment);
        entity.Property(x => x.PlannedPortions).HasPrecision(18, 6);
        entity.Property(x => x.ConsumedPortions).HasPrecision(18, 6);
        entity.Property(x => x.ReservedRemainder).HasPrecision(18, 6);
        entity.HasIndex(x => new { x.HouseholdId, x.ScheduledMealId }).IsUnique();
        entity.HasIndex(x => new { x.HouseholdId, x.SourceCookingEventId });
        entity.Property(x => x.MealType).HasConversion<string>().HasMaxLength(16).HasDefaultValue(MealType.Leftovers);
        entity.HasOne<ScheduledMeal>().WithMany().HasForeignKey("HouseholdId", "ScheduledMealId", "MealType")
            .HasPrincipalKey(x => new { x.HouseholdId, x.Id, x.Type }).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CookingEvent>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.SourceCookingEventId }).OnDelete(DeleteBehavior.Restrict);
    }
}
