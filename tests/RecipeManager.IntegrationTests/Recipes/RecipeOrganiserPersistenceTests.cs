using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Recipes;

public sealed class RecipeOrganiserPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Tenant_filters_and_composite_foreign_keys_protect_organisers_and_links()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var first = new Household(Guid.NewGuid(), "First", "UTC"); var other = new Household(Guid.NewGuid(), "Other", "UTC");
        var collection = new Collection(Guid.NewGuid(), first.Id, "Shared"); var foreign = new Collection(Guid.NewGuid(), other.Id, "Shared");
        var tag = new Tag(Guid.NewGuid(), first.Id, "Shared");
        var recipe = new Recipe(Guid.NewGuid(), first.Id, new RecipeWrite("Empty recipe", 2m, [], [], [], [], []));
        db.AddRange(first, other, collection, foreign, tag, recipe); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Empty(await db.Collections.ToArrayAsync()); Assert.Empty(await db.Tags.ToArrayAsync());
        db.BindHouseholdScope(first.Id); Assert.Single(await db.Collections.ToArrayAsync()); Assert.Single(await db.Tags.ToArrayAsync());
        db.RecipeCollections.Add(new RecipeCollection(first.Id, recipe.Id, foreign.Id));
        var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(invalid.InnerException).SqlState);
        db.ChangeTracker.Clear(); db.Collections.Add(new Collection(Guid.NewGuid(), first.Id, " SHARED "));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
    }
}
