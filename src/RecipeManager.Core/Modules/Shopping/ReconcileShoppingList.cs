using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Shopping;

public sealed record ShoppingReconciliationEdits(IReadOnlyList<ShoppingItem> Added, IReadOnlyList<ShoppingItem> Deleted);
public sealed class ShoppingSourceConflictException : Exception;

public sealed class ReconcileShoppingList(IShoppingStore store)
{
    public Task<ShoppingPreview> PreviewAsync(RequestActor actor, Guid id, long expected, ShoppingPreviewWrite request, CancellationToken ct)
        => store.PreviewAsync(actor, id, expected, request, ct);
    public Task<ShoppingListRead> ApplyAsync(RequestActor actor, Guid id, long expected, ShoppingApplyWrite request, CancellationToken ct)
        => store.ApplyAsync(actor, id, expected, request, ct);

    public static IReadOnlyList<ShoppingChange> Changes(ShoppingList list, IReadOnlyList<ShoppingItem> items, ShoppingCalculation next)
    {
        var old = list.Baseline.Items.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var proposed = next.Items.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var states = items.Where(x => !x.Manual).ToDictionary(x => x.RequirementKey!, StringComparer.Ordinal);
        var unknown = items.Where(x => !x.Removed && x.Unresolved && x.IngredientId is not null).Select(x => x.IngredientId!.Value).ToHashSet();
        var changes = new List<ShoppingChange>();
        foreach (var key in old.Keys.Concat(proposed.Keys).Concat(states.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            states.TryGetValue(key, out var state);
            var before = old.GetValueOrDefault(key) ?? state?.Baseline;
            var after = proposed.GetValueOrDefault(key);
            if (ShoppingJson.Encode(before) == ShoppingJson.Encode(after)) continue;
            var purchaseDecision = state?.Purchased == true && Increased(before, after);
            var conflict = state is not null && (state.HasOverride || state.Removed || purchaseDecision);
            var allowed = state is null ? new[] { ReconciliationChoice.AcceptNew, ReconciliationChoice.Delete }
                : after is null ? [ReconciliationChoice.KeepUser, ReconciliationChoice.Delete]
                : new[] { ReconciliationChoice.AcceptNew, ReconciliationChoice.KeepUser, ReconciliationChoice.Delete };
            var defaultChoice = conflict ? ReconciliationChoice.KeepUser : after is null ? ReconciliationChoice.Delete : ReconciliationChoice.AcceptNew;
            changes.Add(new(key, before, after, state is null ? null : ShoppingViews.Item(state, unknown), conflict, purchaseDecision, defaultChoice, allowed));
        }
        return changes;
    }

    public static ShoppingReconciliationEdits ApplyChoices(ShoppingList list, IReadOnlyList<ShoppingItem> items,
        ShoppingCalculation next, IReadOnlyList<ShoppingReconciliationChoice> choices)
    {
        var changes = Changes(list, items, next);
        if (choices is null || choices.Any(x => x is null) || choices.Count != changes.Count || choices.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != choices.Count)
            throw InvalidChoices();
        var selected = choices.ToDictionary(x => x.Key, StringComparer.Ordinal);
        // Validate the complete review before changing even an in-memory item.
        foreach (var change in changes)
        {
            if (!selected.TryGetValue(change.Key, out var choice) || !change.AllowedChoices.Contains(choice.Choice)) throw InvalidChoices();
            if (choice.Choice == ReconciliationChoice.AcceptNew && change.PurchaseDecisionRequired && choice.Purchased is null)
                throw new DomainValidationException("choices.purchased", "review_required", "Choose whether the increased requirement is already purchased.");
        }
        var states = items.Where(x => !x.Manual).ToDictionary(x => x.RequirementKey!, StringComparer.Ordinal);
        var added = new List<ShoppingItem>(); var deleted = new List<ShoppingItem>();
        var position = items.Select(x => x.Position).DefaultIfEmpty(-1).Max();
        foreach (var change in changes)
        {
            var choice = selected[change.Key]; states.TryGetValue(change.Key, out var item);
            if (choice.Choice == ReconciliationChoice.Delete && change.NewBaseline is null)
            {
                if (item is not null) deleted.Add(item);
                continue;
            }
            if (item is null)
            {
                item = ShoppingItem.Generated(Guid.NewGuid(), list.HouseholdId, list.Id, change.NewBaseline!, checked(++position)); added.Add(item);
            }
            if (choice.Choice == ReconciliationChoice.KeepUser && !item.Removed) item.PreserveCurrentQuantity();
            if (change.NewBaseline is not null) item.SetGenerated(change.NewBaseline);
            switch (choice.Choice)
            {
                case ReconciliationChoice.AcceptNew: item.ClearOverride(); item.Restore(); break;
                case ReconciliationChoice.Delete: item.Remove(); break;
            }
            if (choice.Purchased is { } purchased) item.SetPurchased(purchased);
        }
        return new(added, deleted);
    }
    private static bool Increased(ShoppingRequirement? before, ShoppingRequirement? after)
        => before?.Exact is { } old && after?.Exact is { } next && after.Dimension == before.Dimension
            && next.ToAmount().Numerator * old.ToAmount().Denominator > old.ToAmount().Numerator * next.ToAmount().Denominator;
    private static DomainValidationException InvalidChoices()
        => new("choices", "review_required", "Review every changed row exactly once using its permitted choices.");
}
