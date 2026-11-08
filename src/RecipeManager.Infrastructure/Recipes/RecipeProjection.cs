using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Recipes;

// Shared batched live content projection. Personal favourites and organiser links are added only by library reads.
internal static class RecipeProjection
{
    internal static async Task<IReadOnlyList<RecipeRead>> ReadAsync(RecipeManagerDbContext database, Guid householdId, IReadOnlyList<Recipe> recipes, CancellationToken ct)
    {
        if (database.CurrentHouseholdId != householdId || recipes.Any(x => x.HouseholdId != householdId))
            throw new InvalidOperationException("Recipe projection requires the current household scope.");
        if (recipes.Count == 0) return [];
        var recipeIds = recipes.Select(x => x.Id).ToArray();
        var groups = await database.IngredientGroups.AsNoTracking().Where(x => x.HouseholdId == householdId && recipeIds.Contains(x.RecipeId)).OrderBy(x => x.Position).ToArrayAsync(ct);
        var rows = await database.RecipeIngredients.AsNoTracking().Where(x => x.HouseholdId == householdId && recipeIds.Contains(x.RecipeId)).OrderBy(x => x.Position).ToArrayAsync(ct);
        var ids = rows.Select(x => x.IngredientId).Distinct().ToArray();
        var catalogue = await database.Ingredients.AsNoTracking().Where(x => x.HouseholdId == householdId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var categoryIds = catalogue.Values.Where(x => x.CategoryId != null).Select(x => x.CategoryId!.Value).Distinct().ToArray();
        var categories = await database.IngredientCategories.AsNoTracking().Where(x => x.HouseholdId == householdId && categoryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var instructions = await database.Instructions.AsNoTracking().Where(x => x.HouseholdId == householdId && recipeIds.Contains(x.RecipeId)).OrderBy(x => x.Position).ToArrayAsync(ct);
        return recipes.Select(recipe => new RecipeRead(recipe.Id, recipe.Revision, recipe.Title, recipe.Description, recipe.ImageAssetId, recipe.DefaultServings,
            recipe.PrepMinutes, recipe.CookMinutes, recipe.Notes, recipe.SourceUrl, recipe.MeasurementStandard, recipe.PolicyVersion, recipe.Archived, false,
            groups.Where(x => x.RecipeId == recipe.Id).Select(x => new RecipeGroupRead(x.Id, x.Title, x.Position)).ToArray(),
            rows.Where(x => x.RecipeId == recipe.Id).Select(x => new RecipeIngredientRead(x.Id, x.GroupId, x.Position, x.IngredientId, catalogue[x.IngredientId].CanonicalName,
                catalogue[x.IngredientId].CategoryId, catalogue[x.IngredientId].CategoryId is { } category ? categories.GetValueOrDefault(category) : null,
                x.QuantityText, x.Kind, x.Unit, x.Preparation, x.Uncertain, x.Quantity.Exact is { } exact ? ExactQuantity.From(exact) : null)).ToArray(),
            instructions.Where(x => x.RecipeId == recipe.Id).Select(x => new RecipeInstructionRead(x.Id, x.Position, x.Text)).ToArray(), [], [])).ToArray();
    }
}
