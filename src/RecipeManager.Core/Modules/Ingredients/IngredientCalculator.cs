using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Ingredients.Contracts;

public enum MeasurementStandard { AU, US }
public enum IngredientUnit
{
    [JsonStringEnumMemberName("g")] Gram,
    [JsonStringEnumMemberName("kg")] Kilogram,
    [JsonStringEnumMemberName("ml")] Millilitre,
    [JsonStringEnumMemberName("l")] Litre,
    [JsonStringEnumMemberName("tsp")] Teaspoon,
    [JsonStringEnumMemberName("tbsp")] Tablespoon,
    [JsonStringEnumMemberName("cup")] Cup,
    [JsonStringEnumMemberName("item")] Item
}
public enum IngredientDimension { Mass, Volume, Count }
public sealed record CanonicalAmount(ExactAmount Amount, IngredientDimension Dimension);
public sealed record AmountDisplay(string Text, bool IsApproximate, ExactAmount Exact);

public static class IngredientCalculator
{
    public const int PolicyVersion = 1;
    public const int MaximumBatchLines = 1000;
    public static void ValidateBatchSize(int count)
    {
        if (count is < 0 or > MaximumBatchLines)
            throw new DomainValidationException("ingredients", "calculation_limit", "Calculate at most 1000 ingredient lines in one batch.");
    }
    public static ExactAmount? Scale(Quantity quantity, decimal defaultServings, decimal targetServings)
    {
        PortionQuantity.Validate(defaultServings); PortionQuantity.Validate(targetServings);
        return quantity.Exact is { } amount ? amount * ExactAmount.FromDecimal(targetServings) / ExactAmount.FromDecimal(defaultServings) : null;
    }
    public static CanonicalAmount Canonical(ExactAmount amount, IngredientUnit unit, MeasurementStandard standard)
    {
        if (!Enum.IsDefined(standard)) throw new DomainValidationException("measurementStandard", "unsupported", "Choose AU or US measures.");
        var (dimension, factor) = unit switch
        {
            IngredientUnit.Gram => (IngredientDimension.Mass, 1m),
            IngredientUnit.Kilogram => (IngredientDimension.Mass, 1000m),
            IngredientUnit.Millilitre => (IngredientDimension.Volume, 1m),
            IngredientUnit.Litre => (IngredientDimension.Volume, 1000m),
            IngredientUnit.Teaspoon => (IngredientDimension.Volume, standard == MeasurementStandard.AU ? 5m : 4.929m),
            IngredientUnit.Tablespoon => (IngredientDimension.Volume, standard == MeasurementStandard.AU ? 20m : 14.787m),
            IngredientUnit.Cup => (IngredientDimension.Volume, standard == MeasurementStandard.AU ? 250m : 236.588m),
            IngredientUnit.Item => (IngredientDimension.Count, 1m),
            _ => throw new DomainValidationException("unit", "unsupported", "Choose a supported ingredient unit.")
        };
        return new CanonicalAmount(amount * ExactAmount.FromDecimal(factor), dimension);
    }
    public static IngredientUnit ParseUnit(string value) => value switch
    {
        "g" => IngredientUnit.Gram,
        "kg" => IngredientUnit.Kilogram,
        "ml" => IngredientUnit.Millilitre,
        "l" => IngredientUnit.Litre,
        "tsp" => IngredientUnit.Teaspoon,
        "tbsp" => IngredientUnit.Tablespoon,
        "cup" => IngredientUnit.Cup,
        "item" => IngredientUnit.Item,
        _ => throw new DomainValidationException("unit", "unsupported", "Choose g, kg, ml, l, tsp, tbsp, cup or item.")
    };
    public static string UnitCode(IngredientUnit value) => value switch
    {
        IngredientUnit.Gram => "g",
        IngredientUnit.Kilogram => "kg",
        IngredientUnit.Millilitre => "ml",
        IngredientUnit.Litre => "l",
        IngredientUnit.Teaspoon => "tsp",
        IngredientUnit.Tablespoon => "tbsp",
        IngredientUnit.Cup => "cup",
        IngredientUnit.Item => "item",
        _ => throw new DomainValidationException("unit", "unsupported", "Choose a supported ingredient unit.")
    };
    public static AmountDisplay Display(ExactAmount amount, IngredientDimension dimension)
    {
        var largeUnit = dimension != IngredientDimension.Count && amount.Numerator >= 1000 * amount.Denominator;
        var displayed = largeUnit ? amount / new ExactAmount(1000, 1) : amount;
        var unit = dimension switch
        {
            IngredientDimension.Mass => largeUnit ? "kg" : "g",
            IngredientDimension.Volume => largeUnit ? "L" : "ml",
            IngredientDimension.Count => "item",
            _ => throw new DomainValidationException("dimension", "unsupported", "Choose a supported ingredient dimension.")
        };
        var (text, approximate) = Rounded(displayed, dimension == IngredientDimension.Count ? 6 : 2);
        return new AmountDisplay($"{text} {unit}", approximate, amount);
    }
    public static AmountDisplay OriginalUnitDisplay(ExactAmount amount, IngredientUnit unit)
    {
        var code = unit == IngredientUnit.Litre ? "L" : UnitCode(unit);
        var denominator = amount.Denominator;
        if (unit is IngredientUnit.Cup or IngredientUnit.Tablespoon or IngredientUnit.Teaspoon && (denominator == 2 || denominator == 4 || denominator == 8))
        {
            var whole = BigInteger.DivRem(amount.Numerator, amount.Denominator, out var remainder);
            var fraction = $"{remainder.ToString(CultureInfo.InvariantCulture)}/{amount.Denominator.ToString(CultureInfo.InvariantCulture)}";
            return new AmountDisplay($"{(whole.IsZero ? fraction : whole.ToString(CultureInfo.InvariantCulture) + " " + fraction)} {code}", false, amount);
        }
        var (text, approximate) = Rounded(amount, unit is IngredientUnit.Gram or IngredientUnit.Millilitre ? 2 : 6);
        return new AmountDisplay($"{text} {code}", approximate, amount);
    }
    private static (string Text, bool Approximate) Rounded(ExactAmount amount, int places)
    {
        var scale = BigInteger.Pow(10, places);
        var integer = BigInteger.DivRem(amount.Numerator * scale, amount.Denominator, out var remainder);
        if (remainder * 2 >= amount.Denominator) integer++;
        if (integer.IsZero && !amount.Numerator.IsZero) return ("<0." + new string('0', places - 1) + "1", true);
        var whole = BigInteger.DivRem(integer, scale, out var fractional);
        var text = whole.ToString(CultureInfo.InvariantCulture);
        if (!fractional.IsZero) text += "." + fractional.ToString(CultureInfo.InvariantCulture).PadLeft(places, '0').TrimEnd('0');
        return (text, !remainder.IsZero);
    }
}
