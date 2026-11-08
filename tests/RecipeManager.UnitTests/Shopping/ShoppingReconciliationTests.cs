using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Shopping;

public sealed class ShoppingReconciliationTests
{
    [Fact]
    public void Preview_does_not_mutate_and_keep_user_preserves_override_purchase_and_manual_items()
    {
        var (list, item, old) = Setup(); var next = Changed(old, "900");
        item.Override(new("600", QuantityKind.Numeric, IngredientUnit.Gram)); item.SetPurchased(true);
        var manual = ShoppingItem.ManualItem(Guid.NewGuid(), list.HouseholdId, list.Id, "Bread", new("1", QuantityKind.Numeric, IngredientUnit.Item), 1);
        var before = list.BaselineJson;
        var change = Assert.Single(ReconcileShoppingList.Changes(list, [item, manual], next));
        Assert.True(change.Conflict); Assert.Equal(ReconciliationChoice.KeepUser, change.DefaultChoice);
        Assert.Equal(before, list.BaselineJson); Assert.Equal(new ExactAmount(750, 1), item.Baseline!.Exact!.ToAmount());
        var edits = ReconcileShoppingList.ApplyChoices(list, [item, manual], next, [new(item.RequirementKey!, ReconciliationChoice.KeepUser)]);
        Assert.Empty(edits.Added); Assert.Empty(edits.Deleted);
        Assert.Equal(new ExactAmount(600, 1), item.EffectiveExact!.ToAmount()); Assert.True(item.Purchased);
        Assert.Equal(new ExactAmount(900, 1), item.Baseline!.Exact!.ToAmount());
        Assert.True(manual.Manual); Assert.Equal("Bread", manual.Label);
    }

    [Fact]
    public void Increased_purchased_requirements_need_an_explicit_purchase_decision_before_any_changes()
    {
        var (list, item, old) = Setup(); item.SetPurchased(true); var next = Changed(old, "900");
        Assert.True(Assert.Single(ReconcileShoppingList.Changes(list, [item], next)).PurchaseDecisionRequired);
        Assert.Throws<DomainValidationException>(() => ReconcileShoppingList.ApplyChoices(list, [item], next, [new(item.RequirementKey!, ReconciliationChoice.AcceptNew)]));
        Assert.Equal(new ExactAmount(750, 1), item.EffectiveExact!.ToAmount()); Assert.True(item.Purchased);
        ReconcileShoppingList.ApplyChoices(list, [item], next, [new(item.RequirementKey!, ReconciliationChoice.AcceptNew, Purchased: false)]);
        Assert.False(item.Purchased); Assert.Equal(new ExactAmount(900, 1), item.EffectiveExact!.ToAmount());
    }

    [Fact]
    public void Missing_or_duplicate_choices_fail_without_touching_existing_items()
    {
        var (list, item, old) = Setup(); var next = Changed(old, "900");
        Assert.Throws<DomainValidationException>(() => ReconcileShoppingList.ApplyChoices(list, [item], next, []));
        var choice = new ShoppingReconciliationChoice(item.RequirementKey!, ReconciliationChoice.AcceptNew);
        Assert.Throws<DomainValidationException>(() => ReconcileShoppingList.ApplyChoices(list, [item], next, [choice, choice]));
        Assert.Equal(new ExactAmount(750, 1), item.EffectiveExact!.ToAmount()); Assert.False(item.HasOverride);
    }

    [Fact]
    public void Removed_tombstone_survives_keep_user_and_removed_sources_require_explicit_choices()
    {
        var (list, item, old) = Setup(); item.Remove();
        ReconcileShoppingList.ApplyChoices(list, [item], Changed(old, "900"), [new(item.RequirementKey!, ReconciliationChoice.KeepUser)]);
        Assert.True(item.Removed); Assert.False(item.HasOverride);
        var edits = ReconcileShoppingList.ApplyChoices(list, [item], new([], 0), [new(item.RequirementKey!, ReconciliationChoice.Delete)]);
        Assert.Equal(item.Id, Assert.Single(edits.Deleted).Id);
    }

    private static (ShoppingList List, ShoppingItem Item, ShoppingCalculation Baseline) Setup()
    {
        var recipe = ShoppingCalculationTests.Recipe(ShoppingCalculationTests.Line(Guid.NewGuid(), "Rice", "750", IngredientUnit.Gram));
        var baseline = ShoppingRequirements.Calculate([ShoppingCalculationTests.Source("source", recipe)], ShoppingBasis.Planned);
        var list = new ShoppingList(Guid.NewGuid(), Guid.NewGuid(), "List", null, ShoppingBasis.Planned, baseline, [], 1);
        return (list, ShoppingItem.Generated(Guid.NewGuid(), list.HouseholdId, list.Id, baseline.Items[0], 0), baseline);
    }
    private static ShoppingCalculation Changed(ShoppingCalculation old, string amount) => new([old.Items[0] with { Exact = new(amount, "1"), DisplayText = amount + " g" }], 0);
}
