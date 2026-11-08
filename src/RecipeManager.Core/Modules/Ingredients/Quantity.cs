using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Ingredients.Contracts;

public enum QuantityKind
{
    [JsonStringEnumMemberName("numeric")] Numeric,
    [JsonStringEnumMemberName("unknown")] Unknown
}

/// <summary>Entered values retain decimal or fraction storage; derived values are never written back.</summary>
public sealed record Quantity
{
    public QuantityKind Kind { get; }
    public string OriginalText { get; }
    public decimal? DecimalValue { get; }
    public long? FractionNumerator { get; }
    public long? FractionDenominator { get; }
    public ExactAmount? Exact => DecimalValue is { } value ? ExactAmount.FromDecimal(value)
        : FractionNumerator is { } numerator && FractionDenominator is { } denominator ? new ExactAmount(numerator, denominator) : null;
    private Quantity(QuantityKind kind, string text, decimal? value = null, long? numerator = null, long? denominator = null)
        => (Kind, OriginalText, DecimalValue, FractionNumerator, FractionDenominator) = (kind, text, value, numerator, denominator);

    private static readonly Regex Fraction = new(@"^(?:(?<whole>[0-9]+) +)?(?<numerator>[0-9]+) */ *(?<denominator>[0-9]+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static readonly Regex DecimalInput = new(@"^(?:[0-9]+(?:\.[0-9]{0,6})?|\.[0-9]{1,6})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public const long MaximumInputComponent = 1_000_000_000;

    public static Quantity Unknown(string text)
    {
        ValidateText(text);
        return new Quantity(QuantityKind.Unknown, text);
    }
    public static Quantity Parse(string text)
    {
        ValidateText(text);
        var input = text.Trim();
        if (DecimalInput.IsMatch(input) && decimal.TryParse(input, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            && value <= MaximumInputComponent)
            return new Quantity(QuantityKind.Numeric, text, value);
        var fraction = Fraction.Match(input);
        if (fraction.Success)
        {
            var whole = fraction.Groups["whole"].Success ? Component(fraction.Groups["whole"].Value) : 0;
            var numerator = Component(fraction.Groups["numerator"].Value);
            var denominator = Component(fraction.Groups["denominator"].Value);
            if (denominator == 0 || (fraction.Groups["whole"].Success && numerator >= denominator)) throw Invalid();
            var improper = checked(whole * denominator + numerator);
            if (improper > MaximumInputComponent * denominator) throw Invalid();
            var divisor = (long)BigInteger.GreatestCommonDivisor(improper, denominator);
            return new Quantity(QuantityKind.Numeric, text, numerator: improper / divisor, denominator: denominator / divisor);
        }
        throw Invalid();
    }
    private static long Component(string text)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= MaximumInputComponent ? value : throw Invalid();
    private static void ValidateText(string text)
    {
        if (text is null || text.Length > 200 || text.Contains('\n') || text.Contains('\r')) throw Invalid();
    }
    private static DomainValidationException Invalid() => new("quantityText", "invalid_quantity", "Use a nonnegative decimal with at most six places or a fraction with positive denominator. Quantities and input components must not exceed 1000000000.");
}

public static class PortionQuantity
{
    public const decimal Maximum = 1_000_000m;
    public static decimal Parse(string text, bool allowZero = false)
    {
        if (text is null || text.Length > 200 || !Quantity.DecimalInput.IsMatch(text.Trim())
            || !decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) throw Invalid();
        return Validate(value, allowZero);
    }
    public static decimal Validate(decimal value, bool allowZero = false)
    {
        if (value < 0 || (!allowZero && value == 0) || value > Maximum || ((decimal.GetBits(value)[3] >> 16) & 0xff) > 6) throw Invalid();
        return value;
    }
    private static DomainValidationException Invalid() => new("portions", "invalid_portions", "Use a positive decimal up to 1000000 with at most six places; consumed portions may explicitly be zero.");
}

/// <summary>Small, bounded, pure calculation value. It is not a relational storage type.</summary>
public sealed record ExactAmount
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public const int MaximumDigits = 4096;
    public ExactAmount(BigInteger numerator, BigInteger denominator)
    {
        if (numerator < 0 || denominator <= 0) throw new DomainValidationException("quantity", "invalid_quantity", "Exact quantities must be nonnegative with a positive denominator.");
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor; Denominator = denominator / divisor;
        if (Digits(Numerator) > MaximumDigits || Digits(Denominator) > MaximumDigits)
            throw new DomainValidationException("quantity", "calculation_limit", "The exact calculation exceeds the 4096-digit component limit.");
    }
    private static int Digits(BigInteger value) => value.ToString(CultureInfo.InvariantCulture).Length;
    public static ExactAmount FromDecimal(decimal value)
    {
        if (value < 0) throw new DomainValidationException("quantity", "invalid_quantity", "Quantities must be nonnegative.");
        var bits = decimal.GetBits(value);
        var numerator = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        return new ExactAmount(numerator, BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
    }
    public static ExactAmount operator +(ExactAmount left, ExactAmount right)
        => new(left.Numerator * right.Denominator + right.Numerator * left.Denominator, left.Denominator * right.Denominator);
    public static ExactAmount operator *(ExactAmount left, ExactAmount right)
        => new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    public static ExactAmount operator /(ExactAmount left, ExactAmount right)
    {
        if (right.Numerator.IsZero) throw new DomainValidationException("quantity", "division_by_zero", "A scaling divisor must be positive.");
        return new ExactAmount(left.Numerator * right.Denominator, left.Denominator * right.Numerator);
    }
}
