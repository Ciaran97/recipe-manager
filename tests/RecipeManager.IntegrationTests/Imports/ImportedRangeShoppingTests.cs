using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.Imports;

public sealed class ImportedRangeShoppingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Reviewed_import_range_stays_visible_and_unresolved_when_servings_change()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var ingredient = await RecipeContractTests.Ingredient(scenario.Admin, "Noodles");
        var parsed = ImportedIngredientParser.Parse("200 - 250 g / 7 - 9 oz noodles");
        Assert.Equal(RecipeManager.Core.Modules.Ingredients.Contracts.QuantityKind.Unknown, parsed.Kind);
        var created = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/recipes",
            RecipeContractTests.RecipeWrite(ingredient, parsed.QuantityText, "unknown", true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipe = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = recipe.GetProperty("id").GetGuid();
        var preview = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/recipes/{id}/serving-preview", new { targetServings = "8" });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var row = (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ingredients")[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("exact").ValueKind);
        Assert.Contains("200 - 250", row.GetProperty("displayText").GetString());
        var generated = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/shopping-lists/generate",
            new { name = "Reviewed range", selection = new { kind = "recipes", entries = new[] { new { recipeId = id, servings = "8" } } }, basis = "planned" });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        var item = (await generated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        Assert.True(item.GetProperty("unresolved").GetBoolean());
        Assert.Equal("200 - 250", item.GetProperty("displayText").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("effectiveExact").ValueKind);
    }
}
