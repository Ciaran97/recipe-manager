using System.Numerics;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Ingredients;

public sealed class QuantityTests
{
    [Theory]
    [InlineData("0", 0, 1)]
    [InlineData("0.125", 1, 8)]
    [InlineData("1/3", 1, 3)]
    [InlineData("2/6", 1, 3)]
    [InlineData("1 1/2", 3, 2)]
    [InlineData("1000000000", 1000000000, 1)]
    public void Inputs_preserve_originals_and_have_exact_values(string text, long numerator, long denominator)
    {
        var quantity = Quantity.Parse(text);
        Assert.Equal(text, quantity.OriginalText);
        Assert.Equal(new ExactAmount(numerator, denominator), quantity.Exact);
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("-1")]
    [InlineData("-1/3")]
    [InlineData("1/-3")]
    [InlineData("0.1234567")]
    [InlineData("1000000000.000001")]
    [InlineData("1000000001/1000000001")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("1,5")]
    [InlineData("")]
    public void Invalid_or_unbounded_inputs_are_rejected_without_rounding(string input)
        => Assert.Throws<DomainValidationException>(() => Quantity.Parse(input));

    [Fact]
    public void Unknown_is_not_zero_and_has_no_numeric_storage()
    {
        var unknown = Quantity.Unknown("to taste");
        Assert.Null(unknown.Exact);
        Assert.Null(unknown.DecimalValue);
        Assert.Null(unknown.FractionNumerator);
        Assert.Null(unknown.FractionDenominator);
        Assert.Equal("to taste", unknown.OriginalText);
        Assert.NotNull(Quantity.Parse("0").Exact);
    }

    [Fact]
    public void Storage_keeps_decimal_and_fraction_forms_mutually_exclusive()
    {
        var decimalValue = Quantity.Parse("0.500000");
        Assert.Equal(0.5m, decimalValue.DecimalValue);
        Assert.Null(decimalValue.FractionNumerator);
        var fraction = Quantity.Parse("2/4");
        Assert.Null(fraction.DecimalValue);
        Assert.Equal(1, fraction.FractionNumerator);
        Assert.Equal(2, fraction.FractionDenominator);
    }

    [Fact]
    public void Portions_are_bounded_positive_decimals_with_explicit_zero_consumption()
    {
        Assert.Equal(1.5m, PortionQuantity.Parse("1.5"));
        Assert.Equal(0.333333m, PortionQuantity.Parse("0.333333"));
        Assert.Equal(0m, PortionQuantity.Parse("0", allowZero: true));
        foreach (var input in new[] { "0", "-1", "1000000.000001", "0.0000001", "1/2" })
            Assert.Throws<DomainValidationException>(() => PortionQuantity.Parse(input));
    }

    [Fact]
    public void Scaling_thirds_does_not_lose_precision_or_change_source()
    {
        var source = Quantity.Parse("1");
        var third = IngredientCalculator.Scale(source, 3m, 1m)!;
        Assert.Equal(new ExactAmount(1, 3), third);
        Assert.Equal(new ExactAmount(1, 1), third + third + third);
        Assert.Equal("1", source.OriginalText);
        Assert.Null(IngredientCalculator.Scale(Quantity.Unknown("as needed"), 3m, 1m));
    }

    [Fact]
    public void Australian_and_US_volumes_consolidate_exactly_and_mass_does_not_convert_to_volume()
    {
        var australian = IngredientCalculator.Canonical(Quantity.Parse("1").Exact!, IngredientUnit.Cup, MeasurementStandard.AU);
        var american = IngredientCalculator.Canonical(Quantity.Parse("1").Exact!, IngredientUnit.Cup, MeasurementStandard.US);
        Assert.Equal(new ExactAmount(121647, 250), australian.Amount + american.Amount); // 486.588 ml
        Assert.Equal(IngredientDimension.Volume, australian.Dimension);
        Assert.Equal(new ExactAmount(20, 1), IngredientCalculator.Canonical(new ExactAmount(1, 1), IngredientUnit.Tablespoon, MeasurementStandard.AU).Amount);
        Assert.Equal(new ExactAmount(14787, 1000), IngredientCalculator.Canonical(new ExactAmount(1, 1), IngredientUnit.Tablespoon, MeasurementStandard.US).Amount);
        Assert.Equal(new ExactAmount(5, 1), IngredientCalculator.Canonical(new ExactAmount(1, 1), IngredientUnit.Teaspoon, MeasurementStandard.AU).Amount);
        Assert.Equal(new ExactAmount(4929, 1000), IngredientCalculator.Canonical(new ExactAmount(1, 1), IngredientUnit.Teaspoon, MeasurementStandard.US).Amount);
        Assert.NotEqual(australian.Dimension, IngredientCalculator.Canonical(new ExactAmount(1, 1), IngredientUnit.Gram, MeasurementStandard.AU).Dimension);
        Assert.Equal(new ExactAmount(350, 1), IngredientCalculator.Canonical(new ExactAmount(1, 5), IngredientUnit.Kilogram, MeasurementStandard.AU).Amount + new ExactAmount(150, 1));
    }

    [Fact]
    public void Exact_arithmetic_is_order_independent_for_bounded_fraction_inputs()
    {
        var random = new Random(78213);
        for (var i = 0; i < 100; i++)
        {
            var first = new ExactAmount(random.Next(100000), random.Next(1, 100000));
            var second = new ExactAmount(random.Next(1, 100000), random.Next(1, 100000));
            var third = new ExactAmount(random.Next(100000), random.Next(1, 100000));
            Assert.Equal((first + second) + third, first + (second + third));
            Assert.Equal(first, first * second / second);
        }
    }

    [Fact]
    public void Arithmetic_and_batch_limits_fail_as_a_whole()
    {
        Assert.Throws<DomainValidationException>(() => new ExactAmount(BigInteger.Pow(10, 4096), 1));
        Assert.Throws<DomainValidationException>(() => new ExactAmount(1, 0));
        Assert.Throws<DomainValidationException>(() => IngredientCalculator.ValidateBatchSize(1001));
        IngredientCalculator.ValidateBatchSize(1000);
        var largest = new ExactAmount(BigInteger.Pow(10, 4095), 1);
        Assert.Throws<DomainValidationException>(() => largest * new ExactAmount(10, 1));
    }

    [Fact]
    public void Display_rounds_only_at_the_boundary_and_never_hides_a_positive_quantity()
    {
        var third = new ExactAmount(1, 3);
        var display = IngredientCalculator.Display(third, IngredientDimension.Mass);
        Assert.Equal("0.33 g", display.Text);
        Assert.True(display.IsApproximate);
        Assert.Equal(new ExactAmount(1, 3), display.Exact);
        Assert.Equal("<0.01 g", IngredientCalculator.Display(new ExactAmount(1, 1000), IngredientDimension.Mass).Text);
        Assert.Equal("1.01 g", IngredientCalculator.Display(new ExactAmount(201, 200), IngredientDimension.Mass).Text);
        Assert.Equal("1.5 kg", IngredientCalculator.Display(new ExactAmount(1500, 1), IngredientDimension.Mass).Text);
        Assert.Equal("1/8 cup", IngredientCalculator.OriginalUnitDisplay(new ExactAmount(1, 8), IngredientUnit.Cup).Text);
    }
}
