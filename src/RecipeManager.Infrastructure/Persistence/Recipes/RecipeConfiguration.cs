using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes;

namespace RecipeManager.Infrastructure.Persistence.Recipes;

public sealed class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> entity)
    {
        entity.ToTable("Recipes", "recipes", table =>
        {
            table.HasCheckConstraint("CK_Recipes_Servings", "\"DefaultServings\" > 0 AND \"DefaultServings\" <= 1000000");
            table.HasCheckConstraint("CK_Recipes_Times", "(\"PrepMinutes\" IS NULL OR \"PrepMinutes\" >= 0) AND (\"CookMinutes\" IS NULL OR \"CookMinutes\" >= 0)");
            table.HasCheckConstraint("CK_Recipes_Standard", "\"MeasurementStandard\" IN ('AU', 'US')");
            table.HasCheckConstraint("CK_Recipes_Revision", "\"Revision\" >= 1 AND \"PolicyVersion\" >= 1");
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(20000);
        entity.Property(x => x.Notes).HasMaxLength(20000);
        entity.Property(x => x.SourceUrl).HasMaxLength(2048);
        entity.Property(x => x.DefaultServings).HasPrecision(18, 6);
        entity.Property(x => x.MeasurementStandard).HasConversion<string>().HasMaxLength(2);
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasIndex(x => new { x.HouseholdId, x.Title });
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ImageAsset>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.ImageAssetId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class IngredientGroupConfiguration : IEntityTypeConfiguration<IngredientGroup>
{
    public void Configure(EntityTypeBuilder<IngredientGroup> entity)
    {
        entity.ToTable("IngredientGroups", "recipes", table => table.HasCheckConstraint("CK_IngredientGroups_Position", "\"Position\" >= 0"));
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.HasAlternateKey(x => new { x.HouseholdId, x.RecipeId, x.Id });
        entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => new { x.HouseholdId, x.RecipeId, x.Position }).IsUnique();
        entity.HasOne<Recipe>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.RecipeId }).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class RecipeIngredientConfiguration : IEntityTypeConfiguration<RecipeIngredient>
{
    public void Configure(EntityTypeBuilder<RecipeIngredient> entity)
    {
        entity.ToTable("RecipeIngredients", "recipes", table =>
        {
            table.HasCheckConstraint("CK_RecipeIngredients_Position", "\"Position\" >= 0");
            table.HasCheckConstraint("CK_RecipeIngredients_Unit", "\"Unit\" IN ('g', 'kg', 'ml', 'l', 'tsp', 'tbsp', 'cup', 'item', 'pinch', 'dash', 'handful', 'bunch', 'clove', 'slice')");
            table.HasCheckConstraint("CK_RecipeIngredients_Quantity", """
                ("Kind" = 'Unknown' AND "DecimalValue" IS NULL AND "FractionNumerator" IS NULL AND "FractionDenominator" IS NULL)
                OR ("Kind" = 'Numeric' AND (
                  ("DecimalValue" IS NOT NULL AND "DecimalValue" >= 0 AND "DecimalValue" <= 1000000000
                    AND "FractionNumerator" IS NULL AND "FractionDenominator" IS NULL)
                  OR ("DecimalValue" IS NULL AND "FractionNumerator" IS NOT NULL AND "FractionDenominator" IS NOT NULL
                    AND "FractionNumerator" >= 0 AND "FractionDenominator" > 0 AND "FractionDenominator" <= 1000000000
                    AND "FractionNumerator" <= 1000000000::bigint * "FractionDenominator")))
                """);
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Ignore(x => x.Quantity);
        entity.Property(x => x.QuantityText).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.Unit).HasConversion(x => IngredientCalculator.UnitCode(x), x => IngredientCalculator.ParseUnit(x)).HasMaxLength(16);
        entity.Property(x => x.DecimalValue).HasPrecision(18, 6);
        entity.Property(x => x.Preparation).HasMaxLength(20000);
        entity.HasIndex(x => new { x.HouseholdId, x.RecipeId, x.Position }).IsUnique();
        entity.HasOne<Recipe>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.RecipeId }).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.IngredientId }).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IngredientGroup>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.RecipeId, x.GroupId })
            .HasPrincipalKey(x => new { x.HouseholdId, x.RecipeId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
public sealed class InstructionConfiguration : IEntityTypeConfiguration<Instruction>
{
    public void Configure(EntityTypeBuilder<Instruction> entity)
    {
        entity.ToTable("Instructions", "recipes", table => table.HasCheckConstraint("CK_Instructions_Position", "\"Position\" >= 0"));
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.Text).HasMaxLength(20000).IsRequired();
        entity.HasIndex(x => new { x.HouseholdId, x.RecipeId, x.Position }).IsUnique();
        entity.HasOne<Recipe>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.RecipeId }).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class FavouriteConfiguration : IEntityTypeConfiguration<Favourite>
{
    public void Configure(EntityTypeBuilder<Favourite> entity)
    {
        entity.ToTable("Favourites", "recipes");
        entity.HasKey(x => new { x.HouseholdId, x.UserId, x.RecipeId });
        entity.HasOne<Recipe>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.RecipeId }).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.UserId })
            .HasPrincipalKey(x => new { x.HouseholdId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ImageAssetConfiguration : IEntityTypeConfiguration<ImageAsset>
{
    public void Configure(EntityTypeBuilder<ImageAsset> entity)
    {
        entity.ToTable("ImageAssets", "recipes", table =>
        {
            table.HasCheckConstraint("CK_ImageAssets_Size", "\"Size\" > 0");
            table.HasCheckConstraint("CK_ImageAssets_Media", "\"MediaType\" IN ('image/jpeg', 'image/png', 'image/webp')");
        });
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.StorageKey).HasMaxLength(32).IsRequired();
        entity.Property(x => x.MediaType).HasMaxLength(32).IsRequired();
        entity.HasIndex(x => new { x.HouseholdId, x.StorageKey }).IsUnique();
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
    }
}
