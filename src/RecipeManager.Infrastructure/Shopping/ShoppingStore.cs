using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Shopping;

public sealed partial class ShoppingStore(RecipeManagerDbContext database, TransactionBoundary transactions,
    RevisionCoordinator revisions, CreationRetryStore retries, IRecipeSnapshotSource recipes) : IShoppingStore
{
    private Task<T> Within<T>(RequestActor actor, Guid? listId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        AggregateLock[] locks = listId is { } id ? [new("shopping-list", id, async token =>
        {
            _ = await database.ShoppingLists.FromSqlInterpolated($"SELECT * FROM shopping.\"ShoppingLists\" WHERE \"HouseholdId\" = {actor.HouseholdId} AND \"Id\" = {id} FOR UPDATE").AsNoTracking().ToListAsync(token);
        })] : [];
        return transactions.ExecuteAsync(actor, false, locks, action, ct);
    }
    private async Task<ShoppingList> List(RequestActor actor, Guid id, CancellationToken ct)
        => await database.ShoppingLists.SingleOrDefaultAsync(x => x.HouseholdId == actor.HouseholdId && x.Id == id, ct) ?? throw new ResourceMissingException();
    private Task<ShoppingItem[]> Items(RequestActor actor, Guid listId, CancellationToken ct)
        => database.ShoppingItems.Where(x => x.HouseholdId == actor.HouseholdId && x.ListId == listId).ToArrayAsync(ct);
    private async Task<ShoppingListRead> Read(RequestActor actor, Guid id, CancellationToken ct)
        => ShoppingViews.Read(await List(actor, id, ct), await Items(actor, id, ct));
    private static void Match(long actual, long expected) { if (actual != expected) throw new RevisionConflictException(); }
    private async Task Finish(RequestActor actor, CancellationToken ct, ShoppingList list, bool deleted = false)
    {
        await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Shopping, false, ct);
        revisions.ShoppingListChanged(actor.HouseholdId, list.Id, list.Revision, deleted);
        await database.SaveChangesAsync(ct);
    }
    private async Task<ShoppingListRead> Retry(RequestActor actor, string route, Guid? key, object payload, Func<CancellationToken, Task<ShoppingListRead>> create, CancellationToken ct)
    {
        if (key is null) return await create(ct);
        var outcome = await retries.ExecuteAsync(actor.HouseholdId, actor.UserId, route, key.Value, JsonSerializer.SerializeToUtf8Bytes(payload),
            async token => new CreationOutcome(201, JsonSerializer.Serialize(await create(token)), null, null), ct);
        return JsonSerializer.Deserialize<ShoppingListRead>(outcome.Body) ?? throw new InvalidOperationException("Shopping creation retry result is missing.");
    }
    public Task<ShoppingListRead> ReadAsync(RequestActor actor, Guid id, CancellationToken ct) => Within(actor, null, token => Read(actor, id, token), ct);
    public Task<ShoppingListPage> ListAsync(RequestActor actor, Guid? cursor, int limit, CancellationToken ct) => Within(actor, null, async token =>
    {
        if (limit is < 1 or > 100) throw new DomainValidationException("limit", "range", "Choose between 1 and 100 lists per page.");
        var page = await database.ShoppingLists.AsNoTracking().Where(x => x.HouseholdId == actor.HouseholdId && (cursor == null || x.Id.CompareTo(cursor.Value) > 0))
            .OrderBy(x => x.Id).Take(limit + 1).ToArrayAsync(token);
        var lists = page.Take(limit).ToArray(); var ids = lists.Select(x => x.Id).ToArray();
        var items = await database.ShoppingItems.AsNoTracking().Where(x => x.HouseholdId == actor.HouseholdId && ids.Contains(x.ListId)).ToArrayAsync(token);
        var result = lists.Select(list =>
        {
            var read = ShoppingViews.Read(list, items.Where(x => x.ListId == list.Id).ToArray());
            return new ShoppingListBrief(read.Id, read.Revision, read.Name, read.Basis, read.Summary);
        }).ToArray();
        var household = await database.Households.SingleAsync(x => x.Id == actor.HouseholdId, token);
        return new ShoppingListPage(result, page.Length > limit ? lists[^1].Id : null, household.ShoppingRevision);
    }, ct);
    public Task<ShoppingListRead> CreateAsync(RequestActor actor, ShoppingListWrite request, Guid? retryKey, CancellationToken ct)
        => Within(actor, null, token => Retry(actor, "/api/v1/shopping-lists", retryKey, request, async cancellation =>
        {
            var household = await database.Households.SingleAsync(x => x.Id == actor.HouseholdId, cancellation);
            var list = new ShoppingList(Guid.NewGuid(), actor.HouseholdId, request.Name, null, ShoppingBasis.Planned, new([], 0), [], household.ShoppingSourcesRevision);
            database.Add(list); await Finish(actor, cancellation, list); return await Read(actor, list.Id, cancellation);
        }, token), ct);
    public Task<ShoppingListRead> GenerateAsync(RequestActor actor, ShoppingGenerateWrite request, Guid? retryKey, CancellationToken ct)
        => Within(actor, null, token => Retry(actor, "/api/v1/shopping-lists/generate", retryKey, request, async cancellation =>
        {
            var selection = GenerateShoppingList.Selection(request.Selection, request.Basis);
            var sources = await Sources(actor, selection, request.Basis, cancellation);
            var list = new ShoppingList(Guid.NewGuid(), actor.HouseholdId, request.Name, selection, request.Basis,
                sources.Calculation, sources.Revisions, sources.HouseholdRevision);
            database.Add(list);
            database.AddRange(sources.Calculation.Items.Select((item, position) => ShoppingItem.Generated(Guid.NewGuid(), actor.HouseholdId, list.Id, item, position)));
            await Finish(actor, cancellation, list); return await Read(actor, list.Id, cancellation);
        }, token), ct);
    public Task<ShoppingListRead> RenameAsync(RequestActor actor, Guid id, long expected, ShoppingListWrite request, CancellationToken ct)
        => Within(actor, id, async token =>
        {
            var list = await List(actor, id, token); Match(list.Revision, expected); list.Rename(request.Name);
            await Finish(actor, token, list); return await Read(actor, id, token);
        }, ct);
    public async Task DeleteAsync(RequestActor actor, Guid id, long expected, CancellationToken ct)
        => _ = await Within(actor, id, async token =>
        {
            var list = await List(actor, id, token); Match(list.Revision, expected); list.Advance(); database.Remove(list);
            await Finish(actor, token, list, deleted: true); return true;
        }, ct);
}
