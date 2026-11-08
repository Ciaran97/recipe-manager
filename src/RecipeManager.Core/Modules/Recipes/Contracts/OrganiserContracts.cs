namespace RecipeManager.Core.Modules.Recipes.Contracts;

public sealed record RecipeOrganiserIds(IReadOnlyList<Guid> CollectionIds, IReadOnlyList<Guid> TagIds);

// US6 supplies the organiser owner; recipe writes/duplication use these same transaction-scoped ports.
public interface IRecipeOrganisers
{
    Task ValidateAsync(Guid householdId, IReadOnlyList<Guid> collectionIds, IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken);
    Task ReplaceAsync(Guid householdId, Guid recipeId, IReadOnlyList<Guid> collectionIds, IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, RecipeOrganiserIds>> ReadManyAsync(Guid householdId, IReadOnlyList<Guid> recipeIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> FilterAsync(Guid householdId, IReadOnlyList<Guid> collectionIds, IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> SearchTagsAsync(Guid householdId, string query, CancellationToken cancellationToken);
}
