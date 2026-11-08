using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Infrastructure.Shopping;

public sealed partial class ShoppingStore
{
    public Task<ShoppingListRead> AddItemAsync(RequestActor actor, Guid id, long expected, ShoppingManualItemWrite request, Guid? retryKey, CancellationToken ct)
        => Within(actor, id, token => Retry(actor, $"/api/v1/shopping-lists/{id:D}/items", retryKey, request, async cancellation =>
        {
            var list = await List(actor, id, cancellation); Match(list.Revision, expected);
            var items = await Items(actor, id, cancellation);
            var category = request.CategoryId is { } categoryId ? await database.IngredientCategories.AsNoTracking()
                .SingleOrDefaultAsync(x => x.HouseholdId == actor.HouseholdId && x.Id == categoryId, cancellation) ?? throw new ResourceMissingException() : null;
            var item = ShoppingItem.ManualItem(Guid.NewGuid(), actor.HouseholdId, id, request.Label,
                new(request.QuantityText, request.Kind, request.Unit, request.MeasurementStandard), checked(items.Select(x => x.Position).DefaultIfEmpty(-1).Max() + 1), category?.Id, category?.Name);
            database.Add(item); list.Advance(); await Finish(actor, cancellation, list); return await Read(actor, id, cancellation);
        }, token), ct);
    private async Task<ShoppingItem> Item(RequestActor actor, Guid id, Guid itemId, CancellationToken ct)
        => await database.ShoppingItems.SingleOrDefaultAsync(x => x.HouseholdId == actor.HouseholdId && x.ListId == id && x.Id == itemId, ct) ?? throw new ResourceMissingException();
    public Task<ShoppingListRead> EditItemAsync(RequestActor actor, Guid id, Guid itemId, long expected, ShoppingItemWrite request, CancellationToken ct)
        => Within(actor, id, async token =>
        {
            var list = await List(actor, id, token); var item = await Item(actor, id, itemId, token); Match(list.Revision, expected);
            if (request.ClearOverride && request.QuantityOverride is not null)
                throw new DomainValidationException("quantityOverride", "ambiguous_quantity", "Clear the override or enter a replacement quantity.");
            if (request.QuantityOverride is not null) item.Override(request.QuantityOverride);
            if (request.ClearOverride) item.ClearOverride();
            if (request.Label is not null) item.RenameManual(request.Label);
            if (request.Purchased is { } purchased) item.SetPurchased(purchased);
            if (request.Removed is { } removed)
            {
                if (item.Manual && removed) database.Remove(item);
                else if (removed) item.Remove(); else item.Restore();
            }
            list.Advance(); await Finish(actor, token, list); return await Read(actor, id, token);
        }, ct);
    public Task<ShoppingListRead> RemoveItemAsync(RequestActor actor, Guid id, Guid itemId, long expected, CancellationToken ct)
        => Within(actor, id, async token =>
        {
            var list = await List(actor, id, token); var item = await Item(actor, id, itemId, token); Match(list.Revision, expected);
            if (item.Manual) database.Remove(item); else item.Remove();
            list.Advance(); await Finish(actor, token, list); return await Read(actor, id, token);
        }, ct);
}
