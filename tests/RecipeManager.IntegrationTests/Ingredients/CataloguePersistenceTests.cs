using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Ingredients;

public sealed class CataloguePersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private RecipeManagerDbContext Open() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);

    [Fact]
    public async Task Catalogue_upgrade_preserves_existing_household_and_seeds_its_categories()
    {
        await using var db = Open(); await db.Database.EnsureDeletedAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261101114916_CreationRetries");
        var household = new Household(Guid.NewGuid(), "Existing household", "Australia/Perth");
        db.Households.Add(household); await db.SaveChangesAsync();
        await migrator.MigrateAsync(); db.ChangeTracker.Clear(); db.BindHouseholdScope(household.Id);
        Assert.Equal("Existing household", (await db.Households.SingleAsync()).Name);
        Assert.Equal(new[] { "Produce", "Meat", "Dairy", "Pantry" }, await db.IngredientCategories.OrderBy(x => x.SortOrder).Select(x => x.Name).ToArrayAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Catalogue_tables_fail_closed_enforce_unified_names_and_block_foreign_category_links()
    {
        await using var db = Open(); await db.Database.MigrateAsync();
        var first = new Household(Guid.NewGuid(), "First", "UTC"); var second = new Household(Guid.NewGuid(), "Second", "UTC");
        var category = new IngredientCategory(Guid.NewGuid(), second.Id, "Produce", 0);
        var ingredient = new Ingredient(Guid.NewGuid(), first.Id, "Capsicum");
        var alias = new IngredientAlias(Guid.NewGuid(), first.Id, ingredient.Id, "Red pepper");
        db.AddRange(first, second, category, ingredient, alias,
            new IngredientName(first.Id, ingredient.Id, ingredient.CanonicalName, true), new IngredientName(first.Id, ingredient.Id, alias.Name, false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Ingredients.ToListAsync()); Assert.Empty(await db.IngredientAliases.ToListAsync());
        Assert.Empty(await db.IngredientNames.ToListAsync()); Assert.Empty(await db.IngredientCategories.ToListAsync());
        db.BindHouseholdScope(first.Id);
        Assert.Single(await db.Ingredients.ToListAsync()); Assert.Single(await db.IngredientAliases.ToListAsync());
        Assert.Equal(2, await db.IngredientNames.CountAsync()); Assert.Empty(await db.IngredientCategories.ToListAsync());
        var duplicate = new Ingredient(Guid.NewGuid(), first.Id, "Red pepper");
        db.AddRange(duplicate, new IngredientName(first.Id, duplicate.Id, duplicate.CanonicalName, true));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var foreignCategory = new Ingredient(Guid.NewGuid(), first.Id, "Rice", category.Id); db.Add(foreignCategory);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.BindHouseholdScope(second.Id);
        Assert.Empty(await db.Ingredients.ToListAsync()); Assert.Single(await db.IngredientCategories.ToListAsync());
        var sameNameOtherTenant = new Ingredient(Guid.NewGuid(), second.Id, "Red pepper");
        db.AddRange(sameNameOtherTenant, new IngredientName(second.Id, sameNameOtherTenant.Id, "Red pepper", true));
        await db.SaveChangesAsync(); Assert.Single(await db.Ingredients.ToListAsync());
    }
}
