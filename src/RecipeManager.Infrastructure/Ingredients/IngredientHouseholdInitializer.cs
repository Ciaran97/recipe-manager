using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Ingredients;

public sealed class IngredientHouseholdInitializer(RecipeManagerDbContext database) : IHouseholdInitializer
{
    public Task InitializeAsync(Guid householdId, CancellationToken cancellationToken)
    {
        // The installation setup owns this transaction. Null CategoryId is the uncategorised fallback.
        var names = new[] { "Produce", "Meat", "Dairy", "Pantry" };
        database.IngredientCategories.AddRange(names.Select((name, index) => new IngredientCategory(Guid.NewGuid(), householdId, name, index)));
        return Task.CompletedTask;
    }
}
