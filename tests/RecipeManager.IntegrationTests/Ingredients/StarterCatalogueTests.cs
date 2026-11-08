using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.Ingredients;

public sealed class StarterCatalogueTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private RecipeManagerDbContext Database() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
    private ApiFixture Host(bool enabled) => new(postgres.ConnectionString, new Dictionary<string, string?>
    {
        ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default",
        ["StarterData:Enabled"] = enabled.ToString()
    });
    private static HttpClient Client(ApiFixture host) => host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    [Fact]
    public async Task New_households_have_editable_starter_ingredients_and_organisers()
    {
        await using var db = Database(); await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync();
        await using var host = Host(true); using var client = Client(host);
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/ingredients?limit=100");
        Assert.True(page.GetProperty("items").GetArrayLength() >= 70);
        var salt = page.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("canonicalName").GetString() == "Salt");
        var id = salt.GetProperty("id").GetGuid();
        using var read = await client.GetAsync("/api/v1/ingredients/" + id);
        Assert.True(read.Headers.CacheControl!.NoTransform); Assert.False(read.Headers.ETag!.IsWeak);
        var categories = await client.GetFromJsonAsync<JsonElement>("/api/v1/ingredient-categories");
        var tag = read.Headers.ETag!.ToString();
        foreach (var category in categories.GetProperty("items").EnumerateArray().Take(2))
        {
            var updated = await HouseholdContractTests.Write(client, HttpMethod.Put, "/api/v1/ingredients/" + id,
                new { canonicalName = "Salt", categoryId = category.GetProperty("id").GetGuid(), retainOldNameAsAlias = false }, tag);
            // Use the returned version for the next local edit; a truly stale write still fails below.
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var stale = await HouseholdContractTests.Write(client, HttpMethod.Put, "/api/v1/ingredients/" + id,
                new { canonicalName = "Salt", categoryId = (Guid?)null, retainOldNameAsAlias = false }, read.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
            tag = updated.Headers.ETag!.ToString();
        }
        foreach (var kind in new[] { "collections", "tags" })
        {
            var options = await client.GetFromJsonAsync<JsonElement>("/api/v1/" + kind);
            Assert.True(options.GetProperty("items").GetArrayLength() >= 6);
        }
        var custom = await RecipeContractTests.Ingredient(client, "My custom spice blend");
        var recipe = JsonSerializer.SerializeToNode(RecipeContractTests.RecipeWrite(custom))!;
        recipe["ingredients"]![0]!["unit"] = "handful";
        var saved = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", recipe);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var recipeBody = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("handful", recipeBody.GetProperty("ingredients")[0].GetProperty("unit").GetString());
    }

    [Fact]
    public async Task Upgrade_preserves_custom_ingredients_and_aliases_and_seeds_only_once()
    {
        await using var db = Database(); await db.Database.EnsureDeletedAsync();
        await db.GetService<IMigrator>().MigrateAsync("20261103112039_RecipeOrganisers");
        await using var host = Host(false); using var client = Client(host);
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);
        var rice = await RecipeContractTests.Ingredient(client, "Rice");
        var custom = await RecipeContractTests.Ingredient(client, "Table seasoning");
        using var read = await client.GetAsync("/api/v1/ingredients/" + custom);
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/ingredients/" + custom + "/aliases", new { name = "Salt" }, read.Headers.ETag!.ToString())).StatusCode);
        await db.Database.MigrateAsync();
        var ingredients = await db.Ingredients.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Null(ingredients.Single(x => x.Id == rice).CategoryId);
        Assert.DoesNotContain(ingredients, x => x.CanonicalName == "Salt");
        Assert.Single(await db.IngredientNames.IgnoreQueryFilters().Where(x => x.NormalizedName == "SALT").ToListAsync());
        var count = ingredients.Count;
        await db.Database.MigrateAsync();
        Assert.Equal(count, await db.Ingredients.IgnoreQueryFilters().CountAsync());
    }
}
