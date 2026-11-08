using System.Text.RegularExpressions;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Imports;

// Conservative extraction only. All original lines remain available during explicit review.
public static class ImportedIngredientParser
{
    private static readonly Regex Number = new(@"^(?<q>\d+\s+\d+/\d+|\d+/\d+|\d+(?:\.\d+)?)(?<rest>.*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private const string AmountPattern = @"(?:\d+\s+\d+/\d+|\d+/\d+|\d+(?:\.\d+)?)";
    private static readonly Regex Range = new(@"^(?<q>" + AmountPattern + @"\s*(?:[-–—]|to\b)\s*" + AmountPattern + @")(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Alternate = new(@"^/\s*(?<amount>" + AmountPattern + @"(?:\s*(?:[-–—]|to\b)\s*" + AmountPattern + @")?)\s*(?<unit>[a-zA-Z]+\.?)(?=\s|$)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Word = new(@"^[a-zA-Z]+\.?", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex UnknownSuffix = new(@"\b(to taste|as needed|as required|for serving)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Dictionary<string, IngredientUnit> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g"] = IngredientUnit.Gram,
        ["gram"] = IngredientUnit.Gram,
        ["grams"] = IngredientUnit.Gram,
        ["kg"] = IngredientUnit.Kilogram,
        ["kilogram"] = IngredientUnit.Kilogram,
        ["kilograms"] = IngredientUnit.Kilogram,
        ["ml"] = IngredientUnit.Millilitre,
        ["millilitre"] = IngredientUnit.Millilitre,
        ["millilitres"] = IngredientUnit.Millilitre,
        ["milliliter"] = IngredientUnit.Millilitre,
        ["milliliters"] = IngredientUnit.Millilitre,
        ["l"] = IngredientUnit.Litre,
        ["litre"] = IngredientUnit.Litre,
        ["litres"] = IngredientUnit.Litre,
        ["liter"] = IngredientUnit.Litre,
        ["liters"] = IngredientUnit.Litre,
        ["tsp"] = IngredientUnit.Teaspoon,
        ["teaspoon"] = IngredientUnit.Teaspoon,
        ["teaspoons"] = IngredientUnit.Teaspoon,
        ["tbsp"] = IngredientUnit.Tablespoon,
        ["tablespoon"] = IngredientUnit.Tablespoon,
        ["tablespoons"] = IngredientUnit.Tablespoon,
        ["cup"] = IngredientUnit.Cup,
        ["cups"] = IngredientUnit.Cup,
        ["item"] = IngredientUnit.Item,
        ["items"] = IngredientUnit.Item
    };
    private static readonly HashSet<string> UnsupportedUnits = new(StringComparer.OrdinalIgnoreCase)
        { "t", "c", "x", "oz", "ounce", "ounces", "lb", "lbs", "pound", "pounds", "pint", "pints", "quart", "quarts", "gallon", "gallons", "pinch", "pinches", "dash", "dashes" };
    public static ImportedIngredient Parse(string original, string? group = null)
    {
        if (string.IsNullOrWhiteSpace(original) || original.Length > 20000) throw new ImportFailureException("import_invalid_document");
        var input = original.Trim();
        foreach (var (glyph, fraction) in new[] { ("½", "1/2"), ("¼", "1/4"), ("¾", "3/4"), ("⅓", "1/3"), ("⅔", "2/3"), ("⅛", "1/8"), ("⅜", "3/8"), ("⅝", "5/8"), ("⅞", "7/8") })
            input = input.Replace(glyph, " " + fraction, StringComparison.Ordinal);
        input = input.Trim();
        var range = Range.Match(input);
        var match = range.Success ? range : Number.Match(input);
        if (!match.Success) return Unknown(original, group);
        var quantity = Regex.Replace(match.Groups["q"].Value, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!range.Success)
        {
            try { _ = Quantity.Parse(quantity); }
            catch (DomainValidationException) { return Unknown(original, group); }
        }
        var tail = match.Groups["rest"].Value;
        if (tail.Length == 0 || (!char.IsWhiteSpace(tail[0]) && !char.IsLetter(tail[0]))) return Unknown(original, group);
        var separated = char.IsWhiteSpace(tail[0]); tail = tail.TrimStart();
        var word = Word.Match(tail); var token = word.Value.TrimEnd('.');
        IngredientUnit? unit; string? originalUnit = null;
        if (word.Success && Units.TryGetValue(token, out var supported))
        {
            unit = supported; originalUnit = word.Value; tail = tail[word.Length..].TrimStart();
        }
        else if (word.Success && UnsupportedUnits.Contains(token))
        {
            unit = null; originalUnit = word.Value; tail = tail[word.Length..].TrimStart();
        }
        else if (separated) unit = IngredientUnit.Item;
        else return Unknown(original, group);
        string? alternative = null;
        var alternate = Alternate.Match(tail);
        if (originalUnit is not null && alternate.Success &&
            (Units.ContainsKey(alternate.Groups["unit"].Value.TrimEnd('.')) || UnsupportedUnits.Contains(alternate.Groups["unit"].Value.TrimEnd('.'))))
        {
            alternative = "Alternative measure: " + alternate.Groups["amount"].Value + " " + alternate.Groups["unit"].Value;
            tail = tail[alternate.Length..].TrimStart();
        }
        if (string.IsNullOrWhiteSpace(tail)) return Unknown(original, group);
        var (name, preparation) = NameAndPreparation(tail);
        if (string.IsNullOrWhiteSpace(name)) return Unknown(original, group);
        var uncertain = range.Success || unit is null || UnknownSuffix.IsMatch(tail) || tail.Contains("plus ", StringComparison.OrdinalIgnoreCase);
        return new(original, name, quantity, range.Success ? QuantityKind.Unknown : QuantityKind.Numeric,
            unit, originalUnit, Join(preparation, alternative), group, uncertain);
    }
    private static ImportedIngredient Unknown(string original, string? group)
    {
        var marker = UnknownSuffix.Match(original);
        var (name, preparation) = NameAndPreparation(original.Trim());
        var nameMarker = UnknownSuffix.Match(name);
        if (nameMarker.Success && nameMarker.Index > 0)
        {
            preparation = Join(name[nameMarker.Index..].Trim(' ', ','), preparation);
            name = name[..nameMarker.Index].Trim(' ', ',');
        }
        return new(original, name, marker.Success ? marker.Value : "unspecified", QuantityKind.Unknown, null, null, preparation, group, true);
    }
    private static (string Name, string? Preparation) NameAndPreparation(string value)
    {
        string? notes = null;
        if (value.EndsWith(')'))
        {
            var depth = 0;
            for (var i = value.Length - 1; i >= 0; i--)
            {
                if (value[i] == ')') depth++;
                else if (value[i] == '(' && --depth == 0)
                {
                    if (i > 0) { notes = TrimWrapping(value[i..]); value = value[..i].TrimEnd(' ', ','); }
                    break;
                }
            }
        }
        var level = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') level++;
            else if (value[i] == ')') level--;
            else if (value[i] == ',' && level == 0)
                return (value[..i].Trim(), Join(value[(i + 1)..].Trim(), notes));
        }
        return (value.Trim(), notes);
    }
    private static string TrimWrapping(string value)
    {
        value = value.Trim(' ', ',');
        while (value.StartsWith('(') && value.EndsWith(')'))
        {
            var depth = 0; var wraps = true;
            for (var i = 0; i < value.Length - 1; i++)
            {
                if (value[i] == '(') depth++;
                else if (value[i] == ')' && --depth == 0) { wraps = false; break; }
            }
            if (!wraps) break;
            value = value[1..^1].Trim(' ', ',');
        }
        return value;
    }
    private static string? Join(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ? second : string.IsNullOrWhiteSpace(second) ? first : first + "; " + second;
}
