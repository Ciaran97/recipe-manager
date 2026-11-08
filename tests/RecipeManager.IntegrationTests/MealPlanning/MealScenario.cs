using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.MealPlanning;

internal sealed class MealScenario(ApiFixture host, HttpClient admin, Guid recipeId, Guid slotId) : IAsyncDisposable
{
    public ApiFixture Host { get; } = host;
    public HttpClient Admin { get; } = admin;
    public Guid RecipeId { get; } = recipeId;
    public Guid SlotId { get; } = slotId;
    public static async Task<MealScenario> Create(PostgresFixture postgres, TimeProvider? clock = null, Action<IServiceCollection>? configure = null)
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync();
        var host = new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?> { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" }, clock, configure);
        var admin = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        try
        {
            Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);
            var ingredient = await RecipeContractTests.Ingredient(admin);
            var recipe = await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/recipes", RecipeContractTests.RecipeWrite(ingredient));
            Assert.Equal(HttpStatusCode.Created, recipe.StatusCode);
            var recipeId = (await recipe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var slots = await admin.GetAsync("/api/v1/meal-slots"); Assert.Equal(HttpStatusCode.OK, slots.StatusCode);
            var slot = (await slots.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Dinner");
            return new(host, admin, recipeId, slot.GetProperty("id").GetGuid());
        }
        catch { admin.Dispose(); await host.DisposeAsync(); throw; }
    }
    public async Task<HttpClient> Member()
    {
        var invite = await HouseholdContractTests.Write(Admin, HttpMethod.Post, "/api/v1/household/invitations", new { });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var token = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var member = Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var accepted = await HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/invitations/accept", new { token, email = "planner-member@example.test", password = "member-test-passphrase", name = "Planner member" });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode); return member;
    }
    public async Task<(JsonElement Meal, HttpResponseMessage Response)> Cook(string date = "2026-11-02")
    {
        var response = await HouseholdContractTests.Write(Admin, HttpMethod.Post, "/api/v1/meals/cooking", new { recipeId = RecipeId, date, slotId = SlotId, plannedPrepared = "4", plannedEaten = "2" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(), response);
    }
    public static async Task<(JsonElement Body, string ETag)> Read(HttpClient client, string path)
    {
        var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(), response.Headers.ETag!.ToString());
    }
    public async ValueTask DisposeAsync() { Admin.Dispose(); await Host.DisposeAsync(); }
}
