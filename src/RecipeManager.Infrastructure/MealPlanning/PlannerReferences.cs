using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.MealPlanning;

public sealed class PlannerReferences(RecipeManagerDbContext database) : IRecipeReferences, IImageReferences, IIngredientReferences
{
    public Task<bool> UsesRecipeAsync(Guid householdId, Guid recipeId, CancellationToken cancellationToken)
        => database.CookingEvents.AnyAsync(x => x.HouseholdId == householdId && x.RecipeId == recipeId, cancellationToken);
    public Task<bool> UsesImageAsync(Guid householdId, Guid imageAssetId, CancellationToken cancellationToken)
    {
        var needle = JsonSerializer.Serialize(new { imageAssetId });
        return database.CookingEvents.AnyAsync(x => x.HouseholdId == householdId && EF.Functions.JsonContains(x.RecipeSnapshotJson, needle), cancellationToken);
    }
    public Task<bool> UsesIngredientAsync(Guid householdId, Guid ingredientId, CancellationToken cancellationToken)
    {
        var needle = JsonSerializer.Serialize(new { ingredients = new[] { new { ingredientId } } });
        return database.CookingEvents.AnyAsync(x => x.HouseholdId == householdId && EF.Functions.JsonContains(x.RecipeSnapshotJson, needle), cancellationToken);
    }
    public Task IngredientChangedAsync(Guid householdId, Guid ingredientId, CancellationToken cancellationToken)
        => Task.CompletedTask; // Catalogue edits never rewrite frozen recipe bases.
}
