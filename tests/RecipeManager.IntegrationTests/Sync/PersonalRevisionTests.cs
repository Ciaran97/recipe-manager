using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;

namespace RecipeManager.IntegrationTests.Sync;

public sealed class PersonalRevisionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Committed_personal_notification_contract_targets_only_the_authenticated_actor()
    {
        await using var scenario = await MealScenario.Create(postgres); using var member = await scenario.Member();
        var own = await scenario.Admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        var other = await member.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var userId = own.GetProperty("id").GetGuid(); var householdId = own.GetProperty("householdId").GetGuid();
        var stamp = await db.Users.Where(x => x.Id == userId).Select(x => x.SecurityStamp).SingleAsync();
        var actor = new RequestActor(userId, householdId, stamp!, DateTimeOffset.UtcNow.AddHours(1));
        var revisions = new RevisionCoordinator(db); var transactions = new TransactionBoundary(db, TimeProvider.System, revisions);
        await transactions.ExecuteAsync(actor, false, [], async ct => { await revisions.PersonalAsync(householdId, userId, ct); return 1; });
        var invalidation = Assert.Single(revisions.DrainCommitted());
        Assert.Equal(userId, invalidation.UserId); Assert.Equal(householdId, invalidation.HouseholdId); Assert.Equal(ChangedDomain.None, invalidation.Domains);
        await Assert.ThrowsAsync<TransactionAccessException>(() => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            await revisions.PersonalAsync(householdId, other.GetProperty("id").GetGuid(), ct); return 1;
        }));
        Assert.Empty(revisions.DrainCommitted());
    }
    [Fact]
    public async Task Own_favourites_and_preferences_invalidate_both_own_devices_but_not_another_members_cache()
    {
        await using var scenario = await MealScenario.Create(postgres);
        using var member = await scenario.Member();
        using var second = scenario.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(second, HttpMethod.Post, "/api/v1/auth/login", new { email = "admin@example.test", password = "long-test-only-passphrase" })).StatusCode);
        var (own, ownTag) = await MealScenario.Read(scenario.Admin, "/api/v1/household/sync");
        var (_, memberTag) = await MealScenario.Read(member, "/api/v1/household/sync");
        var (_, secondTag) = await MealScenario.Read(second, "/api/v1/household/sync");
        Assert.Equal(ownTag, secondTag); Assert.NotEqual(ownTag, memberTag);
        using var unchanged = await Conditional(second, ownTag);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.True(unchanged.Headers.CacheControl?.Private); Assert.Contains("Cookie", unchanged.Headers.Vary);
        var (_, recipeTag) = await MealScenario.Read(scenario.Admin, "/api/v1/recipes/" + scenario.RecipeId);
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/recipes/{scenario.RecipeId}/favourite", new { favourite = true }, recipeTag)).StatusCode);
        using var favouriteChanged = await Conditional(second, ownTag);
        Assert.Equal(HttpStatusCode.OK, favouriteChanged.StatusCode);
        var favouriteSync = await favouriteChanged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(own.GetProperty("contentRevision").GetString(), favouriteSync.GetProperty("contentRevision").GetString());
        Assert.NotEqual(own.GetProperty("ownPreferencesRevision").GetString(), favouriteSync.GetProperty("ownPreferencesRevision").GetString());
        Assert.Equal(HttpStatusCode.NotModified, (await Conditional(member, memberTag)).StatusCode);
        var (_, profileTag) = await MealScenario.Read(scenario.Admin, "/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, "/api/v1/profile", new { name = "Updated on another device", preferences = new { compactRecipes = true } }, profileTag)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Conditional(second, favouriteChanged.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotModified, (await Conditional(member, memberTag)).StatusCode);
        await scenario.Cook();
        Assert.Equal(HttpStatusCode.OK, (await Conditional(member, memberTag)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Conditional(second, favouriteChanged.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Conditional(second, ownTag)).StatusCode);
    }

    [Fact]
    public async Task Expired_credentials_cannot_receive_a_conditional_304()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var scenario = await MealScenario.Create(postgres, clock);
        var (_, tag) = await MealScenario.Read(scenario.Admin, "/api/v1/household/sync");
        clock.Advance(TimeSpan.FromDays(15));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Conditional(scenario.Admin, tag)).StatusCode);
    }

    private static Task<HttpResponseMessage> Conditional(HttpClient client, string tag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/household/sync");
        request.Headers.TryAddWithoutValidation("If-None-Match", tag);
        return client.SendAsync(request);
    }
}
