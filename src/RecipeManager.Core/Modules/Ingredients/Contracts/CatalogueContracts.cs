namespace RecipeManager.Core.Modules.Ingredients.Contracts;

public sealed record IngredientAliasRead(Guid Id, string Name);
public sealed record IngredientRead(Guid Id, string CanonicalName, Guid? CategoryId, string? CategoryName, long Revision, IReadOnlyList<IngredientAliasRead> Aliases);
public sealed record CategoryRead(Guid Id, string Name, int SortOrder, long Revision);
public sealed record CataloguePage<T>(IReadOnlyList<T> Items, Guid? NextId);

// Referencing modules implement these queries/effects within the caller's household transaction.
public interface IIngredientReferences
{
    Task<bool> UsesIngredientAsync(Guid householdId, Guid ingredientId, CancellationToken cancellationToken);
    Task IngredientChangedAsync(Guid householdId, Guid ingredientId, CancellationToken cancellationToken);
}
