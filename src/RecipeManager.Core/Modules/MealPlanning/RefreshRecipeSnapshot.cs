using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.MealPlanning;

public static class RefreshRecipeSnapshot
{
    public static SnapshotRefreshPreview Preview(CookingEvent cooking, RecipeRead latest)
    {
        SameRecipe(cooking, latest);
        return new(cooking.Revision, latest.Revision, RecipeSnapshotFactory.Deserialize(cooking.RecipeSnapshotJson), RecipeSnapshotFactory.Create(latest));
    }
    /// <summary>The repository calls this with current rows under its household/source transaction.</summary>
    public static void Apply(CookingEvent cooking, RecipeRead latest, long expectedEventRevision, long expectedRecipeRevision)
    {
        SameRecipe(cooking, latest);
        if (cooking.Revision != expectedEventRevision || latest.Revision != expectedRecipeRevision)
            throw new RecipeSnapshotConflictException();
        var snapshot = RecipeSnapshotFactory.Create(latest);
        cooking.ReplaceRecipe(latest.Id, latest.Revision, RecipeSnapshotFactory.Serialize(snapshot));
    }
    private static void SameRecipe(CookingEvent cooking, RecipeRead latest)
    {
        if (cooking.RecipeId != latest.Id)
            throw new DomainValidationException("recipeId", "different_source", "Refresh must use the cooking event's linked recipe.");
    }
}
public sealed class RecipeSnapshotConflictException : InvalidOperationException;
