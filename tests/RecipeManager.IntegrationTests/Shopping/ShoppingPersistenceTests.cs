using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Shopping;

public sealed class ShoppingPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private RecipeManagerDbContext Open() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
    [Fact]
    public async Task Migration_preserves_households_and_has_no_persisted_reconciliation_proposals_or_model_drift()
    {
        await using var db = Open(); await db.Database.EnsureDeletedAsync();
        await db.GetService<IMigrator>().MigrateAsync("20261101235120_MealPlanningAndAllocations");
        var household = new Household(Guid.NewGuid(), "Existing shopping household", "Australia/Perth");
        db.Add(household); await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); db.BindHouseholdScope(household.Id);
        Assert.Equal(household.Name, (await db.Households.SingleAsync()).Name);
        Assert.Empty(await db.ShoppingLists.ToArrayAsync()); Assert.False(db.Database.HasPendingModelChanges());
        Assert.DoesNotContain(db.Model.GetEntityTypes(), type => type.GetTableName()!.Contains("Reconciliation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Baselines_overrides_and_independent_unresolved_rows_survive_reload_with_household_filters()
    {
        await using var db = Open(); await db.Database.MigrateAsync();
        var household = new Household(Guid.NewGuid(), "Shopping", "UTC");
        var ingredient = new Ingredient(Guid.NewGuid(), household.Id, "Salt");
        var known = Requirement(ingredient.Id); var unknown = known with { Key = "unknown:source:row", Exact = null, Dimension = null, DisplayText = "to taste", Unresolved = true };
        var list = new ShoppingList(Guid.NewGuid(), household.Id, "Snapshot", null, ShoppingBasis.Planned, new([known, unknown], 1), [], 0);
        var numeric = ShoppingItem.Generated(Guid.NewGuid(), household.Id, list.Id, known, 0);
        var missing = ShoppingItem.Generated(Guid.NewGuid(), household.Id, list.Id, unknown, 1);
        numeric.Override(new("600", QuantityKind.Numeric, IngredientUnit.Gram)); numeric.SetPurchased(true); numeric.Remove();
        db.AddRange(household, ingredient, list, numeric, missing); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Empty(await db.ShoppingLists.ToArrayAsync()); Assert.Empty(await db.ShoppingItems.ToArrayAsync());
        db.BindHouseholdScope(household.Id);
        var saved = await db.ShoppingItems.SingleAsync(x => x.Id == numeric.Id);
        Assert.True(saved.Purchased); Assert.True(saved.Removed); Assert.True(saved.HasOverride);
        Assert.Equal(new ExactAmount(5, 1), saved.Baseline!.Exact!.ToAmount());
        Assert.Equal(new ExactAmount(600, 1), saved.EffectiveExact!.ToAmount());
        var unresolved = await db.ShoppingItems.SingleAsync(x => x.Id == missing.Id);
        Assert.True(unresolved.Unresolved); Assert.Null(unresolved.EffectiveExact); Assert.False(unresolved.Purchased);
        Assert.Equal(1, (await db.ShoppingLists.SingleAsync()).Baseline.UnresolvedCount);
        db.BindHouseholdScope(Guid.NewGuid()); Assert.Empty(await db.ShoppingItems.ToArrayAsync());
    }

    [Fact]
    public async Task Tenant_foreign_keys_and_snapshot_ingredient_references_are_enforced_by_postgresql()
    {
        await using var db = Open(); await db.Database.MigrateAsync();
        var first = new Household(Guid.NewGuid(), "First", "UTC"); var second = new Household(Guid.NewGuid(), "Second", "UTC");
        var ingredient = new Ingredient(Guid.NewGuid(), first.Id, "Rice");
        var a = new ShoppingList(Guid.NewGuid(), first.Id, "A", null, ShoppingBasis.Planned, new([], 0), [], 0);
        var b = new ShoppingList(Guid.NewGuid(), second.Id, "B", null, ShoppingBasis.Planned, new([], 0), [], 0);
        var row = ShoppingItem.Generated(Guid.NewGuid(), first.Id, a.Id, Requirement(ingredient.Id), 0);
        db.AddRange(first, second, ingredient, a, b, row); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); db.BindHouseholdScope(first.Id);
        db.Add(ShoppingItem.ManualItem(Guid.NewGuid(), first.Id, b.Id, "Foreign list", new("1", QuantityKind.Numeric, IngredientUnit.Item), 0));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState); db.ChangeTracker.Clear();
        var restricted = await Assert.ThrowsAsync<PostgresException>(() => db.Ingredients.Where(x => x.Id == ingredient.Id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, restricted.SqlState);
        Assert.Equal(1, await db.ShoppingLists.Where(x => x.Id == a.Id).ExecuteDeleteAsync());
        Assert.Empty(await db.ShoppingItems.ToArrayAsync());
        db.BindHouseholdScope(second.Id); Assert.Single(await db.ShoppingLists.ToArrayAsync());
    }

    private static ShoppingRequirement Requirement(Guid ingredient) => new($"known:{ingredient:N}:Mass", ingredient, "Salt", null, null,
        IngredientDimension.Mass, new("5", "1"), "5 g", false, false, true, []);
}
