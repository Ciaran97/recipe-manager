using System.Net;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Infrastructure.Imports;

namespace RecipeManager.UnitTests.Imports;

public sealed class ImporterTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.2.3")]
    [InlineData("192.168.0.2")]
    [InlineData("169.254.169.254")]
    [InlineData("100.100.100.200")]
    [InlineData("168.63.129.16")]
    [InlineData("0.0.0.0")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.2.2")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("3fff::1")]
    [InlineData("ff02::1")]
    public void Non_public_and_transition_addresses_are_denied(string value)
        => Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse(value)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700:4700::1111")]
    public void Public_unicast_addresses_are_allowed(string value)
        => Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse(value)));

    [Theory]
    [InlineData("1/2 cup rice", "1/2", IngredientUnit.Cup, "rice")]
    [InlineData("1 1/2 tablespoons oil, divided", "1 1/2", IngredientUnit.Tablespoon, "oil")]
    [InlineData("500g beef mince", "500", IngredientUnit.Gram, "beef mince")]
    [InlineData("0.2 kg beef strips", "0.2", IngredientUnit.Kilogram, "beef strips")]
    [InlineData("2 eggs", "2", IngredientUnit.Item, "eggs")]
    public void Supported_quantities_are_structured_without_conflating_ingredients(string raw, string quantity, IngredientUnit unit, string name)
    {
        var row = ImportedIngredientParser.Parse(raw);
        Assert.Equal(raw, row.OriginalText); Assert.Equal(quantity, row.QuantityText);
        Assert.Equal(unit, row.Unit); Assert.Equal(name, row.IngredientName);
        Assert.Equal(QuantityKind.Numeric, row.Kind);
    }

    [Theory]
    [InlineData("salt to taste")]
    [InlineData("1-2 tbsp sugar")]
    [InlineData("about 2 cups flour")]
    [InlineData("2% milk")]
    public void Ambiguous_quantities_remain_visible_and_uncertain(string raw)
    {
        var row = ImportedIngredientParser.Parse(raw);
        Assert.Equal(raw, row.OriginalText); Assert.Equal(QuantityKind.Unknown, row.Kind);
        Assert.True(row.Uncertain); Assert.False(string.IsNullOrWhiteSpace(row.QuantityText));
    }

    [Fact]
    public async Task Json_ld_graph_multiple_recipes_sections_and_unknowns_are_extracted_without_guessing_source_standard()
    {
        const string html = """
            <html><script type="application/ld+json">invalid json</script>
            <script type="application/ld+json">{"@context":"https://schema.org","@graph":[
            {"@type":"Recipe","name":"Rice &amp; eggs","recipeCuisine":"Australian","recipeYield":"2 servings",
             "prepTime":"PT10M","cookTime":"PT1H","image":"http://169.254.169.254/image",
             "recipeIngredient":["1/2 cup rice","2 eggs","salt to taste"],
             "recipeInstructions":[{"@type":"HowToSection","name":"Cook","itemListElement":[{"@type":"HowToStep","text":"Boil rice."}]}]},
            {"@type":["Thing","Recipe"],"name":"Second recipe","recipeIngredient":["1 tbsp oil"]}
            ]}</script><img src="http://127.0.0.1/private"></html>
            """;
        var result = await WebsiteRecipeImporter.ExtractAsync(html, new Uri("https://example.org/recipe"), CancellationToken.None);
        Assert.Equal(2, result.Recipes.Count);
        var first = result.Recipes[0]; Assert.Equal("Rice & eggs", first.Title);
        Assert.Equal(2m, first.DefaultServings); Assert.Equal(10, first.PrepMinutes); Assert.Equal(60, first.CookMinutes);
        Assert.Null(first.MeasurementStandard);
        Assert.Equal(QuantityKind.Unknown, first.Ingredients[2].Kind);
        Assert.Equal("Boil rice.", Assert.Single(first.Instructions).Text);
        Assert.Equal("Cook", first.Instructions[0].Group);
        Assert.Contains("measurementStandard", first.UncertaintyFields);
        Assert.Contains("ingredients[2].quantity", first.UncertaintyFields);
    }

    [Fact]
    public async Task Missing_yield_is_not_invented_and_oversized_row_batches_are_rejected()
    {
        var result = await WebsiteRecipeImporter.ExtractAsync("""<script type="application/ld+json">{"@type":"Recipe","name":"Soup"}</script>""", new("https://example.org/"), CancellationToken.None);
        Assert.Null(result.Recipes[0].DefaultServings);
        var rows = string.Join(',', Enumerable.Repeat("\"1 g salt\"", 1001));
        var error = await Assert.ThrowsAsync<ImportFailureException>(() => WebsiteRecipeImporter.ExtractAsync("<script type=\"application/ld+json\">{\"@type\":\"Recipe\",\"recipeIngredient\":[" + rows + "]}</script>", new("https://example.org/"), CancellationToken.None));
        Assert.Equal("import_limit", error.Code);
    }
    [Fact]
    public async Task Structured_source_units_are_preserved_and_missing_units_are_not_invented()
    {
        const string html = """<script type="application/ld+json">{"@type":"Recipe","name":"Structured","recipeIngredient":[{"@type":"PropertyValue","name":"rice","value":150,"unitCode":"GRM"},{"name":"sugar","value":2,"unitText":"fluid ounces"},{"name":"flour","value":200}]}</script>""";
        var recipe = (await WebsiteRecipeImporter.ExtractAsync(html, new("https://example.org/"), CancellationToken.None)).Recipes[0];
        Assert.Equal(IngredientUnit.Gram, recipe.Ingredients[0].Unit); Assert.Equal("GRM", recipe.Ingredients[0].OriginalUnit);
        Assert.Equal("rice", recipe.Ingredients[0].IngredientName);
        Assert.Null(recipe.Ingredients[1].Unit); Assert.Equal("sugar", recipe.Ingredients[1].IngredientName); Assert.True(recipe.Ingredients[1].Uncertain);
        Assert.Null(recipe.Ingredients[2].Unit); Assert.True(recipe.Ingredients[2].Uncertain);
    }

}
