using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Shopping;

namespace RecipeManager.Infrastructure.Persistence.Shopping;

public sealed class ShoppingListConfiguration : IEntityTypeConfiguration<ShoppingList>
{
    public void Configure(EntityTypeBuilder<ShoppingList> entity)
    {
        entity.ToTable("ShoppingLists", "shopping", table =>
        {
            table.HasCheckConstraint("CK_ShoppingLists_Versions", "\"Revision\" >= 1 AND \"PolicyVersion\" >= 1 AND \"HouseholdSourceRevision\" >= 0");
            table.HasCheckConstraint("CK_ShoppingLists_Basis", "\"Basis\" IN ('Planned','Actual') AND (\"Basis\" = 'Planned' OR \"SelectionJson\" IS NOT NULL)");
            table.HasCheckConstraint("CK_ShoppingLists_Snapshots", "jsonb_typeof(\"BaselineJson\") = 'object' AND jsonb_typeof(\"SourceRevisionsJson\") = 'array' AND (\"SelectionJson\" IS NULL OR jsonb_typeof(\"SelectionJson\") = 'object')");
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.Property(x => x.Basis).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.SelectionJson).HasColumnType("jsonb");
        entity.Property(x => x.BaselineJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.SourceRevisionsJson).HasColumnType("jsonb").IsRequired();
        entity.Ignore(x => x.Selection); entity.Ignore(x => x.Baseline); entity.Ignore(x => x.SourceRevisions);
        entity.HasIndex(x => new { x.HouseholdId, x.Name });
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ShoppingItemConfiguration : IEntityTypeConfiguration<ShoppingItem>
{
    public void Configure(EntityTypeBuilder<ShoppingItem> entity)
    {
        entity.ToTable("ShoppingItems", "shopping", table =>
        {
            table.HasCheckConstraint("CK_ShoppingItems_Position", "\"Position\" >= 0");
            table.HasCheckConstraint("CK_ShoppingItems_Identity", """
                ("Manual" AND "RequirementKey" IS NULL AND "IngredientId" IS NULL AND "GeneratedJson" IS NULL AND "OverrideJson" IS NOT NULL)
                OR (NOT "Manual" AND "RequirementKey" IS NOT NULL AND "IngredientId" IS NOT NULL AND "GeneratedJson" IS NOT NULL)
                """);
            table.HasCheckConstraint("CK_ShoppingItems_Values", "(\"GeneratedJson\" IS NULL OR jsonb_typeof(\"GeneratedJson\") = 'object') AND (\"OverrideJson\" IS NULL OR jsonb_typeof(\"OverrideJson\") = 'object')");
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.RequirementKey).HasMaxLength(300);
        entity.Property(x => x.Label).HasMaxLength(200).IsRequired();
        entity.Property(x => x.CategoryName).HasMaxLength(200);
        entity.Property(x => x.GeneratedJson).HasColumnType("jsonb");
        entity.Property(x => x.OverrideJson).HasColumnType("jsonb");
        entity.Ignore(x => x.Baseline); entity.Ignore(x => x.OverrideValue); entity.Ignore(x => x.HasOverride);
        entity.Ignore(x => x.EffectiveExact); entity.Ignore(x => x.Dimension); entity.Ignore(x => x.Unresolved); entity.Ignore(x => x.DisplayText);
        entity.HasIndex(x => new { x.HouseholdId, x.ListId, x.RequirementKey }).IsUnique().HasFilter("\"RequirementKey\" IS NOT NULL");
        entity.HasIndex(x => new { x.HouseholdId, x.ListId, x.Position });
        entity.HasOne<ShoppingList>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.ListId }).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.IngredientId }).OnDelete(DeleteBehavior.Restrict);
        // Category names/IDs are historical display snapshots, not live catalogue foreign keys.
    }
}
