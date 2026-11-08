using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Leftovers.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;

namespace RecipeManager.IntegrationTests.MealPlanning;

public sealed class MealContractTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Concurrent_creation_retries_return_one_cooking_event_after_a_lost_success_response()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var key = Guid.NewGuid(); var body = new { recipeId = scenario.RecipeId, date = "2026-11-02", slotId = scenario.SlotId, plannedPrepared = "4", plannedEaten = "2" };
        var responses = await Task.WhenAll(HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/cooking", body, retryKey: key), HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/cooking", body, retryKey: key));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var first = await responses[0].Content.ReadFromJsonAsync<JsonElement>(); var second = await responses[1].Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(first.GetProperty("cookingEvent").GetProperty("id").GetGuid(), second.GetProperty("cookingEvent").GetProperty("id").GetGuid());
        var replay = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/cooking", body, retryKey: key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(first.GetProperty("id").GetGuid(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var (calendar, _) = await MealScenario.Read(scenario.Admin, "/api/v1/meals?from=2026-11-02&to=2026-11-03");
        Assert.Single(calendar.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Direct_allocation_command_commits_a_single_reservation()
    {
        await using var scenario = await MealScenario.Create(postgres); var (meal, _) = await scenario.Cook();
        var sourceId = meal.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{sourceId}/leftovers");
        await using var scope = scenario.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == "admin@example.test");
        var household = await db.Memberships.IgnoreQueryFilters().Where(x => x.UserId == user.Id).Select(x => x.HouseholdId).SingleAsync();
        db.ChangeTracker.Clear();
        var actor = new RequestActor(user.Id, household, user.SecurityStamp!, DateTimeOffset.UtcNow.AddHours(1));
        var created = await scope.ServiceProvider.GetRequiredService<IPlannerStore>().CreateLeftoversAsync(actor,
            new LeftoverWrite(sourceId, long.Parse(balance.GetProperty("sourceRevision").GetString()!), new DateOnly(2026, 11, 3), scenario.SlotId, 2m), null, default);
        Assert.Equal(2m, created.Leftovers!.Commitment);
    }

    [Fact]
    public async Task Planning_requires_no_actual_confirmation_and_never_invents_a_leftover_meal()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var (meal, _) = await scenario.Cook();
        var source = meal.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (availability, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal("planned", availability.GetProperty("basis").GetString());
        Assert.Equal("2", availability.GetProperty("availablePortions").GetString());
        Assert.Equal("4", availability.GetProperty("plannedPrepared").GetString());
        Assert.Equal("2", availability.GetProperty("plannedEaten").GetString());
        Assert.Equal(JsonValueKind.Null, availability.GetProperty("actualPrepared").ValueKind);
        var (calendar, _) = await MealScenario.Read(scenario.Admin, "/api/v1/meals?from=2026-11-02&to=2026-11-04");
        Assert.Single(calendar.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, meal.GetProperty("cookingEvent").GetProperty("actualPrepared").ValueKind);
    }

    [Fact]
    public async Task Partial_consumption_release_and_reload_never_restore_historical_planned_reservations()
    {
        await using var scenario = await MealScenario.Create(postgres); var (meal, _) = await scenario.Cook();
        var source = meal.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (initial, sourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var allocation = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/leftovers", new { sourceCookingEventId = source, sourceRevision = initial.GetProperty("sourceRevision").GetString(), date = "2026-11-03", slotId = scenario.SlotId, plannedPortions = "2" });
        Assert.Equal(HttpStatusCode.Created, allocation.StatusCode);
        var leftover = await allocation.Content.ReadFromJsonAsync<JsonElement>(); var id = leftover.GetProperty("id").GetGuid();
        var (_, currentSourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var confirmed = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/cooking-events/{source}/actuals", new { actualPrepared = "4", actualEaten = "2" }, currentSourceTag);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var (_, mealTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{id}");
        var partial = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{id}/consumption", new { consumedPortions = "1.5", reservedRemainder = "0.5", sourceRevision = balance.GetProperty("sourceRevision").GetString() }, mealTag);
        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        var (afterPartial, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal("0", afterPartial.GetProperty("availablePortions").GetString());
        var released = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{id}/consumption", new { consumedPortions = "1.5", reservedRemainder = "0", sourceRevision = afterPartial.GetProperty("sourceRevision").GetString() }, partial.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        var (reloaded, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{id}");
        Assert.Equal("2", reloaded.GetProperty("leftovers").GetProperty("plannedPortions").GetString());
        Assert.Equal("1.5", reloaded.GetProperty("leftovers").GetProperty("consumedPortions").GetString());
        Assert.Equal("0", reloaded.GetProperty("leftovers").GetProperty("reservedRemainder").GetString());
        var (available, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal("0.5", available.GetProperty("availablePortions").GetString());
    }

    [Fact]
    public async Task Recipe_edits_do_not_refresh_a_cooking_snapshot_without_explicit_apply()
    {
        await using var scenario = await MealScenario.Create(postgres); var (meal, _) = await scenario.Cook();
        var id = meal.GetProperty("id").GetGuid(); var old = meal.GetProperty("cookingEvent").GetProperty("recipeSnapshot");
        var recipe = await scenario.Admin.GetAsync($"/api/v1/recipes/{scenario.RecipeId}");
        var edit = JsonNode.Parse(await recipe.Content.ReadAsStringAsync())!.AsObject();
        foreach (var key in new[] { "id", "revision", "archived", "favourite", "policyVersion" }) edit.Remove(key);
        foreach (var row in edit["ingredients"]!.AsArray()) { foreach (var key in new[] { "ingredientName", "categoryId", "categoryName", "exact" }) row!.AsObject().Remove(key); }
        edit["title"] = "Changed live title";
        var edited = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/recipes/{scenario.RecipeId}", edit, recipe.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var (unchanged, tag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{id}");
        Assert.Equal(old.GetRawText(), unchanged.GetProperty("cookingEvent").GetProperty("recipeSnapshot").GetRawText());
        var plan = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{id}/cooking", new { recipeId = scenario.RecipeId, date = "2026-11-02", slotId = scenario.SlotId, plannedPrepared = "6", plannedEaten = "2" }, tag);
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal(old.GetRawText(), (await plan.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cookingEvent").GetProperty("recipeSnapshot").GetRawText());
        tag = plan.Headers.ETag!.ToString();
        var recipeRevision = (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetString();
        var preview = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/meals/{id}/recipe-refresh", new { mode = "preview", expectedRecipeRevision = recipeRevision }, tag);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var (afterPreview, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{id}");
        Assert.Equal(old.GetRawText(), afterPreview.GetProperty("cookingEvent").GetProperty("recipeSnapshot").GetRawText());
        var previewBody = (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preview");
        var apply = new { mode = "apply", expectedRecipeRevision = recipeRevision, expectedEventRevision = previewBody.GetProperty("eventRevision").GetString() };
        var refreshed = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/meals/{id}/recipe-refresh", apply, tag);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var refreshedMeal = (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("meal");
        Assert.Equal("Changed live title", refreshedMeal.GetProperty("cookingEvent").GetProperty("recipeSnapshot").GetProperty("title").GetString());
        Assert.Equal("6", refreshedMeal.GetProperty("cookingEvent").GetProperty("plannedPrepared").GetString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/meals/{id}/recipe-refresh", apply, tag)).StatusCode);
    }
}
