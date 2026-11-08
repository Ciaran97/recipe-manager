using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.MealPlanning;

public sealed class PlannerHouseholdInitializer(RecipeManagerDbContext database) : IHouseholdInitializer
{
    public Task InitializeAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var names = new[] { "Breakfast", "Lunch", "Dinner" };
        database.MealSlots.AddRange(names.Select((name, index) => new MealSlot(Guid.NewGuid(), householdId, name, index, isDefault: true)));
        return Task.CompletedTask;
    }
}
