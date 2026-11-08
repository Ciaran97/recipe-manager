using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Imports;

public static class AiRecipeResponseParser
{
    private sealed record SourceLine(string SourceText, string? Group);
    private sealed record RecipeDocument(string? Title, string? Description, string? ServingsText, string? PrepTimeText, string? CookTimeText,
        string? Notes, string? MeasurementStandard, string? MeasurementStandardEvidence, IReadOnlyList<SourceLine> Ingredients, IReadOnlyList<SourceLine> Instructions);
    private sealed record Document(IReadOnlyList<RecipeDocument> Recipes);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32
    };
    public static ImportExtraction Parse(string original, string json)
    {
        ImportInput.Validate(ImportSourceKind.Text, original);
        if (Encoding.UTF8.GetByteCount(json) > 1024 * 1024) throw new ImportFailureException("import_limit");
        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, Json);
            if (document?.Recipes is null || document.Recipes.Count is < 1 or > 20) throw Invalid();
            var output = new List<ImportedRecipe>(); var count = 0;
            foreach (var recipe in document.Recipes)
            {
                if (recipe is null || recipe.Ingredients is null || recipe.Instructions is null) throw Invalid();
                count += recipe.Ingredients.Count + recipe.Instructions.Count;
                if (count > 1000) throw new ImportFailureException("import_limit");
                var title = Evidence(original, recipe.Title); var description = Evidence(original, recipe.Description);
                var notes = Evidence(original, recipe.Notes);
                var servingsText = Evidence(original, recipe.ServingsText);
                var prepText = Evidence(original, recipe.PrepTimeText); var cookText = Evidence(original, recipe.CookTimeText);
                var standardEvidence = Evidence(original, recipe.MeasurementStandardEvidence);
                var ingredients = recipe.Ingredients.Select(line =>
                {
                    if (line is null || string.IsNullOrWhiteSpace(line.SourceText)) throw Invalid();
                    var source = Evidence(original, line.SourceText)!;
                    return ImportedIngredientParser.Parse(source, Evidence(original, line.Group));
                }).ToArray();
                var instructions = recipe.Instructions.Select(line =>
                {
                    if (line is null || string.IsNullOrWhiteSpace(line.SourceText)) throw Invalid();
                    return new ImportedInstruction(Evidence(original, line.SourceText)!, Evidence(original, line.Group));
                }).ToArray();
                var servings = Servings(servingsText); var standard = Standard(recipe.MeasurementStandard, standardEvidence);
                var prep = Minutes(prepText); var cook = Minutes(cookText);
                var flags = ImportReviewFields.For(title, servings, standard, ingredients, instructions).ToList();
                if (prepText is not null && prep is null) flags.Add("prepMinutes");
                if (cookText is not null && cook is null) flags.Add("cookMinutes");
                output.Add(new(title, description, servings, prep, cook, ingredients, instructions, notes, null, null, standard, [], flags));
            }
            return new(output);
        }
        catch (JsonException) { throw Invalid(); }
    }
    private static string? Evidence(string original, string? value)
    {
        if (value is null) return null;
        if (value.Length > 20000 || string.IsNullOrWhiteSpace(value) || !original.Contains(value, StringComparison.Ordinal)) throw Invalid();
        return value;
    }
    private static decimal? Servings(string? text)
    {
        if (text is null) return null;
        var match = Regex.Match(text.Trim(), @"^(?:(?:serves|servings|yield)\s*:?\s*(?<n>\d+(?:\.\d+)?)|(?<n>\d+(?:\.\d+)?)\s*(?:servings?|portions?))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || !decimal.TryParse(match.Groups["n"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        try { return PortionQuantity.Validate(value); } catch (DomainValidationException) { return null; }
    }
    private static int? Minutes(string? text)
    {
        if (text is null) return null;
        var match = Regex.Match(text.Trim(), @"^(?:(?:prep(?:aration)?|cook(?:ing)?)(?: time)?\s*:?\s*)?(?<n>\d+)\s*(?<u>minutes?|mins?|hours?|hrs?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return null;
        var value = (long)number * (match.Groups["u"].Value.StartsWith('h') || match.Groups["u"].Value.StartsWith('H') ? 60 : 1);
        return value <= int.MaxValue ? (int)value : null;
    }
    private static MeasurementStandard? Standard(string? claimed, string? evidence)
    {
        if (evidence is null) return null;
        if (claimed == "AU" && Regex.IsMatch(evidence, @"\b(?:Australian|AU) measures\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return MeasurementStandard.AU;
        if (claimed == "US" && Regex.IsMatch(evidence, @"\bUS (?:measures|customary)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return MeasurementStandard.US;
        return null;
    }
    private static ImportFailureException Invalid() => new("import_provider_invalid_response");
}
