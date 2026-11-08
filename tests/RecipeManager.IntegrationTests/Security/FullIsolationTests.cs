using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;
using RecipeManager.IntegrationTests.Recipes;
using SkiaSharp;

namespace RecipeManager.IntegrationTests.Security;

public sealed class FullIsolationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Two_households_cannot_read_validate_or_reference_each_others_resources_and_members_keep_shared_editing_only()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var (meal, _) = await scenario.Cook();
        var recipe = (await MealScenario.Read(scenario.Admin, "/api/v1/recipes/" + scenario.RecipeId)).Body;
        var ingredient = recipe.GetProperty("ingredients")[0].GetProperty("ingredientId").GetGuid();
        var collection = await Create(scenario.Admin, "/api/v1/collections", new { name = "Private household collection" });
        var category = await Create(scenario.Admin, "/api/v1/ingredient-categories", new { name = "Private category" });
        var tag = await Create(scenario.Admin, "/api/v1/tags", new { name = "Private tag" });
        var list = await Create(scenario.Admin, "/api/v1/shopping-lists/generate", new { name = "Private shopping", selection = new { kind = "recipes", entries = new[] { new { recipeId = scenario.RecipeId, servings = "2" } } } });
        var image = await Upload(scenario.Admin);
        var handoffId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/import-handoffs", new { handoffId, capturedAt = now, inputHash = ImportInput.Hash(ImportSourceKind.Text, "Private import text") })).StatusCode);
        ImportDraftRead draft;
        await using (var scope = scenario.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == "admin@example.test");
            var household = await db.Memberships.IgnoreQueryFilters().Where(x => x.UserId == user.Id).Select(x => x.HouseholdId).SingleAsync();
            var actor = new RequestActor(user.Id, household, user.SecurityStamp!, now.AddHours(1));
            draft = await scope.ServiceProvider.GetRequiredService<ImportHandoffService>().SaveExtractionAsync(actor, new(ImportSourceKind.Text, "Private import text", handoffId), "{}", "[]", null);
            var outside = new ApplicationUser(Guid.NewGuid(), "security-outside@example.test");
            outside.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(outside, "outside-security-passphrase");
            var other = new Household(Guid.NewGuid(), "Outside household", "UTC");
            db.AddRange(outside, other, new Membership(Guid.NewGuid(), other.Id, outside.Id, HouseholdRole.Administrator)); await db.SaveChangesAsync();
        }
        using var outsider = scenario.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/auth/login", new { email = "security-outside@example.test", password = "outside-security-passphrase" })).StatusCode);
        var resources = new[] { "/api/v1/recipes/" + scenario.RecipeId, "/api/v1/ingredients/" + ingredient, "/api/v1/collections/" + collection,
            "/api/v1/tags/" + tag, "/api/v1/ingredient-categories/" + category, "/api/v1/shopping-lists/" + list, "/api/v1/images/" + image,
            "/api/v1/meal-slots/" + scenario.SlotId, "/api/v1/meals/" + meal.GetProperty("id").GetGuid(), "/api/v1/cooking-events/" + meal.GetProperty("cookingEvent").GetProperty("id").GetGuid() + "/leftovers", "/api/v1/import-handoffs/" + handoffId };
        foreach (var resource in resources)
        {
            var own = await scenario.Admin.GetAsync(resource); Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            using var conditional = new HttpRequestMessage(HttpMethod.Get, resource);
            if (own.Headers.ETag is { } etag) conditional.Headers.IfNoneMatch.Add(etag);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendAsync(conditional)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Gone, (await outsider.GetAsync("/api/v1/imports/" + draft.Id)).StatusCode);
        foreach (var resource in new[] { "recipes", "ingredients", "collections", "tags", "shopping-lists" })
            Assert.Empty((await outsider.GetFromJsonAsync<JsonElement>("/api/v1/" + resource)).GetProperty("items").EnumerateArray());
        Assert.Equal("Outside household", (await outsider.GetFromJsonAsync<JsonElement>("/api/v1/household/settings")).GetProperty("name").GetString());
        var sourceSync = await scenario.Admin.GetAsync("/api/v1/household/sync");
        using var foreignTag = new HttpRequestMessage(HttpMethod.Get, "/api/v1/household/sync"); foreignTag.Headers.IfNoneMatch.Add(sourceSync.Headers.ETag!);
        Assert.Equal(HttpStatusCode.OK, (await outsider.SendAsync(foreignTag)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/recipes", RecipeContractTests.RecipeWrite(ingredient))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Admin.PostAsJsonAsync("/api/v1/collections", new { name = "Missing CSRF" })).StatusCode);
        using var member = await scenario.Member();
        Assert.Equal(HttpStatusCode.Forbidden, (await HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/household/invitations", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/collections", new { name = "Member can edit" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/v1/import-handoffs/" + handoffId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/recipes/" + scenario.RecipeId)).StatusCode);
    }

    [Fact]
    public async Task Membership_removal_racing_shared_updates_stops_new_broadcasts_reconnection_and_cached_reads_without_deleting_shared_data()
    {
        await using var scenario = await MealScenario.Create(postgres); using var member = await scenario.Member();
        var login = await HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/auth/login", new { email = "planner-member@example.test", password = "member-test-passphrase" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("RecipeManager.Session=", StringComparison.Ordinal)).Split(';')[0];
        HubConnection Connect() => new HubConnectionBuilder().WithUrl("https://localhost/hubs/shopping", options =>
        {
            options.Transports = HttpTransportType.LongPolling; options.HttpMessageHandlerFactory = _ => scenario.Host.Server.CreateHandler(); options.Headers["Cookie"] = cookie;
        }).Build();
        await using var hub = Connect(); await hub.StartAsync(); await hub.InvokeAsync("SubscribeHousehold");
        var sentinel = Guid.NewGuid().ToString("N"); var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("HouseholdChanged", value => { if (value.TryGetProperty("sentinel", out var marker) && marker.GetString() == sentinel) received.TrySetResult(); });
        var sync = await member.GetAsync("/api/v1/household/sync");
        var members = await scenario.Admin.GetFromJsonAsync<JsonElement>("/api/v1/household/members");
        var target = members.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("email").GetString() == "planner-member@example.test");
        var recipe = await scenario.Admin.GetAsync("/api/v1/recipes/" + scenario.RecipeId);
        var removal = HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, "/api/v1/household/members/" + target.GetProperty("id").GetGuid(), etag: target.GetProperty("etag").GetString());
        var change = HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, "/api/v1/recipes/" + scenario.RecipeId + "/archive", new { archived = true }, recipe.Headers.ETag!.ToString());
        var outcomes = await Task.WhenAll(removal, change); Assert.Equal(HttpStatusCode.NoContent, outcomes[0].StatusCode); Assert.Equal(HttpStatusCode.OK, outcomes[1].StatusCode);
        var me = await scenario.Admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        await scenario.Host.Services.GetRequiredService<AuthorizedConnectionRegistry>().DispatchHouseholdAsync(me.GetProperty("householdId").GetGuid(), "HouseholdChanged", new { sentinel });
        await Task.Delay(250); Assert.False(received.Task.IsCompleted);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/household/sync"); conditional.Headers.IfNoneMatch.Add(sync.Headers.ETag!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.SendAsync(conditional)).StatusCode);
        await using var reconnect = Connect(); await Assert.ThrowsAnyAsync<Exception>(() => reconnect.StartAsync());
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync("/api/v1/recipes/" + scenario.RecipeId)).StatusCode);
    }
    private static async Task<Guid> Create(HttpClient client, string path, object body)
    {
        var response = await HouseholdContractTests.Write(client, HttpMethod.Post, path, body); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> Upload(HttpClient client)
    {
        using var bitmap = new SKBitmap(2, 3); using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var body = new MultipartFormDataContent(); using var content = new ByteArrayContent(png.ToArray()); content.Headers.ContentType = new("image/png"); body.Add(content, "file", "fixture.png");
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/images") { Content = body }; request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
