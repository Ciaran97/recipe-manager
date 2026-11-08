using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Shopping;

public sealed class ShoppingReferences(RecipeManagerDbContext database) : IRecipeReferences, IIngredientReferences
{
    public async Task<bool> UsesRecipeAsync(Guid householdId, Guid recipeId, CancellationToken cancellationToken)
    {
        var sources = JsonSerializer.Serialize(new[] { new { recipeId } });
        if (await database.ShoppingLists.AnyAsync(x => x.HouseholdId == householdId && EF.Functions.JsonContains(x.SourceRevisionsJson, sources), cancellationToken)) return true;
        var retained = JsonSerializer.Serialize(new { contributions = new[] { new { sourceRecipeId = recipeId } } });
        return await database.ShoppingItems.AnyAsync(x => x.HouseholdId == householdId && x.GeneratedJson != null
            && EF.Functions.JsonContains(x.GeneratedJson, retained), cancellationToken);
    }
    public Task<bool> UsesIngredientAsync(Guid householdId, Guid ingredientId, CancellationToken cancellationToken)
        => database.ShoppingItems.AnyAsync(x => x.HouseholdId == householdId && x.IngredientId == ingredientId, cancellationToken);
    public Task IngredientChangedAsync(Guid householdId, Guid ingredientId, CancellationToken cancellationToken)
        => Task.CompletedTask; // Existing lists keep their saved names/categories until explicit reconciliation.
}
