using System.Text.Json;

namespace RecipeManager.IntegrationTests.Contracts;

public sealed class PlannerSchemaTests
{
    private static JsonDocument Document()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RecipeManager.slnx"))) root = root.Parent;
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "specs/001-household-recipe-manager/contracts/openapi.json")));
    }
    [Theory]
    [InlineData("ImportedIngredient", "unit")]
    [InlineData("ImportedRecipe", "measurementStandard")]
    [InlineData("ShoppingItemRead", "dimension")]
    [InlineData("ShoppingPreviewWrite", "basis")]
    public void Nullable_enums_are_typed_and_accept_null_without_dropping_native_fields(string schema, string property)
    {
        using var document = Document();
        var value = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema).GetProperty("properties").GetProperty(property);
        Assert.Contains(value.GetProperty("type").EnumerateArray(), x => x.GetString() == "string");
        Assert.Contains(value.GetProperty("type").EnumerateArray(), x => x.GetString() == "null");
        Assert.Contains(value.GetProperty("enum").EnumerateArray(), x => x.ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData("ImportedRecipe", "defaultServings")]
    [InlineData("CookingEventRead", "actualPrepared")]
    [InlineData("CookingEventRead", "actualEaten")]
    [InlineData("DeleteMealWrite", "sourceRevision")]
    [InlineData("CookingWrite", "expectedRecipeRevision")]
    [InlineData("DependentResolution", "consumedPortions")]
    [InlineData("DependentResolution", "reservedRemainder")]
    public void Optional_quantities_and_revisions_remain_nullable_strings_for_both_clients(string schema, string property)
    {
        using var document = Document();
        var value = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema).GetProperty("properties").GetProperty(property);
        var types = value.GetProperty("type").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("string", types); Assert.Contains("null", types);
        Assert.False(value.TryGetProperty("format", out var format) && format.GetString() == "double");
    }
    [Theory]
    [InlineData("/api/v1/meals/{id}", "DeleteMealWrite")]
    [InlineData("/api/v1/cooking-events/{id}/actuals", "ClearActualsWrite")]
    public void Optional_deletion_bodies_retain_their_typed_explicit_confirmation_and_resolution_payload(string path, string schema)
    {
        using var document = Document();
        var body = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("delete").GetProperty("requestBody");
        Assert.False(body.TryGetProperty("required", out var required) && required.GetBoolean());
        Assert.Equal("#/components/schemas/" + schema, body.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
    }
}
