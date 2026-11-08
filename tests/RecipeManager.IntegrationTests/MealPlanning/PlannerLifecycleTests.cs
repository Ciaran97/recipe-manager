using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;

namespace RecipeManager.IntegrationTests.MealPlanning;

public sealed class PlannerLifecycleTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Custom_slots_create_rename_reorder_and_delete_without_losing_default_slots()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var key = Guid.NewGuid(); var body = new { name = "Afternoon snack", position = 1 };
        var created = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meal-slots", body, retryKey: key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var slot = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = slot.GetProperty("id").GetGuid();
        Assert.Equal(1, slot.GetProperty("position").GetInt32()); Assert.False(slot.GetProperty("isDefault").GetBoolean());
        var replay = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meal-slots", body, retryKey: key);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var renamed = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meal-slots/{id}", new { name = "Snack", position = 3 }, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var (all, _) = await MealScenario.Read(scenario.Admin, "/api/v1/meal-slots");
        Assert.Equal(new[] { "Breakfast", "Lunch", "Dinner", "Snack" }, all.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToArray());
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, $"/api/v1/meal-slots/{id}", etag: renamed.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(3, (await MealScenario.Read(scenario.Admin, "/api/v1/meal-slots")).Body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Other_meals_move_duplicate_and_delete_and_cook_duplicates_never_copy_actuals()
    {
        await using var scenario = await MealScenario.Create(postgres); var (cook, _) = await scenario.Cook();
        var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (_, sourceTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var confirmed = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/cooking-events/{source}/actuals", new { actualPrepared = "5", actualEaten = "2" }, sourceTag);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var copy = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/meals/{cook.GetProperty("id").GetGuid()}/duplicate",
            new { date = "2026-11-05", slotId = scenario.SlotId }, confirmed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        var duplicate = (await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cookingEvent");
        Assert.NotEqual(source, duplicate.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, duplicate.GetProperty("actualPrepared").ValueKind);
        Assert.Equal("4", duplicate.GetProperty("plannedPrepared").GetString());
        var other = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/other", new { date = "2026-11-04", slotId = scenario.SlotId, label = "Takeaway" });
        Assert.Equal(HttpStatusCode.Created, other.StatusCode); var id = (await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var edit = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{id}/other", new { date = "2026-11-04", slotId = scenario.SlotId, label = "Eating out" }, other.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var moved = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{id}/position", new { date = "2026-11-06", slotId = scenario.SlotId }, edit.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var movedBody = await moved.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("2026-11-06", movedBody.GetProperty("date").GetString()); Assert.Equal("Eating out", movedBody.GetProperty("label").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, $"/api/v1/meals/{id}", etag: moved.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync($"/api/v1/meals/{id}")).StatusCode);
    }

    [Fact]
    public async Task Used_slot_deletion_archives_it_and_reordering_cannot_break_same_day_leftovers()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var (slots, _) = await MealScenario.Read(scenario.Admin, "/api/v1/meal-slots");
        var lunch = slots.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Lunch").GetProperty("id").GetGuid();
        var cooking = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/cooking", new { recipeId = scenario.RecipeId, date = "2026-11-02", slotId = lunch, plannedPrepared = "4", plannedEaten = "2" });
        Assert.Equal(HttpStatusCode.Created, cooking.StatusCode);
        var source = (await cooking.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var leftover = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/leftovers", new { sourceCookingEventId = source, sourceRevision = balance.GetProperty("sourceRevision").GetString(), date = "2026-11-02", slotId = scenario.SlotId, plannedPortions = "2" });
        Assert.Equal(HttpStatusCode.Created, leftover.StatusCode);
        var (_, slotTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/meal-slots/{scenario.SlotId}");
        var (_, calendarTag) = await MealScenario.Read(scenario.Admin, "/api/v1/meals?from=2026-11-02&to=2026-11-02");
        var reordered = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meal-slots/{scenario.SlotId}", new { name = "Dinner", position = 0 }, slotTag);
        Assert.Equal(HttpStatusCode.Conflict, reordered.StatusCode);
        Assert.Equal(calendarTag, (await MealScenario.Read(scenario.Admin, "/api/v1/meals?from=2026-11-02&to=2026-11-02")).ETag);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, $"/api/v1/meal-slots/{scenario.SlotId}", etag: slotTag)).StatusCode);
        Assert.True((await MealScenario.Read(scenario.Admin, $"/api/v1/meal-slots/{scenario.SlotId}")).Body.GetProperty("archived").GetBoolean());
        var rejected = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/other", new { date = "2026-11-04", slotId = scenario.SlotId, label = "New meal" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }

    [Fact]
    public async Task Planner_resources_and_conditional_reads_are_isolated_from_other_authenticated_households()
    {
        await using var scenario = await MealScenario.Create(postgres); var (cook, response) = await scenario.Cook();
        var id = cook.GetProperty("id").GetGuid(); var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        await using (var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options))
        {
            var user = new ApplicationUser(Guid.NewGuid(), "planner-outside@example.test");
            user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>().HashPassword(user, "outside-test-passphrase");
            var household = new Household(Guid.NewGuid(), "Outside", "UTC");
            db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Administrator)); await db.SaveChangesAsync();
        }
        using var outsider = scenario.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/auth/login", new { email = "planner-outside@example.test", password = "outside-test-passphrase" })).StatusCode);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/meals/{id}"); conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendAsync(conditional)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/cooking-events/{source}/leftovers")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/meal-slots/{scenario.SlotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await HouseholdContractTests.Write(outsider, HttpMethod.Put, $"/api/v1/meals/{id}/position", new { date = "2026-11-03", slotId = scenario.SlotId }, response.Headers.ETag!.ToString())).StatusCode);
        var foreignCreate = await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/meals/leftovers", new { sourceCookingEventId = source, sourceRevision = "1", date = "2026-11-03", slotId = scenario.SlotId, plannedPortions = "1" });
        Assert.Equal(HttpStatusCode.NotFound, foreignCreate.StatusCode);
    }
}
