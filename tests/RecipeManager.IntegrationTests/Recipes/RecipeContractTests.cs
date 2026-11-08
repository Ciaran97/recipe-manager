using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;

namespace RecipeManager.IntegrationTests.Recipes;

public sealed class RecipeContractTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<ApiFixture> Fresh()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        return new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?> { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" });
    }
    private static HttpClient Client(ApiFixture host) => host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task Setup(HttpClient client) => Assert.Equal(HttpStatusCode.Created,
        (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);
    internal static object RecipeWrite(Guid ingredientId, string quantity = "1/3", string kind = "numeric", bool acknowledgeUncertainIngredients = false) => new
    {
        title = "Rice bowls",
        description = "A reusable recipe",
        defaultServings = "3",
        prepMinutes = 10,
        cookMinutes = 20,
        notes = "Keep the original",
        sourceUrl = "https://recipes.example.test/rice",
        measurementStandard = "AU",
        groups = Array.Empty<object>(),
        ingredients = new[] { new { position = 0, ingredientId, quantityText = quantity, kind, unit = "g", preparation = "rinsed", uncertain = kind == "unknown" } },
        instructions = new[] { new { position = 0, text = "Cook the rice." } },
        collectionIds = Array.Empty<Guid>(),
        tagIds = Array.Empty<Guid>(),
        acknowledgeUncertainIngredients
    };
    internal static async Task<Guid> Ingredient(HttpClient client, string name = "Rice")
    {
        var response = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/ingredients", new { canonicalName = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Manual_recipe_lifecycle_and_exact_preview_preserve_source_values()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var ingredientId = await Ingredient(client);
        var created = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", RecipeWrite(ingredientId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipe = await created.Content.ReadFromJsonAsync<JsonElement>();
        var path = "/api/v1/recipes/" + recipe.GetProperty("id").GetGuid();
        var preview = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/serving-preview", new { targetServings = "1" });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var scaled = await preview.Content.ReadFromJsonAsync<JsonElement>();
        var exact = scaled.GetProperty("ingredients")[0].GetProperty("exact");
        Assert.Equal("1", exact.GetProperty("numerator").GetString());
        Assert.Equal("9", exact.GetProperty("denominator").GetString());
        using var unchanged = await client.GetAsync(path);
        Assert.Equal(created.Headers.ETag, unchanged.Headers.ETag);
        Assert.Equal("1/3", (await unchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ingredients")[0].GetProperty("quantityText").GetString());
        var duplicate = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/duplicate", new { }, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        var copy = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(recipe.GetProperty("id").GetGuid(), copy.GetProperty("id").GetGuid());
        Assert.NotEqual(recipe.GetProperty("ingredients")[0].GetProperty("id").GetGuid(), copy.GetProperty("ingredients")[0].GetProperty("id").GetGuid());
        var archived = await HouseholdContractTests.Write(client, HttpMethod.Put, path + "/archive", new { archived = true }, unchanged.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.True((await archived.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("archived").GetBoolean());
        var active = await HouseholdContractTests.Write(client, HttpMethod.Put, path + "/archive", new { archived = false }, archived.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        var favourite = await HouseholdContractTests.Write(client, HttpMethod.Put, path + "/favourite", new { favourite = true }, active.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, favourite.StatusCode);
        Assert.True((await favourite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("favourite").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(client, HttpMethod.Delete, path, etag: favourite.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Unresolved_quantity_requires_acknowledgement_and_never_becomes_zero()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var ingredientId = await Ingredient(client, "Salt");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", RecipeWrite(ingredientId, "to taste", "unknown"))).StatusCode);
        var created = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", RecipeWrite(ingredientId, "to taste", "unknown", true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipe = await created.Content.ReadFromJsonAsync<JsonElement>();
        var line = recipe.GetProperty("ingredients")[0];
        Assert.Equal("unknown", line.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, line.GetProperty("exact").ValueKind);
        Assert.Equal("to taste", line.GetProperty("quantityText").GetString());
    }

    [Fact]
    public async Task Whole_recipe_edits_preserve_row_ids_and_order_and_reject_foreign_row_ids_atomically()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var ingredient = await Ingredient(client);
        var created = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", RecipeWrite(ingredient));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await created.Content.ReadFromJsonAsync<JsonElement>();
        var path = "/api/v1/recipes/" + original.GetProperty("id").GetGuid();
        var originalRow = original.GetProperty("ingredients")[0].GetProperty("id").GetGuid();
        var originalInstruction = original.GetProperty("instructions")[0].GetProperty("id").GetGuid();
        var group = Guid.NewGuid(); var newRow = Guid.NewGuid(); var newInstruction = Guid.NewGuid();
        var input = JsonSerializer.SerializeToNode(RecipeWrite(ingredient))!.AsObject();
        input["title"] = "Edited bowls";
        input["groups"] = JsonSerializer.SerializeToNode(new[] { new { id = group, title = "Base", position = 0 } });
        input["ingredients"] = JsonSerializer.SerializeToNode(new[]
        {
            new { id = originalRow, groupId = group, position = 1, ingredientId = ingredient, quantityText = "1/3", kind = "numeric", unit = "g" },
            new { id = newRow, groupId = group, position = 0, ingredientId = ingredient, quantityText = "0.5", kind = "numeric", unit = "g" }
        });
        input["instructions"] = JsonSerializer.SerializeToNode(new[]
        {
            new { id = originalInstruction, position = 1, text = "Cook." }, new { id = newInstruction, position = 0, text = "Rinse." }
        });
        var edited = await HouseholdContractTests.Write(client, HttpMethod.Put, path, input, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var saved = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { newRow, originalRow }, saved.GetProperty("ingredients").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { newInstruction, originalInstruction }, saved.GetProperty("instructions").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await HouseholdContractTests.Write(client, HttpMethod.Put, path, input, created.Headers.ETag!.ToString())).StatusCode);
        var duplicate = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/duplicate", new { }, edited.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        var copy = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        input["ingredients"]![0]!["id"] = copy.GetProperty("ingredients")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await HouseholdContractTests.Write(client, HttpMethod.Put, path, input, edited.Headers.ETag!.ToString())).StatusCode);
        using var unchanged = await client.GetAsync(path); Assert.Equal(edited.Headers.ETag, unchanged.Headers.ETag);
    }

    [Fact]
    public async Task Lost_create_and_duplicate_responses_replay_even_after_source_changes()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var ingredient = await Ingredient(client); var createKey = Guid.NewGuid();
        var created = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", RecipeWrite(ingredient), retryKey: createKey);
        var repeated = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", RecipeWrite(ingredient), retryKey: createKey);
        Assert.Equal(HttpStatusCode.Created, repeated.StatusCode); Assert.Equal(created.Headers.ETag, repeated.Headers.ETag);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
        var path = "/api/v1/recipes/" + (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var key = Guid.NewGuid();
        var copy = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/duplicate", new { }, created.Headers.ETag!.ToString(), key);
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(client, HttpMethod.Delete, path, etag: created.Headers.ETag!.ToString())).StatusCode);
        var replay = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/duplicate", new { }, created.Headers.ETag!.ToString(), key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(copy.Headers.ETag, replay.Headers.ETag);
        Assert.Equal(await copy.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Personal_favourites_invalidate_only_the_requesting_users_detail_and_list_caches()
    {
        await using var host = await Fresh(); using var admin = Client(host); await Setup(admin);
        var ingredient = await Ingredient(admin);
        var created = await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/recipes", RecipeWrite(ingredient));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = "/api/v1/recipes/" + (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var invitation = await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/household/invitations", new { });
        var token = (await invitation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        using var member = Client(host);
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/invitations/accept", new { token, email = "member@example.test", password = "member-test-passphrase", name = "Member" })).StatusCode);
        using var memberRecipe = await member.GetAsync(path);
        using var memberList = await member.GetAsync("/api/v1/recipes?favouriteOnly=true");
        using var adminList = await admin.GetAsync("/api/v1/recipes?favouriteOnly=true");
        Assert.Equal(HttpStatusCode.OK, memberRecipe.StatusCode); Assert.Equal(HttpStatusCode.OK, memberList.StatusCode);
        Assert.NotEqual(created.Headers.ETag, memberRecipe.Headers.ETag);
        var favourite = await HouseholdContractTests.Write(admin, HttpMethod.Put, path + "/favourite", new { favourite = true }, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, favourite.StatusCode);
        async Task<HttpResponseMessage> Conditional(HttpClient client, string url, string etag)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.TryAddWithoutValidation("If-None-Match", etag); return await client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.NotModified, (await Conditional(member, path, memberRecipe.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotModified, (await Conditional(member, "/api/v1/recipes?favouriteOnly=true", memberList.Headers.ETag!.ToString())).StatusCode);
        var changed = await Conditional(admin, path, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode); Assert.True((await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("favourite").GetBoolean());
        var changedList = await Conditional(admin, "/api/v1/recipes?favouriteOnly=true", adminList.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changedList.StatusCode); Assert.Single((await changedList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        var archived = await HouseholdContractTests.Write(member, HttpMethod.Put, path + "/archive", new { archived = true }, memberRecipe.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode); // Both roles edit the shared library.
    }

    [Fact]
    public async Task Images_reject_active_content_and_require_authentication()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var body = new MultipartFormDataContent();
        var file = new ByteArrayContent("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray());
        file.Headers.ContentType = new("image/svg+xml"); body.Add(file, "file", "recipe.svg");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/images") { Content = body };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.SendAsync(request)).StatusCode);
        using var anonymous = Client(host);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/images/" + Guid.NewGuid())).StatusCode);
    }
}
