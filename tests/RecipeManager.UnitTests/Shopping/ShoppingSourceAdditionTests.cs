using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Shopping;

public sealed class ShoppingSourceAdditionTests
{
    [Fact]
    public void Adding_a_recipe_keeps_the_existing_meal_range_and_serving_counts()
    {
        var recipe = Guid.NewGuid();
        var original = new ShoppingSelection(ShoppingSourceKind.MealRange, new(2026, 11, 2), new(2026, 11, 8));
        var added = new ShoppingSelection(ShoppingSourceKind.Recipes, Entries: [new(recipe, 3)]);
        var result = GenerateShoppingList.AddSelection(original, added, ShoppingBasis.Planned);
        Assert.Equal(ShoppingSourceKind.Combined, result.Kind);
        Assert.Equal(new ShoppingMealRange(new(2026, 11, 2), new(2026, 11, 8)), Assert.Single(result.Ranges!));
        Assert.Equal(new ShoppingRecipeSelection(recipe, 3), Assert.Single(result.Entries!));
    }
    [Fact]
    public void Separate_weeks_do_not_select_the_gap_between_them()
    {
        var original = new ShoppingSelection(ShoppingSourceKind.MealRange, new(2026, 11, 2), new(2026, 11, 8));
        var added = new ShoppingSelection(ShoppingSourceKind.MealRange, new(2026, 11, 23), new(2026, 11, 29));
        var result = GenerateShoppingList.AddSelection(original, added, ShoppingBasis.Planned);
        Assert.Equal(2, result.Ranges!.Count); Assert.Null(result.From); Assert.Null(result.To);
    }
    [Fact]
    public void Additional_recipe_servings_are_summed_and_actual_lists_are_rejected_explicitly()
    {
        var recipe = Guid.NewGuid();
        var original = new ShoppingSelection(ShoppingSourceKind.Recipes, Entries: [new(recipe, 2)]);
        var added = new ShoppingSelection(ShoppingSourceKind.Recipes, Entries: [new(recipe, 3)]);
        Assert.Equal(5, Assert.Single(GenerateShoppingList.AddSelection(original, added, ShoppingBasis.Planned).Entries!).Servings);
        Assert.Throws<DomainValidationException>(() => GenerateShoppingList.AddSelection(original, added, ShoppingBasis.Actual));
    }
}
