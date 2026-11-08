using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using AngleSharp.Html.Parser;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Infrastructure.Imports;

public sealed class WebsiteRecipeImporter(PublicWebsiteFetcher fetcher) : IWebsiteRecipeProvider
{
    public async Task<ImportExtraction> ImportAsync(string url, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var page = await fetcher.FetchAsync(url, deadline.Token);
            var result = await ExtractAsync(page.Content, page.FinalUri, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ImportFailureException("import_timeout"); }
    }
    public static async Task<ImportExtraction> ExtractAsync(string html, Uri source, CancellationToken ct)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(html) > 2 * 1024 * 1024) throw new ImportFailureException("import_limit");
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, ct);
        var roots = new List<JsonDocument>();
        try
        {
            if (html.AsSpan().TrimStart().StartsWith("{".AsSpan(), StringComparison.Ordinal) || html.AsSpan().TrimStart().StartsWith("[".AsSpan(), StringComparison.Ordinal))
                TryDocument(html, roots);
            else
                foreach (var script in document.QuerySelectorAll("script"))
                {
                    ct.ThrowIfCancellationRequested();
                    if (script.GetAttribute("type")?.Split(';')[0].Trim().Equals("application/ld+json", StringComparison.OrdinalIgnoreCase) == true)
                        TryDocument(script.TextContent, roots);
                }
            var candidates = new List<ImportedRecipe>(); var rows = 0;
            foreach (var root in roots)
                foreach (var node in RecipeNodes(root.RootElement))
                {
                    ct.ThrowIfCancellationRequested();
                    if (candidates.Count >= 20) throw new ImportFailureException("import_limit");
                    var recipe = ReadRecipe(node, source);
                    rows += recipe.Ingredients.Count + recipe.Instructions.Count;
                    if (rows > 1000) throw new ImportFailureException("import_limit");
                    candidates.Add(recipe);
                }
            if (candidates.Count == 0) throw new ImportFailureException("import_no_recipe");
            var enriched = candidates.Select(recipe => candidates.Count(other => string.Equals(other.Title, recipe.Title, StringComparison.OrdinalIgnoreCase)) == 1
                ? RecipeCardMarkup.Enrich(document, recipe) : recipe).ToArray();
            return new(enriched);
        }
        finally { foreach (var root in roots) root.Dispose(); }
    }
    private static void TryDocument(string text, List<JsonDocument> roots)
    {
        try { roots.Add(JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 })); }
        catch (JsonException) { /* An invalid script does not hide a valid independent recipe block. */ }
    }
    private static IEnumerable<JsonElement> RecipeNodes(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in node.EnumerateArray()) foreach (var match in RecipeNodes(entry)) yield return match;
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("@type", out var type) && Strings(type).Any(x => x is "Recipe" or "https://schema.org/Recipe" or "http://schema.org/Recipe")) yield return node;
            else foreach (var property in node.EnumerateObject())
                if (property.Name != "@context") foreach (var match in RecipeNodes(property.Value)) yield return match;
        }
    }
    private static ImportedRecipe ReadRecipe(JsonElement node, Uri source)
    {
        var ingredients = new List<ImportedIngredient>();
        if (node.TryGetProperty("recipeIngredient", out var supplied))
            foreach (var raw in IngredientLines(supplied))
            {
                if (ingredients.Count >= 1000) throw new ImportFailureException("import_limit");
                var row = ImportedIngredientParser.Parse(Plain(raw.Text));
                if (raw.StructuredName is { } structured)
                    row = row with
                    {
                        IngredientName = structured,
                        Unit = row.OriginalUnit is null ? null : row.Unit,
                        OriginalUnit = raw.SourceUnit,
                        Uncertain = row.Uncertain || row.OriginalUnit is null
                    };
                ingredients.Add(row);
            }
        var instructions = new List<ImportedInstruction>();
        if (node.TryGetProperty("recipeInstructions", out var steps)) ReadInstructions(steps, null, instructions);
        var title = Text(node, "name"); var servings = Yield(node); var standard = Standard(node);
        var flags = ImportReviewFields.For(title, servings, standard, ingredients, instructions);
        var image = Image(node, source);
        var tags = node.TryGetProperty("keywords", out var keywords) ? Strings(keywords).SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Select(Plain).Take(100).ToArray() : [];
        return new(title, Text(node, "description"), servings, Minutes(node, "prepTime"), Minutes(node, "cookTime"), ingredients, instructions,
            null, source.AbsoluteUri, image, standard, tags, flags);
    }
    private static IEnumerable<(string Text, string? StructuredName, string? SourceUnit)> IngredientLines(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String) yield return (node.GetString()!, null, null);
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in node.EnumerateArray()) foreach (var line in IngredientLines(entry)) yield return line;
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("itemListElement", out var entries))
            {
                foreach (var line in IngredientLines(entries)) yield return line;
            }
            else if (Text(node, "text") is { } text) yield return (text, null, null);
            else if (Text(node, "name") is { } name)
            {
                var value = node.TryGetProperty("value", out var amount) ? Scalar(amount) : null;
                var sourceUnit = Text(node, "unitText") ?? Text(node, "unitCode");
                var unit = sourceUnit?.ToUpperInvariant() switch
                {
                    "GRM" => "g",
                    "KGM" => "kg",
                    "MLT" => "ml",
                    "LTR" => "l",
                    "C62" => "items",
                    _ => sourceUnit
                };
                yield return (string.Join(' ', new[] { value, unit, name }.Where(x => !string.IsNullOrWhiteSpace(x))), value is not null ? name : null, sourceUnit);
            }
        }
    }
    private static void ReadInstructions(JsonElement node, string? group, List<ImportedInstruction> result)
    {
        if (result.Count > 1000) throw new ImportFailureException("import_limit");
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in node.EnumerateArray()) ReadInstructions(entry, group, result);
        }
        else if (node.ValueKind == JsonValueKind.String)
        {
            var document = new HtmlParser().ParseDocument(node.GetString()!);
            var items = document.QuerySelectorAll("li");
            var lines = items.Length > 0 ? items.Select(x => x.TextContent) : Plain(node.GetString()!).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (result.Count >= 1000) throw new ImportFailureException("import_limit");
                if (!string.IsNullOrWhiteSpace(line)) result.Add(new(Plain(line), group));
            }
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("itemListElement", out var entries)) ReadInstructions(entries, Text(node, "name") ?? group, result);
            else if (Text(node, "text") is { } text) result.Add(new(text, group));
            else if (Text(node, "name") is { } name) result.Add(new(name, group));
        }
    }
    private static IEnumerable<string> Strings(JsonElement node)
        => node.ValueKind == JsonValueKind.Array ? node.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
            : node.ValueKind == JsonValueKind.String ? [node.GetString()!] : [];
    private static string? Scalar(JsonElement node) => node.ValueKind switch { JsonValueKind.String => node.GetString(), JsonValueKind.Number => node.GetRawText(), _ => null };
    private static string? Text(JsonElement node, string field)
        => node.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? Plain(value.GetString()!) : null;
    private static string Plain(string value)
    {
        if (value.Length > 20000) throw new ImportFailureException("import_limit");
        if (!value.Contains('<')) return System.Net.WebUtility.HtmlDecode(value).Trim();
        var document = new HtmlParser().ParseDocument(value);
        foreach (var script in document.QuerySelectorAll("script,style")) script.Remove();
        return document.Body?.TextContent.Trim() ?? "";
    }
    private static decimal? Yield(JsonElement node)
    {
        if (!node.TryGetProperty("recipeYield", out var value)) return null;
        var raw = Scalar(value) ?? (value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(Scalar).FirstOrDefault(x => x is not null) : null);
        if (raw is null && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var number)) raw = Scalar(number);
        if (raw is null) return null;
        var match = Regex.Match(raw.Trim(), @"^(?<n>\d+(?:\.\d+)?)\s*(?:servings?|portions?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || !decimal.TryParse(match.Groups["n"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var servings)) return null;
        try { return PortionQuantity.Validate(servings); } catch (DomainValidationException) { return null; }
    }
    private static int? Minutes(JsonElement node, string field)
    {
        if (Text(node, field) is not { } duration) return null;
        try
        {
            var minutes = XmlConvert.ToTimeSpan(duration).TotalMinutes;
            return minutes >= 0 && minutes <= int.MaxValue && minutes == Math.Truncate(minutes) ? (int)minutes : null;
        }
        catch (FormatException) { return null; }
        catch (OverflowException) { return null; }
    }
    private static MeasurementStandard? Standard(JsonElement node)
        => Text(node, "measurementStandard")?.ToUpperInvariant() switch { "AU" => MeasurementStandard.AU, "US" => MeasurementStandard.US, _ => null };
    private static string? Image(JsonElement node, Uri source)
    {
        if (!node.TryGetProperty("image", out var image)) return null;
        if (image.ValueKind == JsonValueKind.Array) image = image.EnumerateArray().FirstOrDefault();
        var raw = Scalar(image) ?? (image.ValueKind == JsonValueKind.Object ? Text(image, "url") ?? Text(image, "contentUrl") : null);
        return raw is not null && Uri.TryCreate(source, raw, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0 ? url.AbsoluteUri : null;
    }
}
