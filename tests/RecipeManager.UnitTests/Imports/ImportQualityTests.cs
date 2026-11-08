using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Infrastructure.Imports;

namespace RecipeManager.UnitTests.Imports;

public sealed class ImportQualityTests
{
    [Theory]
    [InlineData("500 g / 1.1 lb mushrooms (, sliced)", "mushrooms", "500", IngredientUnit.Gram, "sliced")]
    [InlineData("40 g / 2 tbsp butter", "butter", "40", IngredientUnit.Gram, "2 tbsp")]
    [InlineData("1/2 tsp salt ((halve for fine salt, increase for flakes))", "salt", "1/2", IngredientUnit.Teaspoon, "halve for fine salt, increase for flakes")]
    [InlineData("2 tbsp oil (, divided)", "oil", "2", IngredientUnit.Tablespoon, "divided")]
    public void Alternate_measures_and_balanced_notes_do_not_pollute_ingredient_names(string input, string name, string quantity, IngredientUnit unit, string preparation)
    {
        var row = ImportedIngredientParser.Parse(input);
        Assert.Equal(name, row.IngredientName); Assert.Equal(quantity, row.QuantityText);
        Assert.Equal(unit, row.Unit); Assert.Contains(preparation, row.Preparation);
        Assert.Equal(input, row.OriginalText);
    }

    [Theory]
    [InlineData("200 - 250 g / 7 - 9 oz noodles ((Note 2))", "noodles", "200 - 250", IngredientUnit.Gram)]
    [InlineData("1–2 tbsp sugar", "sugar", "1–2", IngredientUnit.Tablespoon)]
    public void Ranges_remain_unknown_instead_of_becoming_a_definite_lower_bound(string input, string name, string quantity, IngredientUnit unit)
    {
        var row = ImportedIngredientParser.Parse(input);
        Assert.Equal(QuantityKind.Unknown, row.Kind); Assert.True(row.Uncertain);
        Assert.Equal(name, row.IngredientName); Assert.Equal(quantity, row.QuantityText); Assert.Equal(unit, row.Unit);
    }

    [Theory]
    [InlineData("Salt and pepper (, to taste)", "Salt and pepper", "to taste")]
    [InlineData("Chopped herbs (, for garnish (optional))", "Chopped herbs", "unspecified")]
    public void Unquantified_ingredients_keep_their_names_separate_from_preparation(string input, string name, string quantity)
    {
        var row = ImportedIngredientParser.Parse(input);
        Assert.Equal(name, row.IngredientName); Assert.Equal(quantity, row.QuantityText);
        Assert.NotNull(row.Preparation); Assert.Equal(QuantityKind.Unknown, row.Kind); Assert.True(row.Uncertain);
    }

    [Fact]
    public async Task Matching_recipe_card_preserves_groups_notes_and_semantic_fields_without_changing_ordered_steps()
    {
        const string html = """
            <script type="application/ld+json">{"@type":"Recipe","name":"Test stew","recipeYield":"2",
            "recipeIngredient":["500 g / 1.1 lb mushrooms (, sliced)","Salt (, to taste)"],
            "recipeInstructions":[{"@type":"HowToStep","text":"Cook gently. See note 1."},{"@type":"HowToStep","text":"Serve warm."}]}</script>
            <div class="wprm-recipe-container"><h2 class="wprm-recipe-name">Test stew</h2>
              <div class="wprm-recipe-ingredient-group"><h4 class="wprm-recipe-ingredient-group-name">Main:</h4>
                <ul><li class="wprm-recipe-ingredient"><span class="wprm-recipe-ingredient-amount">500 g / 1.1 lb</span>
                  <span class="wprm-recipe-ingredient-name">mushrooms</span><span class="wprm-recipe-ingredient-notes">sliced</span></li></ul></div>
              <div class="wprm-recipe-ingredient-group"><h4 class="wprm-recipe-ingredient-group-name">Finish:</h4>
                <ul><li class="wprm-recipe-ingredient"><span class="wprm-recipe-ingredient-name">Salt</span>
                  <span class="wprm-recipe-ingredient-notes">to taste</span></li></ul></div>
              <div class="wprm-recipe-notes"><p>1. Keep the heat low.</p><p>2. Rest before serving.</p></div>
            </div>
            """;
        var recipe = Assert.Single((await WebsiteRecipeImporter.ExtractAsync(html, new("https://example.org/stew"), CancellationToken.None)).Recipes);
        Assert.Equal(new[] { "Main", "Finish" }, recipe.Ingredients.Select(x => x.Group));
        Assert.Equal("mushrooms", recipe.Ingredients[0].IngredientName); Assert.Contains("sliced", recipe.Ingredients[0].Preparation);
        Assert.Contains("1. Keep the heat low.\n", recipe.Notes); Assert.Contains("2. Rest before serving.", recipe.Notes);
        Assert.Equal(new[] { "Cook gently. See note 1.", "Serve warm." }, recipe.Instructions.Select(x => x.Text));
    }
    [Theory]
    [InlineData("Different title", "mushrooms")]
    [InlineData("Test stew", "rice")]
    public async Task Unrelated_or_misaligned_cards_cannot_attach_groups_to_structured_quantities(string cardTitle, string name)
    {
        var html = """
            <script type="application/ld+json">{"@type":"Recipe","name":"Test stew","recipeIngredient":["500 g mushrooms"]}</script>
            """ + $"<div class=\"wprm-recipe-container\"><h2 class=\"wprm-recipe-name\">{cardTitle}</h2>" +
            $"<div class=\"wprm-recipe-ingredient-group\"><h4 class=\"wprm-recipe-ingredient-group-name\">Wrong group</h4>" +
            $"<li class=\"wprm-recipe-ingredient\"><span class=\"wprm-recipe-ingredient-name\">{name}</span></li></div></div>";
        var recipe = Assert.Single((await WebsiteRecipeImporter.ExtractAsync(html, new("https://example.org/stew"), CancellationToken.None)).Recipes);
        var ingredient = Assert.Single(recipe.Ingredients);
        Assert.Equal("mushrooms", ingredient.IngredientName); Assert.Equal("500", ingredient.QuantityText);
        Assert.Null(ingredient.Group);
    }

}
