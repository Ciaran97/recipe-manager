using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Ingredients;

public sealed class CatalogueStore(RecipeManagerDbContext database, TransactionBoundary transactions, RevisionCoordinator revisions,
    CreationRetryStore retries, IEnumerable<IIngredientReferences> references) : ICatalogueStore
{
    public Task<CataloguePage<IngredientRead>> IngredientsAsync(RequestActor actor, string query, Guid? cursor, int limit, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var pattern = "%" + query.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var rows = await database.Ingredients.AsNoTracking().Where(x => (cursor == null || x.Id.CompareTo(cursor.Value) > 0)
                    && (query == "" || database.IngredientNames.Any(n => n.IngredientId == x.Id && EF.Functions.ILike(n.NormalizedName, pattern, "\\"))))
                .OrderBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
            var page = rows.Take(limit).ToArray();
            var ids = page.Select(x => x.Id).ToArray();
            var aliases = await database.IngredientAliases.AsNoTracking().Where(x => ids.Contains(x.IngredientId)).OrderBy(x => x.Name).ToListAsync(ct);
            var categoryIds = page.Where(x => x.CategoryId != null).Select(x => x.CategoryId!.Value).Distinct().ToArray();
            var categories = await database.IngredientCategories.AsNoTracking().Where(x => categoryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var items = page.Select(x => Read(x, aliases.Where(a => a.IngredientId == x.Id), x.CategoryId is { } id ? categories.GetValueOrDefault(id) : null)).ToArray();
            return new CataloguePage<IngredientRead>(items, rows.Count > limit ? page[^1].Id : null);
        }, cancellationToken);
    public Task<IngredientRead> IngredientAsync(RequestActor actor, Guid id, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct => await ReadAsync(await Required(id, ct), ct), cancellationToken);
    public Task<IngredientRead> CreateAsync(RequestActor actor, string name, Guid? categoryId, Guid? retryKey, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], ct => Retry(actor, "/api/v1/ingredients", retryKey, new { canonicalName = name, categoryId }, async cancellation =>
        {
            await CategoryExists(categoryId, cancellation);
            await AvailableName(CatalogueNames.Normalize(name), cancellation);
            var ingredient = new Ingredient(Guid.NewGuid(), actor.HouseholdId, name, categoryId);
            database.AddRange(ingredient, new IngredientName(actor.HouseholdId, ingredient.Id, name, true));
            await Changed(actor, cancellation);
            await database.SaveChangesAsync(cancellation);
            return await ReadAsync(ingredient, cancellation);
        }, ct), cancellationToken);
    public Task<IngredientRead> UpdateAsync(RequestActor actor, Guid id, long revision, string name, Guid? categoryId, bool retainOldNameAsAlias, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [IngredientLock(actor, id)], async ct =>
        {
            var ingredient = await Required(id, ct); Match(ingredient.Revision, revision);
            await CategoryExists(categoryId, ct);
            var normalized = CatalogueNames.Normalize(name);
            var priorName = ingredient.CanonicalName;
            var contentChanged = priorName != name || ingredient.CategoryId != categoryId;
            if (normalized != ingredient.NormalizedName)
            {
                var target = await database.IngredientNames.SingleOrDefaultAsync(x => x.NormalizedName == normalized, ct);
                if (target is not null && target.IngredientId != id) throw new CatalogueConflictException("name_conflict");
                if (target is not null)
                {
                    var promoted = await database.IngredientAliases.SingleAsync(x => x.IngredientId == id && x.NormalizedName == normalized, ct);
                    database.IngredientAliases.Remove(promoted); database.IngredientNames.Remove(target);
                }
                database.IngredientNames.Remove(await database.IngredientNames.SingleAsync(x => x.IngredientId == id && x.IsCanonical, ct));
                // Clear old keys before promoting/reusing names; all changes remain in this transaction.
                await database.SaveChangesAsync(ct);
                database.IngredientNames.Add(new IngredientName(actor.HouseholdId, id, name, true));
                if (retainOldNameAsAlias)
                    database.AddRange(new IngredientAlias(Guid.NewGuid(), actor.HouseholdId, id, priorName), new IngredientName(actor.HouseholdId, id, priorName, false));
            }
            ingredient.Update(name, categoryId);
            if (contentChanged) await ReferencingContentChanged(actor.HouseholdId, id, ct);
            await Changed(actor, ct); await database.SaveChangesAsync(ct);
            return await ReadAsync(ingredient, ct);
        }, cancellationToken);
    public Task DeleteAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [IngredientLock(actor, id)], async ct =>
        {
            var ingredient = await Required(id, ct); Match(ingredient.Revision, revision);
            foreach (var reference in references)
                if (await reference.UsesIngredientAsync(actor.HouseholdId, id, ct)) throw new CatalogueConflictException("ingredient_referenced");
            database.Ingredients.Remove(ingredient); await Changed(actor, ct); return true;
        }, cancellationToken);
    public Task<IngredientRead> AliasAsync(RequestActor actor, Guid id, long revision, Guid? aliasId, string? name, bool remove, Guid? retryKey, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [IngredientLock(actor, id)], cancellation => Retry(actor, $"/api/v1/ingredients/{id}/aliases", retryKey, new { name, revision }, async ct =>
        {
            var ingredient = await Required(id, ct); Match(ingredient.Revision, revision);
            var alias = aliasId is { } value ? await database.IngredientAliases.SingleOrDefaultAsync(x => x.Id == value && x.IngredientId == id, ct) ?? throw new ResourceMissingException() : null;
            if (remove)
            {
                if (alias is null) throw new ResourceMissingException();
                database.IngredientAliases.Remove(alias);
                database.IngredientNames.Remove(await database.IngredientNames.SingleAsync(x => x.NormalizedName == alias.NormalizedName, ct));
            }
            else
            {
                var normalized = CatalogueNames.Normalize(name!);
                if (alias is null || alias.NormalizedName != normalized)
                {
                    await AvailableName(normalized, ct);
                    if (alias is not null)
                        database.IngredientNames.Remove(await database.IngredientNames.SingleAsync(x => x.NormalizedName == alias.NormalizedName, ct));
                    database.IngredientNames.Add(new IngredientName(actor.HouseholdId, id, name!, false));
                }
                if (alias is null) database.IngredientAliases.Add(new IngredientAlias(Guid.NewGuid(), actor.HouseholdId, id, name!));
                else alias.Rename(name!);
            }
            ingredient.Advance(); await Changed(actor, ct); await database.SaveChangesAsync(ct);
            return await ReadAsync(ingredient, ct);
        }, cancellation, status: 200), cancellationToken);
    public Task<CataloguePage<CategoryRead>> CategoriesAsync(RequestActor actor, Guid? cursor, int limit, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var rows = await database.IngredientCategories.AsNoTracking().Where(x => cursor == null || x.Id.CompareTo(cursor.Value) > 0)
                .OrderBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
            var items = rows.Take(limit).Select(CategoryRead).ToArray();
            return new CataloguePage<CategoryRead>(items, rows.Count > limit ? items[^1].Id : null);
        }, cancellationToken);
    public Task<CategoryRead> CategoryAsync(RequestActor actor, Guid id, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct => CategoryRead(await RequiredCategory(id, ct)), cancellationToken);
    public Task<CategoryRead> CreateCategoryAsync(RequestActor actor, string name, int sortOrder, Guid? retryKey, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], ct => Retry(actor, "/api/v1/ingredient-categories", retryKey, new { name, sortOrder }, async cancellation =>
        {
            if (await database.IngredientCategories.AnyAsync(x => x.NormalizedName == CatalogueNames.Normalize(name), cancellation)) throw new CatalogueConflictException("name_conflict");
            var category = new IngredientCategory(Guid.NewGuid(), actor.HouseholdId, name, sortOrder);
            database.IngredientCategories.Add(category); await Changed(actor, cancellation); return CategoryRead(category);
        }, ct), cancellationToken);
    public Task<CategoryRead> UpdateCategoryAsync(RequestActor actor, Guid id, long revision, string name, int sortOrder, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var category = await RequiredCategory(id, ct); Match(category.Revision, revision);
            if (await database.IngredientCategories.AnyAsync(x => x.Id != id && x.NormalizedName == CatalogueNames.Normalize(name), ct)) throw new CatalogueConflictException("name_conflict");
            category.Update(name, sortOrder);
            foreach (var ingredient in await database.Ingredients.Where(x => x.CategoryId == id).OrderBy(x => x.Id).ToListAsync(ct))
            {
                ingredient.Advance(); await ReferencingContentChanged(actor.HouseholdId, ingredient.Id, ct);
            }
            await Changed(actor, ct); return CategoryRead(category);
        }, cancellationToken);
    public Task DeleteCategoryAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var category = await RequiredCategory(id, ct); Match(category.Revision, revision);
            foreach (var ingredient in await database.Ingredients.Where(x => x.CategoryId == id).OrderBy(x => x.Id).ToListAsync(ct))
            {
                ingredient.Update(ingredient.CanonicalName, null); await ReferencingContentChanged(actor.HouseholdId, ingredient.Id, ct);
            }
            database.IngredientCategories.Remove(category); await Changed(actor, ct); return true;
        }, cancellationToken);

    private async Task<Ingredient> Required(Guid id, CancellationToken ct)
        => await database.Ingredients.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissingException();
    private async Task<IngredientCategory> RequiredCategory(Guid id, CancellationToken ct)
        => await database.IngredientCategories.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissingException();
    private async Task CategoryExists(Guid? id, CancellationToken ct)
    {
        if (id is { } value && !await database.IngredientCategories.AnyAsync(x => x.Id == value, ct)) throw new ResourceMissingException();
    }
    private async Task AvailableName(string normalized, CancellationToken ct)
    {
        if (await database.IngredientNames.AnyAsync(x => x.NormalizedName == normalized, ct)) throw new CatalogueConflictException("name_conflict");
    }
    private Task Changed(RequestActor actor, CancellationToken ct) => revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Recipes, true, ct);
    private async Task ReferencingContentChanged(Guid householdId, Guid ingredientId, CancellationToken ct)
    {
        foreach (var reference in references) await reference.IngredientChangedAsync(householdId, ingredientId, ct);
    }
    private async Task<IngredientRead> ReadAsync(Ingredient ingredient, CancellationToken ct)
    {
        var aliases = await database.IngredientAliases.AsNoTracking().Where(x => x.IngredientId == ingredient.Id).OrderBy(x => x.Name).ToListAsync(ct);
        var category = ingredient.CategoryId is { } id ? await database.IngredientCategories.Where(x => x.Id == id).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
        return Read(ingredient, aliases, category);
    }
    private static IngredientRead Read(Ingredient ingredient, IEnumerable<IngredientAlias> aliases, string? category)
        => new(ingredient.Id, ingredient.CanonicalName, ingredient.CategoryId, category, ingredient.Revision, aliases.Select(x => new IngredientAliasRead(x.Id, x.Name)).ToArray());
    private static CategoryRead CategoryRead(IngredientCategory category) => new(category.Id, category.Name, category.SortOrder, category.Revision);
    private static void Match(long current, long expected) { if (current != expected) throw new RevisionConflictException(); }
    private AggregateLock IngredientLock(RequestActor actor, Guid id) => new("ingredient", id, async ct =>
    {
        _ = await database.Ingredients.FromSqlInterpolated($"SELECT * FROM ingredients.\"Ingredients\" WHERE \"HouseholdId\" = {actor.HouseholdId} AND \"Id\" = {id} FOR UPDATE").AsNoTracking().ToListAsync(ct);
    });
    private async Task<T> Retry<T>(RequestActor actor, string route, Guid? key, object payload, Func<CancellationToken, Task<T>> create, CancellationToken ct, int status = 201)
    {
        if (key is null) return await create(ct);
        var result = await retries.ExecuteAsync(actor.HouseholdId, actor.UserId, route, key.Value, JsonSerializer.SerializeToUtf8Bytes(payload), async cancellation =>
            new CreationOutcome(status, JsonSerializer.Serialize(await create(cancellation)), null, null), ct);
        return JsonSerializer.Deserialize<T>(result.Body) ?? throw new InvalidOperationException("Retry result unavailable.");
    }
}
