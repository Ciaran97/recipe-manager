using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Ingredients;

public interface ICatalogueStore
{
    Task<CataloguePage<IngredientRead>> IngredientsAsync(RequestActor actor, string query, Guid? cursor, int limit, CancellationToken cancellationToken);
    Task<IngredientRead> IngredientAsync(RequestActor actor, Guid id, CancellationToken cancellationToken);
    Task<IngredientRead> CreateAsync(RequestActor actor, string name, Guid? categoryId, Guid? retryKey, CancellationToken cancellationToken);
    Task<IngredientRead> UpdateAsync(RequestActor actor, Guid id, long revision, string name, Guid? categoryId, bool retainOldNameAsAlias, CancellationToken cancellationToken);
    Task DeleteAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken);
    Task<IngredientRead> AliasAsync(RequestActor actor, Guid id, long revision, Guid? aliasId, string? name, bool remove, Guid? retryKey, CancellationToken cancellationToken);
    Task<CataloguePage<CategoryRead>> CategoriesAsync(RequestActor actor, Guid? cursor, int limit, CancellationToken cancellationToken);
    Task<CategoryRead> CategoryAsync(RequestActor actor, Guid id, CancellationToken cancellationToken);
    Task<CategoryRead> CreateCategoryAsync(RequestActor actor, string name, int sortOrder, Guid? retryKey, CancellationToken cancellationToken);
    Task<CategoryRead> UpdateCategoryAsync(RequestActor actor, Guid id, long revision, string name, int sortOrder, CancellationToken cancellationToken);
    Task DeleteCategoryAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken);
}

public sealed class CatalogueService(ICatalogueStore store)
{
    public Task<CataloguePage<IngredientRead>> IngredientsAsync(RequestActor actor, string? query, Guid? cursor, int limit, CancellationToken cancellationToken = default)
        => store.IngredientsAsync(actor, string.IsNullOrWhiteSpace(query) ? "" : CatalogueNames.Normalize(query), cursor, limit, cancellationToken);
    public Task<IngredientRead> IngredientAsync(RequestActor actor, Guid id, CancellationToken cancellationToken = default)
        => store.IngredientAsync(actor, DomainRules.NonEmptyId(id, "id"), cancellationToken);
    public Task<IngredientRead> CreateAsync(RequestActor actor, string canonicalName, Guid? categoryId, Guid? retryKey, CancellationToken cancellationToken = default)
    {
        _ = DomainRules.Name(canonicalName, "canonicalName");
        return store.CreateAsync(actor, canonicalName, CategoryId(categoryId), retryKey, cancellationToken);
    }
    public Task<IngredientRead> UpdateAsync(RequestActor actor, Guid id, long revision, string canonicalName, Guid? categoryId, bool retainOldNameAsAlias, CancellationToken cancellationToken = default)
        => store.UpdateAsync(actor, DomainRules.NonEmptyId(id, "id"), DomainRules.Revision(revision), DomainRules.Name(canonicalName, "canonicalName"), CategoryId(categoryId), retainOldNameAsAlias, cancellationToken);
    public Task DeleteAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken = default)
        => store.DeleteAsync(actor, DomainRules.NonEmptyId(id, "id"), DomainRules.Revision(revision), cancellationToken);
    public Task<IngredientRead> AddAliasAsync(RequestActor actor, Guid id, long revision, string name, Guid? retryKey = null, CancellationToken cancellationToken = default)
    {
        _ = DomainRules.Name(name);
        return store.AliasAsync(actor, DomainRules.NonEmptyId(id, "id"), DomainRules.Revision(revision), null, name, false, retryKey, cancellationToken);
    }
    public Task<IngredientRead> UpdateAliasAsync(RequestActor actor, Guid id, long revision, Guid aliasId, string name, CancellationToken cancellationToken = default)
        => store.AliasAsync(actor, DomainRules.NonEmptyId(id, "id"), DomainRules.Revision(revision), DomainRules.NonEmptyId(aliasId, "aliasId"), DomainRules.Name(name), false, null, cancellationToken);
    public Task<IngredientRead> RemoveAliasAsync(RequestActor actor, Guid id, long revision, Guid aliasId, CancellationToken cancellationToken = default)
        => store.AliasAsync(actor, DomainRules.NonEmptyId(id, "id"), DomainRules.Revision(revision), DomainRules.NonEmptyId(aliasId, "aliasId"), null, true, null, cancellationToken);
    public Task<CataloguePage<CategoryRead>> CategoriesAsync(RequestActor actor, Guid? cursor, int limit, CancellationToken cancellationToken = default)
        => store.CategoriesAsync(actor, cursor, limit, cancellationToken);
    public Task<CategoryRead> CategoryAsync(RequestActor actor, Guid id, CancellationToken cancellationToken = default)
        => store.CategoryAsync(actor, DomainRules.NonEmptyId(id, "id"), cancellationToken);
    public Task<CategoryRead> CreateCategoryAsync(RequestActor actor, string name, int sortOrder, Guid? retryKey, CancellationToken cancellationToken = default)
    {
        var validated = new IngredientCategory(Guid.NewGuid(), actor.HouseholdId, name, sortOrder);
        return store.CreateCategoryAsync(actor, name, validated.SortOrder, retryKey, cancellationToken);
    }
    public Task<CategoryRead> UpdateCategoryAsync(RequestActor actor, Guid id, long revision, string name, int sortOrder, CancellationToken cancellationToken = default)
    {
        var validated = new IngredientCategory(id, actor.HouseholdId, name, sortOrder);
        return store.UpdateCategoryAsync(actor, validated.Id, DomainRules.Revision(revision), validated.Name, validated.SortOrder, cancellationToken);
    }
    public Task DeleteCategoryAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken = default)
        => store.DeleteCategoryAsync(actor, DomainRules.NonEmptyId(id, "id"), DomainRules.Revision(revision), cancellationToken);
    private static Guid? CategoryId(Guid? id) => id is { } value ? DomainRules.NonEmptyId(value, "categoryId") : null;
}
