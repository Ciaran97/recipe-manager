using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Shopping;

public sealed class ShoppingCalculationTests
{
    [Fact]
    public void Manual_overrides_purchase_and_tombstones_do_not_mutate_the_generated_baseline()
    {
        var requirement = Assert.Single(ShoppingRequirements.Calculate([Source("a", Recipe(Line(Guid.NewGuid(), "Rice", "750", IngredientUnit.Gram)))], ShoppingBasis.Planned).Items);
        var item = ShoppingItem.Generated(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), requirement, 0);
        item.Override(new ShoppingQuantityInput("600", QuantityKind.Numeric, IngredientUnit.Gram, MeasurementStandard.AU));
        item.SetPurchased(true); item.Remove();
        Assert.Equal(new ExactAmount(750, 1), item.Baseline!.Exact!.ToAmount());
        Assert.Equal(new ExactAmount(600, 1), item.EffectiveExact!.ToAmount());
        Assert.True(item.HasOverride); Assert.True(item.Purchased); Assert.True(item.Removed);
        item.Restore(); item.ClearOverride();
        Assert.Equal(new ExactAmount(750, 1), item.EffectiveExact!.ToAmount()); Assert.True(item.Purchased);
    }

    [Fact]
    public void Unknown_override_never_falls_back_to_the_known_baseline_and_manual_items_keep_separate_identities()
    {
        var requirement = Assert.Single(ShoppingRequirements.Calculate([Source("a", Recipe(Line(Guid.NewGuid(), "Salt", "5", IngredientUnit.Gram)))], ShoppingBasis.Planned).Items);
        var household = Guid.NewGuid(); var list = Guid.NewGuid();
        var item = ShoppingItem.Generated(Guid.NewGuid(), household, list, requirement, 0);
        item.Override(new ShoppingQuantityInput("as needed", QuantityKind.Unknown, IngredientUnit.Gram, MeasurementStandard.AU));
        Assert.Null(item.EffectiveExact); Assert.True(item.Unresolved); Assert.Equal("as needed", item.DisplayText);
        Assert.NotNull(item.Baseline!.Exact);
        var a = ShoppingItem.ManualItem(Guid.NewGuid(), household, list, "Salt", new("1", QuantityKind.Numeric, IngredientUnit.Item, MeasurementStandard.AU), 1);
        var b = ShoppingItem.ManualItem(Guid.NewGuid(), household, list, "Salt", new("1", QuantityKind.Numeric, IngredientUnit.Item, MeasurementStandard.AU), 2);
        Assert.NotEqual(a.Id, b.Id); Assert.Null(a.RequirementKey); Assert.Null(b.RequirementKey);
        Assert.True(a.Manual); Assert.True(b.Manual);
    }
    [Fact]
    public void Compatible_units_and_alias_identities_consolidate_without_merging_distinct_ingredients()
    {
        var beef = Guid.NewGuid(); var rice = Guid.NewGuid(); var pepper = Guid.NewGuid(); var strips = Guid.NewGuid();
        var a = Source("a", Recipe(Line(beef, "Beef mince", "500", IngredientUnit.Gram), Line(rice, "Rice", "150", IngredientUnit.Gram), Line(pepper, "Red capsicum", "1", IngredientUnit.Item)));
        var b = Source("b", Recipe(Line(beef, "Beef mince", "250", IngredientUnit.Gram), Line(rice, "Rice", "0.2", IngredientUnit.Kilogram), Line(pepper, "Red pepper", "2", IngredientUnit.Item), Line(strips, "Beef strips", "100", IngredientUnit.Gram)));
        var result = ShoppingRequirements.Calculate([a, b], ShoppingBasis.Planned);
        Assert.Equal(4, result.Items.Count);
        Assert.Equal(new ExactAmount(750, 1), result.Items.Single(x => x.IngredientId == beef).Exact!.ToAmount());
        Assert.Equal(new ExactAmount(350, 1), result.Items.Single(x => x.IngredientId == rice).Exact!.ToAmount());
        Assert.Equal(new ExactAmount(3, 1), result.Items.Single(x => x.IngredientId == pepper).Exact!.ToAmount());
        Assert.Equal(2, result.Items.Single(x => x.IngredientId == pepper).Contributions.Count);
    }

    [Fact]
    public void Unknown_salt_is_a_separate_requirement_and_never_disappears_behind_known_totals()
    {
        var salt = Guid.NewGuid();
        var unknown = Line(salt, "Salt", "to taste", IngredientUnit.Gram, unknown: true) with { Preparation = "Finish after cooking" };
        var recipe = Recipe(Line(salt, "Salt", "5", IngredientUnit.Gram), unknown);
        var result = ShoppingRequirements.Calculate([Source("cook:first", recipe)], ShoppingBasis.Planned);
        Assert.Equal(2, result.Items.Count); Assert.Equal(1, result.UnresolvedCount);
        var known = result.Items.Single(x => !x.Unresolved); var missing = result.Items.Single(x => x.Unresolved);
        Assert.Equal(new ExactAmount(5, 1), known.Exact!.ToAmount()); Assert.True(known.AdditionalAmountUnresolved);
        Assert.Null(missing.Exact); Assert.Contains("to taste", missing.DisplayText);
        Assert.Equal("Finish after cooking", Assert.Single(missing.Contributions).Preparation);
        Assert.NotEqual(known.Key, missing.Key);
        Assert.Equal(missing.Key, ShoppingRequirements.Calculate([Source("cook:first", recipe)], ShoppingBasis.Planned).Items.Single(x => x.Unresolved).Key);
    }

    [Fact]
    public void Only_cooking_sources_contribute_and_planned_preparation_does_not_require_actual_confirmation()
    {
        var recipe = Recipe(Line(Guid.NewGuid(), "Rice", "150", IngredientUnit.Gram));
        var cooking = Source("cook:first", recipe) with { PlannedPrepared = 4m };
        var leftovers = Source("leftovers:second", recipe) with { Type = MealType.Leftovers, PlannedPrepared = 2m };
        var other = Source("other:third", recipe) with { Type = MealType.Other };
        var result = ShoppingRequirements.Calculate([cooking, leftovers, other], ShoppingBasis.Planned);
        Assert.Equal(new ExactAmount(300, 1), Assert.Single(result.Items).Exact!.ToAmount());
        Assert.Throws<DomainValidationException>(() => ShoppingRequirements.Calculate([cooking], ShoppingBasis.Actual));
        Assert.Equal(new ExactAmount(375, 1), Assert.Single(ShoppingRequirements.Calculate([cooking with { ActualPrepared = 5m }], ShoppingBasis.Actual).Items).Exact!.ToAmount());
    }

    [Fact]
    public void Volume_standards_are_exact_and_no_mass_volume_density_is_guessed()
    {
        var ingredient = Guid.NewGuid();
        var au = Recipe(Line(ingredient, "Rice", "1", IngredientUnit.Cup));
        var us = Recipe(Line(ingredient, "Rice", "1", IngredientUnit.Cup, standard: MeasurementStandard.US)) with { MeasurementStandard = MeasurementStandard.US };
        var mass = Recipe(Line(ingredient, "Rice", "100", IngredientUnit.Gram));
        var result = ShoppingRequirements.Calculate([Source("au", au), Source("us", us), Source("mass", mass)], ShoppingBasis.Planned);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new ExactAmount(121647, 250), result.Items.Single(x => x.Dimension == IngredientDimension.Volume).Exact!.ToAmount());
        Assert.Equal(new ExactAmount(100, 1), result.Items.Single(x => x.Dimension == IngredientDimension.Mass).Exact!.ToAmount());
    }

    [Fact]
    public void Scaling_and_consolidation_never_round_intermediate_thirds()
    {
        var recipe = Recipe(Line(Guid.NewGuid(), "Rice", "1", IngredientUnit.Gram)) with { DefaultServings = 3m };
        var result = ShoppingRequirements.Calculate([Source("a", recipe) with { PlannedPrepared = 1m }, Source("b", recipe) with { PlannedPrepared = 1m }, Source("c", recipe) with { PlannedPrepared = 1m }], ShoppingBasis.Planned);
        Assert.Equal(new ExactAmount(1, 1), Assert.Single(result.Items).Exact!.ToAmount());
        Assert.Equal("1", recipe.Ingredients[0].QuantityText);
    }

    [Fact]
    public void Source_order_and_different_preparation_notes_do_not_change_consolidation_keys_or_totals()
    {
        var ingredient = Guid.NewGuid();
        var a = Source("a", Recipe(Line(ingredient, "Carrot", "1", IngredientUnit.Item) with { Preparation = "Diced" }));
        var b = Source("b", Recipe(Line(ingredient, "Carrot", "2", IngredientUnit.Item) with { Preparation = "Sliced" }));
        var first = Assert.Single(ShoppingRequirements.Calculate([a, b], ShoppingBasis.Planned).Items);
        var reversed = Assert.Single(ShoppingRequirements.Calculate([b, a], ShoppingBasis.Planned).Items);
        Assert.Equal(first.Key, reversed.Key); Assert.Equal(first.Exact, reversed.Exact);
        Assert.Equal(new[] { "Diced", "Sliced" }, first.Contributions.Select(x => x.Preparation));
    }

    internal static ShoppingCalculationSource Source(string key, RecipeSnapshot recipe) => new(key, recipe.Title, MealType.Cook, recipe, 2m);
    internal static RecipeSnapshot Recipe(params SnapshotIngredient[] lines) => new(1, Guid.NewGuid(), 1, "Test recipe", null, null, 2m, null, null, null, null, MeasurementStandard.AU, IngredientCalculator.PolicyVersion, [], lines, []);
    internal static SnapshotIngredient Line(Guid ingredient, string name, string quantity, IngredientUnit unit, bool unknown = false, MeasurementStandard standard = MeasurementStandard.AU)
    {
        var canonical = IngredientCalculator.Canonical(new ExactAmount(1, 1), unit, standard);
        return new(Guid.NewGuid(), null, 0, ingredient, name, null, null, quantity, unknown ? QuantityKind.Unknown : QuantityKind.Numeric, unit, null, unknown,
            unknown ? null : ExactQuantity.From(Quantity.Parse(quantity).Exact!), canonical.Dimension, ExactQuantity.From(canonical.Amount));
    }
}
