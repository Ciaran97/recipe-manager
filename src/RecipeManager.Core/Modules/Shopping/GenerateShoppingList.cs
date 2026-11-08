using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Shopping;

public sealed class GenerateShoppingList(IShoppingStore store)
{
    public Task<ShoppingListRead> GenerateAsync(RequestActor actor, ShoppingGenerateWrite request, Guid? retryKey, CancellationToken ct)
    {
        _ = DomainRules.Name(request.Name, "name");
        var selection = Selection(request.Selection, request.Basis);
        return store.GenerateAsync(actor, request with { Selection = selection }, retryKey, ct);
    }
    public static ShoppingSelection Selection(ShoppingSelection request, ShoppingBasis basis)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(basis)) throw new DomainValidationException("selection", "unsupported", "Choose a supported source selection and basis.");
        if (request.Kind == ShoppingSourceKind.MealRange)
        {
            if (request.From is null || request.To is null || request.From > request.To || request.To.Value.DayNumber - request.From.Value.DayNumber > 365 || request.Entries is not null)
                throw new DomainValidationException("selection", "invalid_range", "Choose an inclusive meal date range of at most 366 days, without recipe entries.");
            return request;
        }
        if (basis == ShoppingBasis.Actual || request.From is not null || request.To is not null || request.Entries is null || request.Entries.Count > 1000 || request.Entries.Any(x => x is null))
            throw new DomainValidationException("selection", "invalid_recipes", "Select recipe serving counts without dates or an actual cooking basis; use at most 1000 entries.");
        foreach (var entry in request.Entries) { DomainRules.NonEmptyId(entry.RecipeId, "recipeId"); PortionQuantity.Validate(entry.Servings); }
        var entries = request.Entries.GroupBy(x => x.RecipeId).OrderBy(x => x.Key)
            .Select(group => new ShoppingRecipeSelection(group.Key, PortionQuantity.Validate(group.Sum(x => x.Servings)))).ToArray();
        return request with { Entries = entries };
    }
}
