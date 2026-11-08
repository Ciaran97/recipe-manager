using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.Ingredients;

public sealed class CatalogueManagementTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
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
    public async Task Canonical_rename_alias_edit_and_category_change_keep_identity_and_references()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var id = await RecipeContractTests.Ingredient(client, "Red capsicum"); var path = "/api/v1/ingredients/" + id;
        var categories = await client.GetFromJsonAsync<JsonElement>("/api/v1/ingredient-categories");
        var produce = categories.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Produce").GetProperty("id").GetGuid();
        using var current = await client.GetAsync(path);
        var alias = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/aliases", new { name = "Red pepper" }, current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        var ingredient = await alias.Content.ReadFromJsonAsync<JsonElement>();
        var aliasId = ingredient.GetProperty("aliases")[0].GetProperty("id").GetGuid();
        var corrected = await HouseholdContractTests.Write(client, HttpMethod.Put, path + "/aliases/" + aliasId, new { name = "Sweet red pepper" }, alias.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var recipe = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", RecipeContractTests.RecipeWrite(id));
        Assert.Equal(HttpStatusCode.Created, recipe.StatusCode);
        var renamed = await HouseholdContractTests.Write(client, HttpMethod.Put, path,
            new { canonicalName = "Red bell pepper", categoryId = produce, retainOldNameAsAlias = true }, corrected.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var changed = await renamed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, changed.GetProperty("id").GetGuid()); Assert.Equal(produce, changed.GetProperty("categoryId").GetGuid());
        Assert.Contains(changed.GetProperty("aliases").EnumerateArray(), x => x.GetProperty("name").GetString() == "Red capsicum");
        var found = await client.GetFromJsonAsync<JsonElement>("/api/v1/ingredients?q=sweet%20red");
        Assert.Equal(id, Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var delete = await HouseholdContractTests.Write(client, HttpMethod.Delete, path, etag: renamed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("ingredient_referenced", (await delete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var removed = await HouseholdContractTests.Write(client, HttpMethod.Delete, path + "/aliases/" + aliasId, etag: renamed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
    }

    [Fact]
    public async Task Canonical_and_alias_namespace_collisions_reject_atomically_and_stale_edits_do_not_overwrite()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var mince = await RecipeContractTests.Ingredient(client, "Beef mince");
        var strips = await RecipeContractTests.Ingredient(client, "Beef strips");
        var path = "/api/v1/ingredients/" + strips;
        using var current = await client.GetAsync(path);
        var conflict = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/aliases", new { name = "  BEEF   mince " }, current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("name_conflict", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var preserved = await client.GetAsync(path); Assert.Equal(current.Headers.ETag, preserved.Headers.ETag);
        var alias = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/aliases", new { name = "Beef slices" }, current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/ingredients", new { canonicalName = "beef slices" })).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await HouseholdContractTests.Write(client, HttpMethod.Put, path, new { canonicalName = "Steak strips" }, current.Headers.ETag!.ToString())).StatusCode);
        Assert.NotEqual(mince, strips);
    }

    [Fact]
    public async Task Catalogue_and_recipes_are_isolated_from_another_authenticated_household()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var ingredient = await RecipeContractTests.Ingredient(client);
        var created = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", RecipeContractTests.RecipeWrite(ingredient));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options))
        {
            var user = new ApplicationUser(Guid.NewGuid(), "outside@example.test");
            user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>().HashPassword(user, "outside-test-passphrase");
            var household = new Household(Guid.NewGuid(), "Outside", "UTC");
            db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Administrator)); await db.SaveChangesAsync();
        }
        using var outsider = Client(host);
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/auth/login", new { email = "outside@example.test", password = "outside-test-passphrase" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync("/api/v1/ingredients/" + ingredient)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync("/api/v1/recipes/" + recipeId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await HouseholdContractTests.Write(outsider, HttpMethod.Put, "/api/v1/recipes/" + recipeId, RecipeContractTests.RecipeWrite(ingredient), created.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/recipes", RecipeContractTests.RecipeWrite(ingredient))).StatusCode);
    }
}
