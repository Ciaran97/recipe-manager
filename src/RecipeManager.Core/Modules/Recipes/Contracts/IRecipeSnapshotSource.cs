namespace RecipeManager.Core.Modules.Recipes.Contracts;

// Called only inside the existing authenticated household transaction; never starts a nested one.
public interface IRecipeSnapshotSource
{
    Task<RecipeRead> ReadWithinTransactionAsync(Guid householdId, Guid recipeId, CancellationToken cancellationToken);
}
