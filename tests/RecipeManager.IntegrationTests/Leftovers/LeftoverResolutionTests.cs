using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.Leftovers;

public sealed class LeftoverResolutionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<Guid> Reserve(MealScenario scenario, Guid source)
    {
        var (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var result = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/leftovers", new { sourceCookingEventId = source, sourceRevision = balance.GetProperty("sourceRevision").GetString(), date = "2026-11-03", slotId = scenario.SlotId, plannedPortions = "2" });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode); return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task Confirm(MealScenario scenario, Guid source)
    {
        var (_, tag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/cooking-events/{source}/actuals", new { actualPrepared = "4", actualEaten = "2" }, tag)).StatusCode);
    }
    private static async Task Partial(MealScenario scenario, Guid source, Guid meal)
    {
        await Confirm(scenario, source);
        var (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var (_, tag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{meal}/consumption", new { sourceRevision = balance.GetProperty("sourceRevision").GetString(), consumedPortions = "1.5", reservedRemainder = "0.5" }, tag)).StatusCode);
    }

    [Fact]
    public async Task Atomic_plan_resolution_keeps_recipe_selection_position_and_portions_in_one_transaction()
    {
        await using var scenario = await MealScenario.Create(postgres); var (cook, _) = await scenario.Cook();
        var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid(); var meal = await Reserve(scenario, source);
        var ingredient = await RecipeContractTests.Ingredient(scenario.Admin, "Beef");
        var createdRecipe = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/recipes", RecipeContractTests.RecipeWrite(ingredient));
        Assert.Equal(HttpStatusCode.Created, createdRecipe.StatusCode);
        var newRecipe = await createdRecipe.Content.ReadFromJsonAsync<JsonElement>();
        var (balance, sourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var (dependent, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        var changed = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/cooking-events/{source}/resolve", new
        {
            kind = "plan",
            plannedPrepared = "2",
            plannedEaten = "1",
            date = "2026-11-01",
            slotId = scenario.SlotId,
            recipeId = newRecipe.GetProperty("id").GetGuid(),
            expectedRecipeRevision = newRecipe.GetProperty("revision").GetString(),
            expectedSources = new[] { new { sourceCookingEventId = source, revision = balance.GetProperty("sourceRevision").GetString() } },
            dependents = new[] { new { mealId = meal, mealRevision = dependent.GetProperty("revision").GetString(), action = "release", reservedRemainder = "1" } }
        }, sourceTag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var body = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-11-01", body.GetProperty("date").GetString());
        Assert.Equal(newRecipe.GetProperty("id").GetGuid(), body.GetProperty("cookingEvent").GetProperty("sourceRecipeId").GetGuid());
        Assert.Equal("2", body.GetProperty("cookingEvent").GetProperty("plannedPrepared").GetString());
        var (updated, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        Assert.Equal("2", updated.GetProperty("leftovers").GetProperty("plannedPortions").GetString());
        Assert.Equal("1", updated.GetProperty("leftovers").GetProperty("reservedRemainder").GetString());
        Assert.Equal("2026-11-01", updated.GetProperty("leftovers").GetProperty("sourceDate").GetString());
    }

    [Fact]
    public async Task Removing_consumed_leftovers_needs_explicit_confirmation_and_frees_only_its_commitment()
    {
        await using var scenario = await MealScenario.Create(postgres); var (cook, _) = await scenario.Cook();
        var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid(); var meal = await Reserve(scenario, source);
        await Partial(scenario, source, meal);
        var (_, mealTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        var (balance, sourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var rejected = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, $"/api/v1/meals/{meal}", etag: mealTag);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(sourceTag, (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers")).ETag);
        var deleted = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, $"/api/v1/meals/{meal}", new { sourceRevision = balance.GetProperty("sourceRevision").GetString(), confirmConsumedRemoval = true }, mealTag);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal("2", (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers")).Body.GetProperty("availablePortions").GetString());
    }

    [Fact]
    public async Task Atomic_actual_correction_releases_only_reservation_and_source_clear_requires_explicit_consumption_resolution()
    {
        await using var scenario = await MealScenario.Create(postgres); var (cook, _) = await scenario.Cook();
        var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid(); var meal = await Reserve(scenario, source);
        await Partial(scenario, source, meal);
        var (balance, sourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var (allocated, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        var rejected = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/cooking-events/{source}/actuals", new { actualPrepared = "3", actualEaten = "1.5" }, sourceTag);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(sourceTag, (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers")).ETag);
        var resolved = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/cooking-events/{source}/resolve", new
        {
            kind = "actuals",
            actualPrepared = "3",
            actualEaten = "1.5",
            expectedSources = new[] { new { sourceCookingEventId = source, revision = balance.GetProperty("sourceRevision").GetString() } },
            dependents = new[] { new { mealId = meal, mealRevision = allocated.GetProperty("revision").GetString(), action = "release" } }
        }, sourceTag);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var (after, currentSourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal("0", after.GetProperty("availablePortions").GetString());
        var (released, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        Assert.Equal("2", released.GetProperty("leftovers").GetProperty("plannedPortions").GetString());
        Assert.Equal("1.5", released.GetProperty("leftovers").GetProperty("consumedPortions").GetString());
        Assert.Equal("0", released.GetProperty("leftovers").GetProperty("reservedRemainder").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, $"/api/v1/cooking-events/{source}/actuals", etag: currentSourceTag)).StatusCode);
        var cleared = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, $"/api/v1/cooking-events/{source}/actuals", new
        {
            dependentResolutions = new[] { new { mealId = meal, mealRevision = released.GetProperty("revision").GetString(), action = "edit", clearConsumption = true, reservedRemainder = "0" } }
        }, currentSourceTag);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var (planned, plannedTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal("planned", planned.GetProperty("basis").GetString()); Assert.Equal("2", planned.GetProperty("availablePortions").GetString());
        var (unconfirmed, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        Assert.Equal(JsonValueKind.Null, unconfirmed.GetProperty("leftovers").GetProperty("consumedPortions").ValueKind);
        var removed = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/cooking-events/{source}/resolve", new
        {
            kind = "delete",
            expectedSources = new[] { new { sourceCookingEventId = source, revision = planned.GetProperty("sourceRevision").GetString() } },
            dependents = new[] { new { mealId = meal, mealRevision = unconfirmed.GetProperty("revision").GetString(), action = "delete" } }
        }, plannedTag);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync($"/api/v1/meals/{meal}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync($"/api/v1/meals/{cook.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync($"/api/v1/recipes/{scenario.RecipeId}")).StatusCode);
    }

    [Fact]
    public async Task Reassigning_consumed_leftovers_checks_both_sources_and_preserves_absolute_accounting()
    {
        await using var scenario = await MealScenario.Create(postgres); var (first, _) = await scenario.Cook(); var (second, _) = await scenario.Cook();
        var source = first.GetProperty("cookingEvent").GetProperty("id").GetGuid(); var target = second.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var meal = await Reserve(scenario, source); await Partial(scenario, source, meal);
        var (before, beforeTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var (targetBalance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{target}/leftovers");
        var (_, mealTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        var invalid = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{meal}/leftovers", new
        {
            sourceCookingEventId = target,
            sourceRevision = targetBalance.GetProperty("sourceRevision").GetString(),
            previousSourceRevision = before.GetProperty("sourceRevision").GetString(),
            date = "2026-11-03",
            slotId = scenario.SlotId,
            plannedPortions = "2"
        }, mealTag);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Equal(beforeTag, (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers")).ETag);
        await Confirm(scenario, target);
        var (confirmedTarget, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{target}/leftovers");
        var (currentMeal, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        var reassigned = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/cooking-events/{source}/resolve", new
        {
            kind = "plan",
            plannedPrepared = "4",
            plannedEaten = "2",
            expectedSources = new[] { new { sourceCookingEventId = source, revision = before.GetProperty("sourceRevision").GetString() }, new { sourceCookingEventId = target, revision = confirmedTarget.GetProperty("sourceRevision").GetString() } },
            dependents = new[] { new { mealId = meal, mealRevision = currentMeal.GetProperty("revision").GetString(), action = "reassign", sourceCookingEventId = target } }
        }, beforeTag);
        Assert.Equal(HttpStatusCode.OK, reassigned.StatusCode);
        Assert.Equal("2", (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers")).Body.GetProperty("availablePortions").GetString());
        Assert.Equal("0", (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{target}/leftovers")).Body.GetProperty("availablePortions").GetString());
        var (changed, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        Assert.Equal(target, changed.GetProperty("leftovers").GetProperty("sourceCookingEventId").GetGuid());
        Assert.Equal("1.5", changed.GetProperty("leftovers").GetProperty("consumedPortions").GetString());
        Assert.Equal("0.5", changed.GetProperty("leftovers").GetProperty("reservedRemainder").GetString());
    }

    [Fact]
    public async Task Released_unconfirmed_reservations_stay_released_on_edit_reload_repeat_and_duplicate()
    {
        await using var scenario = await MealScenario.Create(postgres); var (cook, _) = await scenario.Cook();
        var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid(); var meal = await Reserve(scenario, source);
        async Task<HttpResponseMessage> Release(Guid id)
        {
            var (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers"); var (_, tag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{id}");
            return await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{id}/leftovers", new { sourceCookingEventId = source, sourceRevision = balance.GetProperty("sourceRevision").GetString(), date = "2026-11-03", slotId = scenario.SlotId, plannedPortions = "2", reservedRemainder = "0" }, tag);
        }
        Assert.Equal(HttpStatusCode.OK, (await Release(meal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Release(meal)).StatusCode);
        var (balance, sourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers"); Assert.Equal("2", balance.GetProperty("availablePortions").GetString());
        var (_, mealTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        var rejected = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{meal}/leftovers", new { sourceCookingEventId = source, sourceRevision = balance.GetProperty("sourceRevision").GetString(), date = "2026-11-03", slotId = scenario.SlotId, plannedPortions = "3" }, mealTag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode); Assert.Equal(sourceTag, (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers")).ETag);
        var copy = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/meals/{meal}/duplicate", new { date = "2026-11-04", slotId = scenario.SlotId }, mealTag);
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode); var duplicate = await copy.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2", duplicate.GetProperty("leftovers").GetProperty("reservedRemainder").GetString());
        Assert.Equal(JsonValueKind.Null, duplicate.GetProperty("leftovers").GetProperty("consumedPortions").ValueKind);
        Assert.Equal("0", (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers")).Body.GetProperty("availablePortions").GetString());
        var (original, currentTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meals/{meal}");
        Assert.NotEqual(mealTag, currentTag); Assert.Equal("0", original.GetProperty("leftovers").GetProperty("reservedRemainder").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Release(duplicate.GetProperty("id").GetGuid())).StatusCode);
        Assert.Equal("2", (await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers")).Body.GetProperty("availablePortions").GetString());
    }
}
