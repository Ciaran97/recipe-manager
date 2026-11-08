using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.Ingredients;

public sealed class CatalogueApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<ApiFixture> Fresh()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync();
        return new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?> { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" });
    }
    private static HttpClient Client(ApiFixture host) => host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task Setup(HttpClient client) => Assert.Equal(HttpStatusCode.Created,
        (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);

    [Fact]
    public async Task Own_alias_can_be_promoted_without_losing_identity_and_category_delete_uses_fallback()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var categories = await client.GetFromJsonAsync<JsonElement>("/api/v1/ingredient-categories");
        Assert.Equal(4, categories.GetProperty("items").GetArrayLength());
        var produce = categories.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Produce").GetProperty("id").GetGuid();
        var id = await RecipeContractTests.Ingredient(client, "Capsicum"); var path = "/api/v1/ingredients/" + id;
        using var initial = await client.GetAsync(path);
        var alias = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/aliases", new { name = "Pepper" }, initial.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        var promoted = await HouseholdContractTests.Write(client, HttpMethod.Put, path, new { canonicalName = "Pepper", categoryId = produce, retainOldNameAsAlias = true }, alias.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        var ingredient = await promoted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, ingredient.GetProperty("id").GetGuid()); Assert.Equal("Pepper", ingredient.GetProperty("canonicalName").GetString());
        Assert.Equal("Capsicum", Assert.Single(ingredient.GetProperty("aliases").EnumerateArray()).GetProperty("name").GetString());
        using var category = await client.GetAsync("/api/v1/ingredient-categories/" + produce);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(client, HttpMethod.Delete, "/api/v1/ingredient-categories/" + produce, etag: category.Headers.ETag!.ToString())).StatusCode);
        using var changed = await client.GetAsync(path);
        Assert.NotEqual(promoted.Headers.ETag, changed.Headers.ETag);
        Assert.Equal(JsonValueKind.Null, (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("categoryId").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(client, HttpMethod.Delete, path, etag: changed.Headers.ETag!.ToString())).StatusCode);
        var found = await client.GetFromJsonAsync<JsonElement>("/api/v1/ingredients?q=Capsicum");
        Assert.Empty(found.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Lost_alias_response_replays_after_parent_deletion_without_creating_another_ingredient()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var id = await RecipeContractTests.Ingredient(client, "Capsicum"); var path = "/api/v1/ingredients/" + id;
        using var before = await client.GetAsync(path); var key = Guid.NewGuid();
        var alias = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/aliases", new { name = "Pepper" }, before.Headers.ETag!.ToString(), key);
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(client, HttpMethod.Delete, path, etag: alias.Headers.ETag!.ToString())).StatusCode);
        var retry = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/aliases", new { name = "Pepper" }, before.Headers.ETag!.ToString(), key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(alias.Headers.ETag, retry.Headers.ETag);
        Assert.Equal(await alias.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Creation_replays_lost_responses_and_literal_search_does_not_expand_wildcards()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var key = Guid.NewGuid();
        var first = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/ingredients", new { canonicalName = "100% cocoa" }, retryKey: key);
        var retry = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/ingredients", new { canonicalName = "100% cocoa" }, retryKey: key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(first.Headers.ETag, retry.Headers.ETag);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/ingredients", new { canonicalName = "Changed cocoa" }, retryKey: key)).StatusCode);
        await RecipeContractTests.Ingredient(client, "Rice");
        var literal = await client.GetFromJsonAsync<JsonElement>("/api/v1/ingredients?q=%25");
        Assert.Equal("100% cocoa", Assert.Single(literal.GetProperty("items").EnumerateArray()).GetProperty("canonicalName").GetString());
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ingredients/" + id); conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditional)).StatusCode);
    }
}
