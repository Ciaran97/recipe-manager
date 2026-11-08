using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;

namespace RecipeManager.IntegrationTests.Recipes;

public sealed class RecipeSearchContractTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Organisers_have_shared_crud_normalized_uniqueness_and_deletion_preserves_recipes()
    {
        await using var scene = await MealScenario.Create(postgres);
        using var member = await scene.Member();
        foreach (var kind in new[] { "collections", "tags" })
        {
            var path = "/api/v1/" + kind;
            var created = await HouseholdContractTests.Write(member, HttpMethod.Post, path, new { name = "Weeknight" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<JsonElement>();
            var detail = path + "/" + body.GetProperty("id").GetGuid();
            Assert.NotNull(created.Headers.ETag);
            Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(scene.Admin, HttpMethod.Post, path, new { name = "  weeknight  " })).StatusCode);
            var updated = await HouseholdContractTests.Write(scene.Admin, HttpMethod.Put, detail, new { name = "Everyday" }, created.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await HouseholdContractTests.Write(member, HttpMethod.Put, detail, new { name = "Stale" }, created.Headers.ETag.ToString())).StatusCode);
            var recipe = await scene.Admin.GetAsync("/api/v1/recipes/" + scene.RecipeId);
            var current = await recipe.Content.ReadFromJsonAsync<JsonElement>();
            var ingredientId = current.GetProperty("ingredients")[0].GetProperty("ingredientId").GetGuid();
            var write = JsonSerializer.SerializeToNode(RecipeContractTests.RecipeWrite(ingredientId))!.AsObject();
            write[kind == "collections" ? "collectionIds" : "tagIds"] = new JsonArray(body.GetProperty("id").GetGuid());
            write["acknowledgeUncertainIngredients"] = true;
            Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scene.Admin, HttpMethod.Put, "/api/v1/recipes/" + scene.RecipeId, write, recipe.Headers.ETag!.ToString())).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(member, HttpMethod.Delete, detail, etag: updated.Headers.ETag!.ToString())).StatusCode);
            var (remaining, _) = await MealScenario.Read(scene.Admin, "/api/v1/recipes/" + scene.RecipeId);
            Assert.Empty(remaining.GetProperty(kind == "collections" ? "collectionIds" : "tagIds").EnumerateArray());
        }
    }

    [Fact]
    public async Task Search_uses_or_within_each_filter_and_and_between_text_collection_and_tag_groups()
    {
        await using var scene = await MealScenario.Create(postgres);
        var firstCollection = await Create(scene.Admin, "collections", "Quick");
        var secondCollection = await Create(scene.Admin, "collections", "Weekend");
        var firstTag = await Create(scene.Admin, "tags", "Comfort");
        var secondTag = await Create(scene.Admin, "tags", "Fresh");
        var rice = await RecipeContractTests.Ingredient(scene.Admin, "Brown rice");
        var riceDetail = await scene.Admin.GetAsync("/api/v1/ingredients/" + rice);
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scene.Admin, HttpMethod.Post, "/api/v1/ingredients/" + rice + "/aliases", new { name = "Wholegrain rice" }, riceDetail.Headers.ETag!.ToString())).StatusCode);
        var a = await Recipe(scene.Admin, rice, "Warm bowl", [firstCollection], [firstTag]);
        var b = await Recipe(scene.Admin, rice, "Cold bowl", [secondCollection], [secondTag]);
        _ = await Recipe(scene.Admin, rice, "Unfiled bowl", [], []);
        var selection = $"collectionIds={firstCollection}&collectionIds={secondCollection}&tagIds={firstTag}&tagIds={secondTag}";
        Assert.Equal(new[] { a, b }.Order(), (await Find(scene.Admin, selection)).Order());
        Assert.Equal([a], await Find(scene.Admin, $"q=WHOLEGRAIN&collectionIds={firstCollection}&tagIds={firstTag}"));
        Assert.Equal([a], await Find(scene.Admin, "q=comFORT"));
        Assert.Empty(await Find(scene.Admin, $"q=cold&collectionIds={firstCollection}"));
        Assert.Equal(4, (await Find(scene.Admin, "")).Length);
        var page = await scene.Admin.GetFromJsonAsync<JsonElement>("/api/v1/recipes?" + selection + "&limit=1");
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var next = page.GetProperty("nextCursor").GetString(); Assert.False(string.IsNullOrEmpty(next));
        var second = await scene.Admin.GetFromJsonAsync<JsonElement>("/api/v1/recipes?" + selection + "&limit=1&cursor=" + Uri.EscapeDataString(next!));
        Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id"), second.GetProperty("items")[0].GetProperty("id"));
    }

    [Fact]
    public async Task Duplicate_assignments_are_unique_and_recipe_duplication_preserves_both_memberships()
    {
        await using var scene = await MealScenario.Create(postgres);
        var collection = await Create(scene.Admin, "collections", "Lunch");
        var tag = await Create(scene.Admin, "tags", "Simple");
        var ingredient = await RecipeContractTests.Ingredient(scene.Admin, "Beans");
        var id = await Recipe(scene.Admin, ingredient, "Bean lunch", [collection, collection], [tag, tag]);
        var response = await scene.Admin.GetAsync("/api/v1/recipes/" + id);
        var copy = await HouseholdContractTests.Write(scene.Admin, HttpMethod.Post, "/api/v1/recipes/" + id + "/duplicate", new { }, response.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        var saved = await copy.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(collection, Assert.Single(saved.GetProperty("collectionIds").EnumerateArray()).GetGuid());
        Assert.Equal(tag, Assert.Single(saved.GetProperty("tagIds").EnumerateArray()).GetGuid());
        using var member = await scene.Member();
        var first = HouseholdContractTests.Write(scene.Admin, HttpMethod.Post, "/api/v1/tags", new { name = "Concurrent" });
        var second = HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/tags", new { name = "CONCURRENT" });
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_and_personal_favourite_filters_combine_without_leaking_other_members_preferences()
    {
        await using var scene = await MealScenario.Create(postgres);
        using var member = await scene.Member();
        var ingredient = await RecipeContractTests.Ingredient(scene.Admin, "Lentils");
        var id = await Recipe(scene.Admin, ingredient, "50% lentil bowl", [], []);
        var path = "/api/v1/recipes/" + id;
        var response = await scene.Admin.GetAsync(path);
        var favourite = await HouseholdContractTests.Write(scene.Admin, HttpMethod.Put, path + "/favourite", new { favourite = true }, response.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, favourite.StatusCode);
        var archived = await HouseholdContractTests.Write(scene.Admin, HttpMethod.Put, path + "/archive", new { archived = true }, favourite.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal([scene.RecipeId], await Find(scene.Admin, "archived=exclude"));
        Assert.Equal([id], await Find(scene.Admin, "archived=only&favouriteOnly=true"));
        Assert.Empty(await Find(member, "archived=include&favouriteOnly=true"));
        Assert.Equal(2, (await Find(scene.Admin, "archived=include")).Length);
        Assert.Equal([id], await Find(scene.Admin, "archived=include&q=%25"));
    }

    private static async Task<Guid> Create(HttpClient client, string kind, string name)
    {
        var response = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/" + kind, new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> Recipe(HttpClient client, Guid ingredient, string title, Guid[] collections, Guid[] tags)
    {
        var body = JsonSerializer.SerializeToNode(RecipeContractTests.RecipeWrite(ingredient))!.AsObject();
        body["title"] = title; body["collectionIds"] = JsonSerializer.SerializeToNode(collections); body["tagIds"] = JsonSerializer.SerializeToNode(tags);
        var response = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<Guid[]> Find(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/v1/recipes?" + query); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
    }
}
