using Microsoft.Extensions.Configuration;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Ingredients;

public sealed class IngredientHouseholdInitializer(RecipeManagerDbContext database, IConfiguration configuration) : IHouseholdInitializer
{
    public Task InitializeAsync(Guid householdId, CancellationToken cancellationToken)
    {
        // Installation setup owns the transaction; names remain independently editable per household.
        var categories = StarterCatalogueV1.Categories.Select((name, index) => new IngredientCategory(Guid.NewGuid(), householdId, name, index)).ToArray();
        database.IngredientCategories.AddRange(categories);
        if (configuration.GetValue("StarterData:Enabled", true))
        {
            foreach (var group in StarterCatalogueV1.Ingredients)
                foreach (var name in group.Names)
                {
                    var ingredient = new Ingredient(Guid.NewGuid(), householdId, name, categories.Single(x => x.Name == group.Category).Id);
                    database.AddRange(ingredient, new IngredientName(householdId, ingredient.Id, name, true));
                }
            database.Collections.AddRange(StarterCatalogueV1.Collections.Select(name => new Collection(Guid.NewGuid(), householdId, name)));
            database.Tags.AddRange(StarterCatalogueV1.Tags.Select(name => new Tag(Guid.NewGuid(), householdId, name)));
        }
        return Task.CompletedTask;
    }
}
