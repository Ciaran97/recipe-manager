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
        if (request.Kind == ShoppingSourceKind.Combined)
        {
            if (basis != ShoppingBasis.Planned || request.From is not null || request.To is not null || request.Ranges is null || request.Ranges.Count > 1000)
                throw new DomainValidationException("selection", "invalid_combined", "Combined lists use planned quantities and explicit meal ranges.");
            foreach (var range in request.Ranges)
            {
                if (range is null) throw new DomainValidationException("selection", "invalid_range", "Choose valid meal ranges.");
                _ = Selection(new(ShoppingSourceKind.MealRange, range.From, range.To), basis);
            }
            var recipes = Selection(new(ShoppingSourceKind.Recipes, Entries: request.Entries ?? []), basis);
            return request with { Entries = recipes.Entries, Ranges = request.Ranges.Distinct().OrderBy(x => x.From).ThenBy(x => x.To).ToArray() };
        }
        if (request.Ranges is not null) throw new DomainValidationException("selection", "invalid_ranges", "Additional ranges require a combined selection.");
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

    public static ShoppingSelection AddSelection(ShoppingSelection? existing, ShoppingSelection addition, ShoppingBasis basis)
    {
        if (basis != ShoppingBasis.Planned)
            throw new DomainValidationException("basis", "planned_required", "Add recipes or meals to a list using planned quantities.");
        var next = Selection(addition, basis);
        if (existing is null) return next;
        var previous = Selection(existing, basis);
        var entries = new List<ShoppingRecipeSelection>(); var ranges = new List<ShoppingMealRange>();
        foreach (var selected in new[] { previous, next })
        {
            entries.AddRange(selected.Entries ?? []);
            if (selected.Kind == ShoppingSourceKind.MealRange) ranges.Add(new(selected.From!.Value, selected.To!.Value));
            ranges.AddRange(selected.Ranges ?? []);
        }
        return Selection(new(ShoppingSourceKind.Combined, Entries: entries, Ranges: ranges), basis);
    }
}
