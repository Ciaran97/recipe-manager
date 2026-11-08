using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Shopping.Contracts;

namespace RecipeManager.Core.Modules.Shopping;

public sealed class EditShoppingList(IShoppingStore store)
{
    public Task<ShoppingListRead> AddAsync(RequestActor actor, Guid id, long expected, ShoppingManualItemWrite request, Guid? retryKey, CancellationToken ct)
        => store.AddItemAsync(actor, id, expected, request, retryKey, ct);
    public Task<ShoppingListRead> EditAsync(RequestActor actor, Guid id, Guid itemId, long expected, ShoppingItemWrite request, CancellationToken ct)
        => store.EditItemAsync(actor, id, itemId, expected, request, ct);
    public Task<ShoppingListRead> RemoveAsync(RequestActor actor, Guid id, Guid itemId, long expected, CancellationToken ct)
        => store.RemoveItemAsync(actor, id, itemId, expected, ct);
}
