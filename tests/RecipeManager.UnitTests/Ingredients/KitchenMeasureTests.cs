using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients.Contracts;

namespace RecipeManager.UnitTests.Ingredients;

public sealed class KitchenMeasureTests
{
    [Theory]
    [InlineData("pinch", IngredientUnit.Pinch, IngredientDimension.Pinch)]
    [InlineData("dash", IngredientUnit.Dash, IngredientDimension.Dash)]
    [InlineData("handful", IngredientUnit.Handful, IngredientDimension.Handful)]
    [InlineData("bunch", IngredientUnit.Bunch, IngredientDimension.Bunch)]
    [InlineData("clove", IngredientUnit.Clove, IngredientDimension.Clove)]
    [InlineData("slice", IngredientUnit.Slice, IngredientDimension.Slice)]
    public void Informal_measures_scale_without_claiming_mass_or_volume(string code, IngredientUnit unit, IngredientDimension dimension)
    {
        Assert.Equal(unit, IngredientCalculator.ParseUnit(code));
        var scaled = IngredientCalculator.Scale(Quantity.Parse("1/2"), 2, 6)!;
        var amount = IngredientCalculator.Canonical(scaled, unit, MeasurementStandard.AU);
        Assert.Equal(dimension, amount.Dimension);
        Assert.Equal(new ExactAmount(3, 2), amount.Amount);
        Assert.Equal("1.5 " + code, IngredientCalculator.Display(amount.Amount, dimension).Text);
        Assert.NotEqual(IngredientDimension.Count, dimension);
    }

    [Theory]
    [InlineData("1 pinch salt", IngredientUnit.Pinch, "salt")]
    [InlineData("2 pinches salt", IngredientUnit.Pinch, "salt")]
    [InlineData("1 handful spinach", IngredientUnit.Handful, "spinach")]
    [InlineData("2 bunches parsley", IngredientUnit.Bunch, "parsley")]
    [InlineData("2 cloves garlic", IngredientUnit.Clove, "garlic")]
    public void Imported_kitchen_measures_keep_the_ingredient_name(string text, IngredientUnit unit, string name)
    {
        var ingredient = ImportedIngredientParser.Parse(text);
        Assert.Equal(unit, ingredient.Unit); Assert.Equal(name, ingredient.IngredientName);
        Assert.Equal(QuantityKind.Numeric, ingredient.Kind);
    }
}
