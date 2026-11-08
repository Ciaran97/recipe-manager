using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Recipes;

namespace RecipeManager.Infrastructure.Persistence.Recipes;

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> entity) => OrganiserConfiguration.Configure(entity, "Collections");
}
public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> entity) => OrganiserConfiguration.Configure(entity, "Tags");
}
internal static class OrganiserConfiguration
{
    internal static void Configure<T>(EntityTypeBuilder<T> entity, string table) where T : class, IRecipeOrganiser
    {
        entity.ToTable(table, "recipes", t => t.HasCheckConstraint("CK_" + table + "_Revision", "\"Revision\" >= 1"));
        entity.HasKey(x => new { x.HouseholdId, x.Id });
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasIndex(x => new { x.HouseholdId, x.NormalizedName }).IsUnique();
        entity.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class RecipeCollectionConfiguration : IEntityTypeConfiguration<RecipeCollection>
{
    public void Configure(EntityTypeBuilder<RecipeCollection> entity)
    {
        entity.ToTable("RecipeCollections", "recipes"); entity.HasKey(x => new { x.HouseholdId, x.RecipeId, x.CollectionId });
        entity.HasOne<Recipe>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.RecipeId }).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Collection>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.CollectionId }).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class RecipeTagConfiguration : IEntityTypeConfiguration<RecipeTag>
{
    public void Configure(EntityTypeBuilder<RecipeTag> entity)
    {
        entity.ToTable("RecipeTags", "recipes"); entity.HasKey(x => new { x.HouseholdId, x.RecipeId, x.TagId });
        entity.HasOne<Recipe>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.RecipeId }).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Tag>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.TagId }).OnDelete(DeleteBehavior.Cascade);
    }
}
