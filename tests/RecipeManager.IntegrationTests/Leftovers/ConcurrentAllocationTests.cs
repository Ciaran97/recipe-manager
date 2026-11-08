using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;

namespace RecipeManager.IntegrationTests.Leftovers;

public sealed class ConcurrentAllocationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Source_actual_correction_racing_an_allocation_cannot_commit_both_against_one_revision()
    {
        await using var scenario = await MealScenario.Create(postgres); using var member = await scenario.Member();
        var (cook, _) = await scenario.Cook(); var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (balance, tag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var allocation = HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/meals/leftovers", new { sourceCookingEventId = source, sourceRevision = balance.GetProperty("sourceRevision").GetString(), date = "2026-11-03", slotId = scenario.SlotId, plannedPortions = "2" });
        var correction = HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/cooking-events/{source}/actuals", new { actualPrepared = "2", actualEaten = "1" }, tag);
        var responses = await Task.WhenAll(allocation, correction);
        Assert.Single(responses, x => x.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var (after, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Contains(after.GetProperty("availablePortions").GetString(), new[] { "0", "1" });
    }

    [Fact]
    public async Task Two_household_members_cannot_commit_the_same_available_portions()
    {
        await using var scenario = await MealScenario.Create(postgres); using var member = await scenario.Member();
        var (cook, _) = await scenario.Cook(); var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var body = new { sourceCookingEventId = source, sourceRevision = balance.GetProperty("sourceRevision").GetString(), date = "2026-11-03", slotId = scenario.SlotId, plannedPortions = "2" };
        var responses = await Task.WhenAll(HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/leftovers", body), HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/meals/leftovers", body));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var (remaining, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal("0", remaining.GetProperty("availablePortions").GetString());
        var (calendar, _) = await MealScenario.Read(member, "/api/v1/meals?from=2026-11-02&to=2026-11-04");
        Assert.Equal(2, calendar.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Conflicting_source_correction_rolls_back_and_invalid_chronology_creates_nothing()
    {
        await using var scenario = await MealScenario.Create(postgres); var (cook, _) = await scenario.Cook();
        var source = cook.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        object Allocation(string date) => new { sourceCookingEventId = source, sourceRevision = balance.GetProperty("sourceRevision").GetString(), date, slotId = scenario.SlotId, plannedPortions = "2" };
        var earlier = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/leftovers", Allocation("2026-11-01"));
        Assert.Equal(HttpStatusCode.Conflict, earlier.StatusCode);
        var allocated = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/leftovers", Allocation("2026-11-03"));
        Assert.Equal(HttpStatusCode.Created, allocated.StatusCode);
        var (_, tag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var correction = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/cooking-events/{source}/actuals", new { actualPrepared = "2", actualEaten = "1" }, tag);
        Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);
        var (unchanged, unchangedTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal(tag, unchangedTag); Assert.Equal("planned", unchanged.GetProperty("basis").GetString());
        Assert.Equal("0", unchanged.GetProperty("availablePortions").GetString());
        using var stranger = scenario.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync($"/api/v1/cooking-events/{source}/leftovers")).StatusCode);
    }
}
