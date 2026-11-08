using System.Globalization;
using System.Numerics;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Ingredients.Contracts;

/// <summary>Transport/snapshot representation; clients display it without performing domain arithmetic.</summary>
public sealed record ExactQuantity(string Numerator, string Denominator)
{
    public static ExactQuantity From(ExactAmount amount) => new(amount.Numerator.ToString(CultureInfo.InvariantCulture), amount.Denominator.ToString(CultureInfo.InvariantCulture));
    public ExactAmount ToAmount()
    {
        if (Numerator is not { Length: >= 1 and <= ExactAmount.MaximumDigits } || Denominator is not { Length: >= 1 and <= ExactAmount.MaximumDigits }
            || !BigInteger.TryParse(Numerator, NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
            || !BigInteger.TryParse(Denominator, NumberStyles.None, CultureInfo.InvariantCulture, out var denominator))
            throw new DomainValidationException("quantity", "calculation_limit", "Exact values require bounded nonnegative integer strings.");
        return new ExactAmount(numerator, denominator);
    }
}
