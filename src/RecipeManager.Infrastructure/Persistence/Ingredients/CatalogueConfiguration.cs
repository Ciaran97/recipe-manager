using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Ingredients;

namespace RecipeManager.Infrastructure.Persistence.Ingredients;

public sealed class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> entity)
    {
        entity.ToTable("Ingredients", "ingredients", table => table.HasCheckConstraint("CK_Ingredients_Revision", "\"Revision\" >= 1"));
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.CanonicalName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasIndex(x => new { x.HouseholdId, x.NormalizedName }).IsUnique();
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IngredientCategory>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.CategoryId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class IngredientAliasConfiguration : IEntityTypeConfiguration<IngredientAlias>
{
    public void Configure(EntityTypeBuilder<IngredientAlias> entity)
    {
        entity.ToTable("IngredientAliases", "ingredients");
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => new { x.HouseholdId, x.NormalizedName }).IsUnique();
        entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.IngredientId }).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class IngredientNameConfiguration : IEntityTypeConfiguration<IngredientName>
{
    public void Configure(EntityTypeBuilder<IngredientName> entity)
    {
        entity.ToTable("IngredientNames", "ingredients");
        entity.HasKey(x => new { x.HouseholdId, x.NormalizedName });
        entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => new { x.HouseholdId, x.IngredientId, x.IsCanonical }).IsUnique().HasFilter("\"IsCanonical\"");
        entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.IngredientId }).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class IngredientCategoryConfiguration : IEntityTypeConfiguration<IngredientCategory>
{
    public void Configure(EntityTypeBuilder<IngredientCategory> entity)
    {
        entity.ToTable("Categories", "ingredients", table =>
        {
            table.HasCheckConstraint("CK_Categories_SortOrder", "\"SortOrder\" >= 0");
            table.HasCheckConstraint("CK_Categories_Revision", "\"Revision\" >= 1");
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasIndex(x => new { x.HouseholdId, x.NormalizedName }).IsUnique();
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
    }
}
