using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.MealPlanning;

public sealed class MealPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private RecipeManagerDbContext Open() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
    [Fact]
    public async Task Upgrade_preserves_existing_data_and_seeds_default_slots_without_model_drift()
    {
        await using var db = Open(); await db.Database.EnsureDeletedAsync();
        await db.GetService<IMigrator>().MigrateAsync("20261101170957_StructuredRecipes");
        var household = new Household(Guid.NewGuid(), "Existing household", "Australia/Perth");
        db.Households.Add(household); await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear(); db.BindHouseholdScope(household.Id);
        Assert.Equal("Existing household", (await db.Households.SingleAsync()).Name);
        Assert.Equal(new[] { "Breakfast", "Lunch", "Dinner" }, await db.MealSlots.OrderBy(x => x.Position).Select(x => x.Name).ToArrayAsync());
        Assert.All(await db.MealSlots.ToArrayAsync(), slot => Assert.True(slot.IsDefault));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Allocation_storage_preserves_released_reservations_and_restricts_source_deletion()
    {
        await using var db = Open(); await db.Database.MigrateAsync();
        var household = new Household(Guid.NewGuid(), "Portions", "UTC");
        var slot = new MealSlot(Guid.NewGuid(), household.Id, "Dinner", 2, true);
        var recipe = new Recipe(Guid.NewGuid(), household.Id, new RecipeWrite("Rice", 2m, [], [], [], [], []));
        var meal = new ScheduledMeal(Guid.NewGuid(), household.Id, new DateOnly(2026, 11, 2), slot.Id, MealType.Cook);
        var source = new CookingEvent(Guid.NewGuid(), household.Id, meal.Id, recipe.Id, recipe.Revision, "{\"schemaVersion\":1,\"title\":\"Frozen rice\"}", 4m, 2m);
        source.SetActuals(4m, 2m, DateTimeOffset.UtcNow);
        var leftoverMeal = new ScheduledMeal(Guid.NewGuid(), household.Id, meal.Date.AddDays(1), slot.Id, MealType.Leftovers);
        var allocation = new Allocation(Guid.NewGuid(), household.Id, leftoverMeal.Id, source.Id, 2m);
        allocation.SetConsumption(1.5m, 0.5m, DateTimeOffset.UtcNow); allocation.ReleaseReservation();
        db.AddRange(household, slot, recipe, meal, source, leftoverMeal, allocation); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Empty(await db.MealSlots.ToArrayAsync()); Assert.Empty(await db.ScheduledMeals.ToArrayAsync());
        Assert.Empty(await db.CookingEvents.ToArrayAsync()); Assert.Empty(await db.Allocations.ToArrayAsync());
        db.BindHouseholdScope(household.Id);
        var restored = Assert.Single(await db.Allocations.ToArrayAsync());
        Assert.Equal(2m, restored.PlannedPortions); Assert.Equal(1.5m, restored.ConsumedPortions); Assert.Equal(0m, restored.ReservedRemainder);
        Assert.Equal(0.5m, AllocationBalance.Available(2m, [restored]));
        var deletion = await Assert.ThrowsAsync<PostgresException>(() => db.CookingEvents.Where(x => x.Id == source.Id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, deletion.SqlState);
        await db.ScheduledMeals.Where(x => x.Id == leftoverMeal.Id).ExecuteDeleteAsync();
        Assert.Empty(await db.Allocations.ToArrayAsync());
        Assert.Equal(1, await db.CookingEvents.Where(x => x.Id == source.Id).ExecuteDeleteAsync());
        Assert.Single(await db.Recipes.ToArrayAsync());
    }

    [Fact]
    public async Task Tenant_links_meal_type_and_actual_pair_are_database_constraints()
    {
        await using var db = Open(); await db.Database.MigrateAsync();
        var first = new Household(Guid.NewGuid(), "First planner", "UTC"); var second = new Household(Guid.NewGuid(), "Second planner", "UTC");
        var slot = new MealSlot(Guid.NewGuid(), first.Id, "Dinner", 2);
        var foreignSlot = new MealSlot(Guid.NewGuid(), second.Id, "Dinner", 2);
        var recipe = new Recipe(Guid.NewGuid(), first.Id, new RecipeWrite("Rice", 2m, [], [], [], [], []));
        var other = new ScheduledMeal(Guid.NewGuid(), first.Id, new DateOnly(2026, 11, 2), slot.Id, MealType.Other, "Takeaway");
        db.AddRange(first, second, slot, foreignSlot, recipe, other); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); db.BindHouseholdScope(first.Id);
        db.Add(new ScheduledMeal(Guid.NewGuid(), first.Id, other.Date, foreignSlot.Id, MealType.Other, "Foreign slot"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>((await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException).SqlState);
        db.ChangeTracker.Clear();
        db.Add(new CookingEvent(Guid.NewGuid(), first.Id, other.Id, recipe.Id, 1, "{}", 4m, 2m));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>((await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException).SqlState);
        db.ChangeTracker.Clear();
        var cook = new ScheduledMeal(Guid.NewGuid(), first.Id, other.Date, slot.Id, MealType.Cook);
        var source = new CookingEvent(Guid.NewGuid(), first.Id, cook.Id, recipe.Id, 1, "{}", 4m, 2m);
        db.AddRange(cook, source); await db.SaveChangesAsync();
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE planning.\"CookingEvents\" SET \"ActualPrepared\" = 4 WHERE \"HouseholdId\" = {first.Id} AND \"Id\" = {source.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
    }
}
