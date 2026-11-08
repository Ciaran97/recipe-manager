using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.Shopping;

public sealed class ShoppingContractTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Concurrent_item_edits_have_one_winner_and_manual_item_retry_does_not_duplicate_or_need_a_new_parent_version()
    {
        await using var scenario = await MealScenario.Create(postgres); using var member = await scenario.Member();
        await scenario.Cook(); var list = await Generate(scenario.Admin, "Shared edits", MealRange());
        var path = "/api/v1/shopping-lists/" + list.GetProperty("id").GetGuid();
        var itemId = Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
        var (_, tag) = await MealScenario.Read(scenario.Admin, path);
        var results = await Task.WhenAll(new[] { scenario.Admin, member }.Select(client => HouseholdContractTests.Write(client, HttpMethod.Put,
            path + "/items/" + itemId, new { purchased = true }, tag)));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var (_, currentTag) = await MealScenario.Read(scenario.Admin, path);
        var key = Guid.NewGuid(); var body = new { label = "Bread", quantityText = "1", kind = "numeric", unit = "item" };
        var first = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/items", body, currentTag, key);
        var retry = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/items", body, currentTag, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        var (after, _) = await MealScenario.Read(scenario.Admin, path);
        Assert.Single(after.GetProperty("items").EnumerateArray(), x => x.GetProperty("manual").GetBoolean());
        Assert.True(after.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == itemId).GetProperty("purchased").GetBoolean());
    }

    [Fact]
    public async Task Saved_snapshot_references_protect_recipes_and_ingredients_after_source_events_and_live_rows_are_removed()
    {
        await using var scenario = await MealScenario.Create(postgres); var (cook, _) = await scenario.Cook();
        var list = await Generate(scenario.Admin, "Retained snapshot", MealRange());
        var listPath = "/api/v1/shopping-lists/" + list.GetProperty("id").GetGuid();
        var recipePath = "/api/v1/recipes/" + scenario.RecipeId;
        var (recipe, recipeTag) = await MealScenario.Read(scenario.Admin, recipePath);
        var ingredient = recipe.GetProperty("ingredients")[0].GetProperty("ingredientId").GetGuid();
        var emptyRecipe = new { title = "Recipe with live rows removed", defaultServings = "3", groups = Array.Empty<object>(), ingredients = Array.Empty<object>(), instructions = Array.Empty<object>(), collectionIds = Array.Empty<Guid>(), tagIds = Array.Empty<Guid>() };
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, recipePath, emptyRecipe, recipeTag)).StatusCode);
        var mealPath = "/api/v1/meals/" + cook.GetProperty("id").GetGuid(); var (_, mealTag) = await MealScenario.Read(scenario.Admin, mealPath);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, mealPath, etag: mealTag)).StatusCode);
        var (saved, listTag) = await MealScenario.Read(scenario.Admin, listPath);
        Assert.Equal(list.GetRawText(), saved.GetRawText());
        var (_, currentRecipeTag) = await MealScenario.Read(scenario.Admin, recipePath);
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, recipePath, etag: currentRecipeTag)).StatusCode);
        var ingredientPath = "/api/v1/ingredients/" + ingredient; var (_, ingredientTag) = await MealScenario.Read(scenario.Admin, ingredientPath);
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, ingredientPath, etag: ingredientTag)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, listPath, etag: listTag)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, recipePath, etag: currentRecipeTag)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, ingredientPath, etag: ingredientTag)).StatusCode);
    }
    [Fact]
    public async Task Actual_basis_requires_every_cook_confirmed_and_range_removal_requires_new_review()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var (monday, _) = await scenario.Cook(); var (wednesday, _) = await scenario.Cook("2026-11-04");
        var list = await Generate(scenario.Admin, "Actual basis review", MealRange());
        var path = "/api/v1/shopping-lists/" + list.GetProperty("id").GetGuid();
        var (_, tag) = await MealScenario.Read(scenario.Admin, path);
        async Task Confirm(JsonElement meal, string prepared)
        {
            var id = meal.GetProperty("cookingEvent").GetProperty("id").GetGuid();
            var (_, sourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{id}/leftovers");
            Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/cooking-events/{id}/actuals", new { actualPrepared = prepared, actualEaten = "0" }, sourceTag)).StatusCode);
        }
        await Confirm(monday, "5");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/preview", new { basis = "actual" }, tag)).StatusCode);
        await Confirm(wednesday, "6");
        var (stillPlanned, _) = await MealScenario.Read(scenario.Admin, path);
        Assert.Equal("8", Assert.Single(stillPlanned.GetProperty("items").EnumerateArray()).GetProperty("baselineExact").GetProperty("numerator").GetString());
        var previewResponse = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/preview", new { basis = "actual" }, tag);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var applied = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/apply", Apply(preview), tag);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var (actual, actualTag) = await MealScenario.Read(scenario.Admin, path);
        Assert.Equal("actual", actual.GetProperty("basis").GetString());
        Assert.Equal("11", Assert.Single(actual.GetProperty("items").EnumerateArray()).GetProperty("baselineExact").GetProperty("numerator").GetString());
        var beforeRemoval = await Preview(scenario.Admin, path, actualTag);
        var mealId = wednesday.GetProperty("id").GetGuid(); var (_, mealTag) = await MealScenario.Read(scenario.Admin, "/api/v1/meals/" + mealId);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, "/api/v1/meals/" + mealId, etag: mealTag)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/apply", Apply(beforeRemoval), actualTag)).StatusCode);
        var (unchanged, _) = await MealScenario.Read(scenario.Admin, path);
        Assert.Equal(actual.GetRawText(), unchanged.GetRawText());
    }
    [Fact]
    public async Task Planned_generation_matches_recipe_selection_without_actuals_or_leftover_requirements()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var (cook, _) = await scenario.Cook();
        var source = cook.GetProperty("cookingEvent");
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/leftovers", new
        {
            sourceCookingEventId = source.GetProperty("id").GetGuid(),
            sourceRevision = source.GetProperty("revision").GetString(),
            date = "2026-11-03",
            slotId = scenario.SlotId,
            plannedPortions = "2"
        })).StatusCode);
        var plan = await Generate(scenario.Admin, "Plan", MealRange());
        var manual = await Generate(scenario.Admin, "Recipes", new { kind = "recipes", entries = new[] { new { recipeId = scenario.RecipeId, servings = "4" } } });
        var planItem = Assert.Single(plan.GetProperty("items").EnumerateArray());
        var manualItem = Assert.Single(manual.GetProperty("items").EnumerateArray());
        Assert.Equal("4", planItem.GetProperty("baselineExact").GetProperty("numerator").GetString());
        Assert.Equal("9", planItem.GetProperty("baselineExact").GetProperty("denominator").GetString());
        Assert.Equal(planItem.GetProperty("baselineExact").GetRawText(), manualItem.GetProperty("baselineExact").GetRawText());
        Assert.Equal(1, planItem.GetProperty("contributions").GetArrayLength());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/shopping-lists/generate", new { name = "Actuals", selection = MealRange(), basis = "actual" })).StatusCode);
    }

    [Fact]
    public async Task Unknown_contributions_remain_visible_and_independently_checkable_next_to_purchased_known_amounts()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var ingredient = await RecipeContractTests.Ingredient(scenario.Admin, "Salt");
        var recipe = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/recipes", new
        {
            title = "Salt example",
            defaultServings = "2",
            groups = Array.Empty<object>(),
            ingredients = new[] {
                new { position = 0, ingredientId = ingredient, quantityText = "5", kind = "numeric", unit = "g", uncertain = false },
                new { position = 1, ingredientId = ingredient, quantityText = "to taste", kind = "unknown", unit = "g", uncertain = true } },
            instructions = Array.Empty<object>(),
            collectionIds = Array.Empty<Guid>(),
            tagIds = Array.Empty<Guid>(),
            acknowledgeUncertainIngredients = true
        });
        Assert.Equal(HttpStatusCode.Created, recipe.StatusCode);
        var id = (await recipe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var list = await Generate(scenario.Admin, "Salt shopping", new { kind = "recipes", entries = new[] { new { recipeId = id, servings = "2" } } });
        var path = "/api/v1/shopping-lists/" + list.GetProperty("id").GetGuid();
        var (_, tag) = await MealScenario.Read(scenario.Admin, path);
        var known = list.GetProperty("items").EnumerateArray().Single(x => !x.GetProperty("unresolved").GetBoolean());
        var unknown = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("unresolved").GetBoolean());
        Assert.True(known.GetProperty("additionalAmountUnresolved").GetBoolean());
        Assert.Equal(JsonValueKind.Null, unknown.GetProperty("baselineExact").ValueKind);
        var purchased = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, path + "/items/" + known.GetProperty("id").GetGuid(), new { purchased = true }, tag);
        Assert.Equal(HttpStatusCode.OK, purchased.StatusCode);
        var (after, _) = await MealScenario.Read(scenario.Admin, path);
        Assert.Equal(2, after.GetProperty("items").GetArrayLength());
        Assert.Equal(1, after.GetProperty("summary").GetProperty("unresolvedCount").GetInt32());
        Assert.False(after.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == unknown.GetProperty("id").GetGuid()).GetProperty("purchased").GetBoolean());
    }

    [Fact]
    public async Task Reconciliation_preserves_manual_items_overrides_and_purchased_state_and_rejects_stale_list_or_sources()
    {
        await using var scenario = await MealScenario.Create(postgres);
        await scenario.Cook();
        var list = await Generate(scenario.Admin, "Editable snapshot", MealRange());
        var path = "/api/v1/shopping-lists/" + list.GetProperty("id").GetGuid();
        var itemId = Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
        var (_, tag) = await MealScenario.Read(scenario.Admin, path);
        var edited = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, path + "/items/" + itemId,
            new { quantityOverride = new { quantityText = "600", kind = "numeric", unit = "g", measurementStandard = "AU" }, purchased = true }, tag);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var manual = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/items",
            new { label = "Bread", quantityText = "1", kind = "numeric", unit = "item", measurementStandard = "AU" }, edited.Headers.ETag!.ToString(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var (before, listTag) = await MealScenario.Read(scenario.Admin, path);
        var preview = await Preview(scenario.Admin, path, listTag);
        await scenario.Cook("2026-11-04"); // Must invalidate even though this source was absent from the preview vector.
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/apply", Apply(preview), listTag)).StatusCode);
        var (unchanged, unchangedTag) = await MealScenario.Read(scenario.Admin, path);
        Assert.Equal(before.GetRawText(), unchanged.GetRawText()); Assert.Equal(listTag, unchangedTag);
        var fresh = await Preview(scenario.Admin, path, listTag);
        var applied = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/apply", Apply(fresh), listTag);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var (after, newTag) = await MealScenario.Read(scenario.Admin, path);
        Assert.NotEqual(listTag, newTag);
        var overridden = after.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == itemId);
        Assert.True(overridden.GetProperty("purchased").GetBoolean()); Assert.True(overridden.GetProperty("hasOverride").GetBoolean());
        Assert.Equal("600", overridden.GetProperty("effectiveExact").GetProperty("numerator").GetString());
        Assert.Contains(after.GetProperty("items").EnumerateArray(), x => x.GetProperty("manual").GetBoolean() && x.GetProperty("label").GetString() == "Bread");
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/apply", Apply(fresh), listTag)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_lost_response_generation_retries_return_one_snapshot_and_foreign_identifiers_are_denied()
    {
        await using var scenario = await MealScenario.Create(postgres);
        await scenario.Cook(); var key = Guid.NewGuid();
        var body = new { name = "One snapshot", selection = MealRange(), basis = "planned" };
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/shopping-lists/generate", body, retryKey: key)));
        Assert.All(results, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal(await results[0].Content.ReadAsStringAsync(), await results[1].Content.ReadAsStringAsync());
        Assert.Equal(results[0].Headers.ETag, results[1].Headers.ETag);
        using var anonymous = scenario.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var id = (await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/shopping-lists/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync("/api/v1/shopping-lists/" + Guid.NewGuid())).StatusCode);
        await using (var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options))
        {
            var user = new ApplicationUser(Guid.NewGuid(), "shopping-outside@example.test");
            user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>().HashPassword(user, "outside-test-passphrase");
            var household = new Household(Guid.NewGuid(), "Outside", "UTC");
            db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Administrator)); await db.SaveChangesAsync();
        }
        using var outsider = scenario.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/auth/login", new { email = "shopping-outside@example.test", password = "outside-test-passphrase" })).StatusCode);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/shopping-lists/" + id); conditional.Headers.IfNoneMatch.Add(results[0].Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendAsync(conditional)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, $"/api/v1/shopping-lists/{id}/items", new { label = "Foreign item" }, results[0].Headers.ETag!.ToString())).StatusCode);
    }

    [Fact]
    public async Task Adding_recipe_sources_preserves_meals_and_manual_items_and_rejects_stale_apply()
    {
        await using var scenario = await MealScenario.Create(postgres);
        await scenario.Cook();
        var list = await Generate(scenario.Admin, "Combined groceries", MealRange());
        var path = "/api/v1/shopping-lists/" + list.GetProperty("id").GetGuid();
        var (_, tag) = await MealScenario.Read(scenario.Admin, path);
        var manual = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/items", new { label = "Soap" }, tag, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var previewResponse = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/sources/preview",
            new { selection = new { kind = "recipes", entries = new[] { new { recipeId = scenario.RecipeId, servings = "3" } } } }, manual.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("combined", preview.GetProperty("selection").GetProperty("kind").GetString());
        Assert.Single(preview.GetProperty("manualItems").EnumerateArray());
        var applied = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/apply", Apply(preview), previewResponse.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var result = await applied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetProperty("items").GetArrayLength());
        var generated = result.GetProperty("items").EnumerateArray().Single(x => !x.GetProperty("manual").GetBoolean());
        Assert.Equal("7", generated.GetProperty("baselineExact").GetProperty("numerator").GetString());
        Assert.Equal("9", generated.GetProperty("baselineExact").GetProperty("denominator").GetString());
        Assert.Equal(2, generated.GetProperty("contributions").GetArrayLength());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, path + "/reconciliation/apply", Apply(preview), previewResponse.Headers.ETag!.ToString())).StatusCode);
        // The combined source snapshot remains reviewable after the initial add operation.
        var refreshed = await Preview(scenario.Admin, path, applied.Headers.ETag!.ToString());
        Assert.Equal("combined", refreshed.GetProperty("selection").GetProperty("kind").GetString());
    }

    private static object MealRange() => new { kind = "mealRange", from = "2026-11-02", to = "2026-11-04" };
    private static async Task<JsonElement> Generate(HttpClient client, string name, object selection)
    {
        var response = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/shopping-lists/generate", new { name, selection, basis = "planned" }, retryKey: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<JsonElement> Preview(HttpClient client, string path, string tag)
    {
        var response = await HouseholdContractTests.Write(client, HttpMethod.Post, path + "/reconciliation/preview", new { }, tag);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static object Apply(JsonElement preview) => new
    {
        listRevision = preview.GetProperty("listRevision").GetString(),
        householdSourceRevision = preview.GetProperty("householdSourceRevision").GetString(),
        sourceRevisions = preview.GetProperty("sourceRevisions"),
        policyVersion = preview.GetProperty("policyVersion").GetInt32(),
        selection = preview.GetProperty("selection"),
        basis = preview.GetProperty("basis"),
        choices = preview.GetProperty("changes").EnumerateArray().Select(x => new { key = x.GetProperty("key").GetString(), choice = x.GetProperty("conflict").GetBoolean() ? "keepUser" : "acceptNew" }).ToArray()
    };
}
