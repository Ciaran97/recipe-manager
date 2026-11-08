using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Recipes;

public sealed class RecipeOrganiserStore(RecipeManagerDbContext database, TransactionBoundary transactions,
    RevisionCoordinator revisions, CreationRetryStore retries) : IRecipeOrganiserStore, IRecipeOrganisers
{
    public Task<OrganiserPage> ListAsync(RequestActor actor, OrganiserKind kind, string query, Guid? cursor, int limit, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async cancellation =>
        {
            var pattern = "%" + Escape(query.Trim()) + "%";
            // Select each concrete entity before projecting; EF needs concrete mapped properties.
            var rows = kind == OrganiserKind.Collection
                ? await database.Collections.AsNoTracking().Where(x => (cursor == null || x.Id.CompareTo(cursor.Value) > 0) && EF.Functions.ILike(x.Name, pattern, "\\")).OrderBy(x => x.Id).Take(limit + 1).Select(x => new OrganiserRead(x.Id, x.Name, x.Revision)).ToArrayAsync(cancellation)
                : await database.Tags.AsNoTracking().Where(x => (cursor == null || x.Id.CompareTo(cursor.Value) > 0) && EF.Functions.ILike(x.Name, pattern, "\\")).OrderBy(x => x.Id).Take(limit + 1).Select(x => new OrganiserRead(x.Id, x.Name, x.Revision)).ToArrayAsync(cancellation);
            var page = rows.Take(limit).ToArray(); return new OrganiserPage(page, rows.Length > limit ? page[^1].Id : null);
        }, ct);
    public Task<OrganiserRead> ReadAsync(RequestActor actor, OrganiserKind kind, Guid id, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async cancellation => Read(await Required(kind, id, cancellation)), ct);
    public Task<OrganiserRead> CreateAsync(RequestActor actor, OrganiserKind kind, string name, Guid? retryKey, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async cancellation =>
        {
            async Task<OrganiserRead> Create(CancellationToken token)
            {
                await Available(kind, name, null, token);
                IRecipeOrganiser entity = kind == OrganiserKind.Collection ? new Collection(Guid.NewGuid(), actor.HouseholdId, name) : new Tag(Guid.NewGuid(), actor.HouseholdId, name);
                database.Add(entity); await Changed(actor, token); await database.SaveChangesAsync(token); return Read(entity);
            }
            if (retryKey is null) return await Create(cancellation);
            var result = await retries.ExecuteAsync(actor.HouseholdId, actor.UserId, "/api/v1/" + Route(kind), retryKey.Value,
                JsonSerializer.SerializeToUtf8Bytes(new { name }), async token => new CreationOutcome(201, JsonSerializer.Serialize(await Create(token)), null, null), cancellation);
            return JsonSerializer.Deserialize<OrganiserRead>(result.Body) ?? throw new InvalidOperationException("Retry result unavailable.");
        }, ct);
    public Task<OrganiserRead> UpdateAsync(RequestActor actor, OrganiserKind kind, Guid id, long revision, string name, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async cancellation =>
        {
            var entity = await Required(kind, id, cancellation); Match(entity, revision); await Available(kind, name, id, cancellation);
            entity.Rename(name); await AdvanceReferences(kind, id, cancellation); await Changed(actor, cancellation); return Read(entity);
        }, ct);
    public Task DeleteAsync(RequestActor actor, OrganiserKind kind, Guid id, long revision, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async cancellation =>
        {
            var entity = await Required(kind, id, cancellation); Match(entity, revision);
            await AdvanceReferences(kind, id, cancellation); database.Remove(entity); await Changed(actor, cancellation); return true;
        }, ct);
    private async Task AdvanceReferences(OrganiserKind kind, Guid id, CancellationToken ct)
    {
        var ids = kind == OrganiserKind.Collection ? database.RecipeCollections.Where(x => x.CollectionId == id).Select(x => x.RecipeId)
            : database.RecipeTags.Where(x => x.TagId == id).Select(x => x.RecipeId);
        foreach (var recipe in await database.Recipes.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(ct)) recipe.Advance();
    }
    private async Task<IRecipeOrganiser> Required(OrganiserKind kind, Guid id, CancellationToken ct)
    {
        IRecipeOrganiser? entity = kind == OrganiserKind.Collection ? await database.Collections.SingleOrDefaultAsync(x => x.Id == id, ct)
            : await database.Tags.SingleOrDefaultAsync(x => x.Id == id, ct);
        return entity ?? throw new ResourceMissingException();
    }
    private async Task Available(OrganiserKind kind, string name, Guid? except, CancellationToken ct)
    {
        var normalized = CatalogueNames.Normalize(name);
        var exists = kind == OrganiserKind.Collection ? await database.Collections.AnyAsync(x => x.Id != except && x.NormalizedName == normalized, ct)
            : await database.Tags.AnyAsync(x => x.Id != except && x.NormalizedName == normalized, ct);
        if (exists) throw new RecipeConflictException("name_conflict");
    }
    public async Task ValidateAsync(Guid householdId, IReadOnlyList<Guid> collectionIds, IReadOnlyList<Guid> tagIds, CancellationToken ct)
    {
        var collections = collectionIds.Distinct().ToArray(); var tags = tagIds.Distinct().ToArray();
        if (await database.Collections.CountAsync(x => x.HouseholdId == householdId && collections.Contains(x.Id), ct) != collections.Length
            || await database.Tags.CountAsync(x => x.HouseholdId == householdId && tags.Contains(x.Id), ct) != tags.Length) throw new ResourceMissingException();
    }
    public async Task ReplaceAsync(Guid householdId, Guid recipeId, IReadOnlyList<Guid> collectionIds, IReadOnlyList<Guid> tagIds, CancellationToken ct)
    {
        await ValidateAsync(householdId, collectionIds, tagIds, ct);
        var existingCollections = await database.RecipeCollections.Where(x => x.HouseholdId == householdId && x.RecipeId == recipeId).ToArrayAsync(ct);
        var existingTags = await database.RecipeTags.Where(x => x.HouseholdId == householdId && x.RecipeId == recipeId).ToArrayAsync(ct);
        database.RemoveRange(existingCollections.Where(x => !collectionIds.Contains(x.CollectionId)));
        database.RemoveRange(existingTags.Where(x => !tagIds.Contains(x.TagId)));
        database.AddRange(collectionIds.Distinct().Except(existingCollections.Select(x => x.CollectionId)).Select(id => new RecipeCollection(householdId, recipeId, id)));
        database.AddRange(tagIds.Distinct().Except(existingTags.Select(x => x.TagId)).Select(id => new RecipeTag(householdId, recipeId, id)));
    }
    public async Task<IReadOnlyDictionary<Guid, RecipeOrganiserIds>> ReadManyAsync(Guid householdId, IReadOnlyList<Guid> recipeIds, CancellationToken ct)
    {
        var collections = await database.RecipeCollections.AsNoTracking().Where(x => x.HouseholdId == householdId && recipeIds.Contains(x.RecipeId)).OrderBy(x => x.CollectionId).ToArrayAsync(ct);
        var tags = await database.RecipeTags.AsNoTracking().Where(x => x.HouseholdId == householdId && recipeIds.Contains(x.RecipeId)).OrderBy(x => x.TagId).ToArrayAsync(ct);
        return recipeIds.ToDictionary(id => id, id => new RecipeOrganiserIds(collections.Where(x => x.RecipeId == id).Select(x => x.CollectionId).ToArray(), tags.Where(x => x.RecipeId == id).Select(x => x.TagId).ToArray()));
    }
    public async Task<IReadOnlyList<Guid>> FilterAsync(Guid householdId, IReadOnlyList<Guid> collectionIds, IReadOnlyList<Guid> tagIds, CancellationToken ct)
    {
        return await database.Recipes.Where(x => x.HouseholdId == householdId
            && (collectionIds.Count == 0 || database.RecipeCollections.Any(link => link.HouseholdId == householdId && link.RecipeId == x.Id && collectionIds.Contains(link.CollectionId)))
            && (tagIds.Count == 0 || database.RecipeTags.Any(link => link.HouseholdId == householdId && link.RecipeId == x.Id && tagIds.Contains(link.TagId)))).Select(x => x.Id).ToArrayAsync(ct);
    }
    public async Task<IReadOnlyList<Guid>> SearchTagsAsync(Guid householdId, string query, CancellationToken ct)
    {
        var pattern = "%" + Escape(query.Trim()) + "%";
        return await database.RecipeTags.Where(link => link.HouseholdId == householdId && database.Tags.Any(tag => tag.HouseholdId == householdId && tag.Id == link.TagId && EF.Functions.ILike(tag.Name, pattern, "\\")))
            .Select(x => x.RecipeId).Distinct().ToArrayAsync(ct);
    }
    private Task Changed(RequestActor actor, CancellationToken ct) => revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Recipes, true, ct);
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    private static void Match(IRecipeOrganiser entity, long revision) { if (entity.Revision != revision) throw new RevisionConflictException(); }
    private static OrganiserRead Read(IRecipeOrganiser entity) => new(entity.Id, entity.Name, entity.Revision);
    internal static string Route(OrganiserKind kind) => kind == OrganiserKind.Collection ? "collections" : "tags";
}
