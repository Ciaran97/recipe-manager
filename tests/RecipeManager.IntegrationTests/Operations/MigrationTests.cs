using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Operations;

public sealed class MigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Checked_in_migrations_upgrade_existing_recipe_data_and_match_the_current_model()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.EnsureDeletedAsync();
        var migrations = db.Database.GetMigrations().ToArray(); Assert.True(migrations.Length > 1);
        await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        var home = new Household(Guid.NewGuid(), "Upgrade fixture", "UTC");
        var recipe = new Recipe(Guid.NewGuid(), home.Id, new RecipeWrite("Retained recipe", 2m, [], [], [], [], []));
        db.AddRange(home, recipe); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        db.BindHouseholdScope(home.Id);
        Assert.Equal("Retained recipe", (await db.Recipes.SingleAsync()).Title);
        db.Collections.Add(new Collection(Guid.NewGuid(), home.Id, "After upgrade")); await db.SaveChangesAsync();
        Assert.Single(await db.Collections.ToArrayAsync());
        await db.Database.MigrateAsync(); // The operator's one-shot migrator is safe to rerun.
        Assert.Single(await db.Recipes.ToArrayAsync());
    }
}
