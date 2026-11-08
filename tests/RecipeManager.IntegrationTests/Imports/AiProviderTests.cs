using System.Net;
using System.Text;
using System.Text.Json;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Infrastructure.Imports;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Imports;

public sealed class AiProviderTests
{
    internal const string Input = "Soup\nServes 2\n1/2 cup rice\nsalt to taste\nBoil rice.";
    internal static string Document(string ingredient = "1/2 cup rice") => JsonSerializer.Serialize(new
    {
        recipes = new[] { new {
            title = "Soup", description = (string?)null, servingsText = "Serves 2", prepTimeText = (string?)null, cookTimeText = (string?)null,
            notes = (string?)null, measurementStandard = (string?)null, measurementStandardEvidence = (string?)null,
            ingredients = new[] { new { sourceText = ingredient, group = (string?)null }, new { sourceText = "salt to taste", group = (string?)null } },
            instructions = new[] { new { sourceText = "Boil rice.", group = (string?)null } }
        } }
    });
    private static AiRecipeProviderConfiguration Config(bool enabled = true) => new(enabled, "http://localhost:11434/v1", "configured-model", "test-provider-key");
    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = json } } } }), Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task Compatible_provider_uses_only_configured_endpoint_model_key_and_original_user_text_without_tools()
    {
        var calls = 0;
        using var client = new HttpClient(new ScriptedHttpHandler(async (request, ct) =>
        {
            calls++; Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("test-provider-key", request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("configured-model", body.RootElement.GetProperty("model").GetString());
            Assert.Equal(Input, body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            Assert.False(body.RootElement.TryGetProperty("tools", out _));
            return Response(Document());
        }));
        var result = await new AiTextImporter(new OpenAiCompatibleRecipeProvider(client)).ImportAsync(Input, Config(), CancellationToken.None);
        Assert.Equal(1, calls); var recipe = Assert.Single(result.Recipes);
        Assert.Equal(2m, recipe.DefaultServings); Assert.Null(recipe.MeasurementStandard);
        Assert.Equal("1/2", recipe.Ingredients[0].QuantityText); Assert.Equal(IngredientUnit.Cup, recipe.Ingredients[0].Unit);
        Assert.Equal(QuantityKind.Unknown, recipe.Ingredients[1].Kind); Assert.Contains("ingredients[1].quantity", recipe.UncertaintyFields);
    }

    [Fact]
    public async Task Fake_provider_uses_same_validation_and_invented_source_quantities_are_rejected()
    {
        var good = await new AiTextImporter(new FakeAiRecipeProvider(Document())).ImportAsync(Input, Config(), CancellationToken.None);
        Assert.Equal("salt to taste", good.Recipes[0].Ingredients[1].OriginalText);
        var bad = await Assert.ThrowsAsync<ImportFailureException>(() => new AiTextImporter(new FakeAiRecipeProvider(Document("2 tsp salt"))).ImportAsync(Input, Config(), CancellationToken.None));
        Assert.Equal("import_provider_invalid_response", bad.Code);
    }

    [Fact]
    public async Task Disabled_ai_oversized_text_failed_or_oversized_responses_never_create_an_unvalidated_result()
    {
        var calls = 0;
        using var client = new HttpClient(new ScriptedHttpHandler((_, _) => { calls++; return Task.FromResult(Response(Document())); }));
        var importer = new AiTextImporter(new OpenAiCompatibleRecipeProvider(client));
        Assert.Equal("import_ai_disabled", (await Assert.ThrowsAsync<ImportFailureException>(() => importer.ImportAsync(Input, Config(false), CancellationToken.None))).Code);
        await Assert.ThrowsAsync<RecipeManager.Core.Shared.DomainValidationException>(() => importer.ImportAsync(new string('é', 51201), Config(), CancellationToken.None));
        Assert.Equal(0, calls);
        using var failedClient = new HttpClient(new ScriptedHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("private upstream credential details") })));
        var failed = await Assert.ThrowsAsync<ImportFailureException>(() => new AiTextImporter(new OpenAiCompatibleRecipeProvider(failedClient)).ImportAsync(Input, Config(), CancellationToken.None));
        Assert.Equal("import_provider_failed", failed.Code); Assert.DoesNotContain("credential", failed.Message);
        using var hugeClient = new HttpClient(new ScriptedHttpHandler((_, _) => Task.FromResult(Response(new string('x', 1024 * 1024 + 1)))));
        Assert.Equal("import_limit", (await Assert.ThrowsAsync<ImportFailureException>(() => new AiTextImporter(new OpenAiCompatibleRecipeProvider(hugeClient)).ImportAsync(Input, Config(), CancellationToken.None))).Code);
    }

    [Theory]
    [InlineData("{\"recipes\":true}")]
    [InlineData("{\"recipes\":[]}")]
    [InlineData("not json")]
    [InlineData("{\"recipes\":[],\"executeTool\":\"write recipe\"}")]
    public async Task Invalid_provider_schema_is_rejected(string document)
        => Assert.Equal("import_provider_invalid_response", (await Assert.ThrowsAsync<ImportFailureException>(() => new AiTextImporter(new FakeAiRecipeProvider(document)).ImportAsync(Input, Config(), CancellationToken.None))).Code);
}
