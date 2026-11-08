using RecipeManager.Core.Modules.Shopping.Contracts;

namespace RecipeManager.Core.Modules.Shopping;

public static class ShoppingViews
{
    public static ShoppingListRead Read(ShoppingList list, IReadOnlyList<ShoppingItem> items)
    {
        var unknownIdentities = items.Where(x => !x.Removed && x.Unresolved && x.IngredientId is not null).Select(x => x.IngredientId!.Value).ToHashSet();
        var rows = items.OrderBy(x => x.Position).ThenBy(x => x.Id).Select(x => Item(x, unknownIdentities)).ToArray();
        var active = rows.Where(x => !x.Removed).ToArray();
        var known = active.Where(x => x.EffectiveExact is not null).Select(x => new ShoppingKnownTotal(x.Id, x.IngredientId, x.Label, x.Dimension!.Value, x.EffectiveExact!, x.Purchased)).ToArray();
        return new(list.Id, list.Revision, list.Name, list.Selection, list.Basis, list.PolicyVersion, list.HouseholdSourceRevision,
            list.SourceRevisions, rows, new(known.Length, active.Count(x => x.Unresolved), active.Count(x => x.Purchased), rows.Count(x => x.Removed), known));
    }
    public static ShoppingItemRead Item(ShoppingItem item, IReadOnlySet<Guid>? unknownIdentities = null)
    {
        var baseline = item.Baseline; var value = item.OverrideValue;
        var unresolved = value is not null ? value.Exact is null : baseline!.Unresolved;
        var additional = !unresolved && item.IngredientId is { } ingredient && (unknownIdentities?.Contains(ingredient) ?? baseline?.AdditionalAmountUnresolved == true);
        return new(item.Id, item.RequirementKey, item.IngredientId, item.Label, item.CategoryId, item.CategoryName,
            value is not null ? value.Dimension : baseline?.Dimension, baseline?.Exact,
            value is not null ? value.Exact : baseline?.Exact, value, item.HasOverride,
            value?.DisplayText ?? baseline!.DisplayText, value?.IsApproximate ?? baseline!.IsApproximate, unresolved, additional,
            item.Purchased, item.Manual, item.Removed, item.Position, baseline?.Contributions ?? []);
    }
}
