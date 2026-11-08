using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Recipes;

public sealed record RecipeVersion(Guid UserId, long Revision, long PersonalRevision);
public sealed record RecipeResult(RecipeRead Recipe, long PersonalRevision);
public sealed record RecipeListResult(RecipePage Page, long HouseholdRecipesRevision, long PersonalRevision);
public sealed record RecipeQuery(string Query, string Archived, bool FavouriteOnly, IReadOnlyList<Guid> CollectionIds, IReadOnlyList<Guid> TagIds, Guid? Cursor, int Limit);
public interface IRecipeStore
{
    Task<RecipeListResult> ListAsync(RequestActor actor, RecipeQuery query, CancellationToken cancellationToken);
    Task<RecipeResult> ReadAsync(RequestActor actor, Guid id, CancellationToken cancellationToken);
    Task<RecipeResult> CreateAsync(RequestActor actor, RecipeWrite request, Guid? retryKey, CancellationToken cancellationToken);
    Task<RecipeResult> UpdateAsync(RequestActor actor, Guid id, RecipeVersion expected, RecipeWrite request, CancellationToken cancellationToken);
    Task<RecipeResult> DuplicateAsync(RequestActor actor, Guid id, RecipeVersion expected, Guid? retryKey, CancellationToken cancellationToken);
    Task<RecipeResult> ArchiveAsync(RequestActor actor, Guid id, RecipeVersion expected, bool archived, CancellationToken cancellationToken);
    Task<RecipeResult> FavouriteAsync(RequestActor actor, Guid id, RecipeVersion expected, bool favourite, CancellationToken cancellationToken);
    Task DeleteAsync(RequestActor actor, Guid id, RecipeVersion expected, CancellationToken cancellationToken);
    Task<RecipeServingPreview> PreviewAsync(RequestActor actor, Guid id, decimal targetServings, CancellationToken cancellationToken);
}
public sealed class RecipeService(IRecipeStore store)
{
    public Task<RecipeListResult> ListAsync(RequestActor actor, RecipeQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Query.Length > 200 || query.Archived is not ("exclude" or "include" or "only") || query.Limit is < 1 or > 100
            || query.CollectionIds.Count > 100 || query.TagIds.Count > 100)
            throw new DomainValidationException("query", "invalid", "Use supported bounded search, filters and pagination.");
        foreach (var id in query.CollectionIds.Concat(query.TagIds)) DomainRules.NonEmptyId(id, "filterId");
        return store.ListAsync(actor, query, cancellationToken);
    }
    public Task<RecipeResult> ReadAsync(RequestActor actor, Guid id, CancellationToken cancellationToken = default)
        => store.ReadAsync(actor, DomainRules.NonEmptyId(id, "recipeId"), cancellationToken);
    public Task<RecipeResult> CreateAsync(RequestActor actor, RecipeWrite request, Guid? retryKey, CancellationToken cancellationToken = default)
    {
        _ = RecipeRules.Validate(request);
        return store.CreateAsync(actor, request, retryKey, cancellationToken);
    }
    public Task<RecipeResult> UpdateAsync(RequestActor actor, Guid id, RecipeVersion expected, RecipeWrite request, CancellationToken cancellationToken = default)
    {
        _ = RecipeRules.Validate(request);
        return store.UpdateAsync(actor, DomainRules.NonEmptyId(id, "recipeId"), Version(expected), request, cancellationToken);
    }
    public Task<RecipeResult> DuplicateAsync(RequestActor actor, Guid id, RecipeVersion expected, Guid? retryKey, CancellationToken cancellationToken = default)
        => store.DuplicateAsync(actor, DomainRules.NonEmptyId(id, "recipeId"), Version(expected), retryKey, cancellationToken);
    public Task<RecipeResult> ArchiveAsync(RequestActor actor, Guid id, RecipeVersion expected, bool archived, CancellationToken cancellationToken = default)
        => store.ArchiveAsync(actor, DomainRules.NonEmptyId(id, "recipeId"), Version(expected), archived, cancellationToken);
    public Task<RecipeResult> FavouriteAsync(RequestActor actor, Guid id, RecipeVersion expected, bool favourite, CancellationToken cancellationToken = default)
        => store.FavouriteAsync(actor, DomainRules.NonEmptyId(id, "recipeId"), Version(expected), favourite, cancellationToken);
    public Task DeleteAsync(RequestActor actor, Guid id, RecipeVersion expected, CancellationToken cancellationToken = default)
        => store.DeleteAsync(actor, DomainRules.NonEmptyId(id, "recipeId"), Version(expected), cancellationToken);
    private static RecipeVersion Version(RecipeVersion value) => new(DomainRules.NonEmptyId(value.UserId, "userId"), DomainRules.Revision(value.Revision), DomainRules.Revision(value.PersonalRevision));
    public Task<RecipeServingPreview> PreviewAsync(RequestActor actor, Guid id, decimal targetServings, CancellationToken cancellationToken = default)
        => store.PreviewAsync(actor, DomainRules.NonEmptyId(id, "recipeId"), PortionQuantity.Validate(targetServings), cancellationToken);
}
public sealed class RecipeConflictException(string code) : Exception { public string Code { get; } = code; }
