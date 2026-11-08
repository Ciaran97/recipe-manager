using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;

namespace RecipeManager.IntegrationTests.Shopping;

public sealed class RealtimeTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Committed_changes_reach_list_subscribers_and_personal_changes_reach_only_the_same_users_devices()
    {
        await using var scenario = await MealScenario.Create(postgres); using var member = await scenario.Member();
        await scenario.Cook();
        var created = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/shopping-lists/generate", new
        {
            name = "Live list",
            selection = new { kind = "mealRange", from = "2026-11-02", to = "2026-11-04" }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var list = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = list.GetProperty("id").GetGuid();
        var item = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        var cookie = await Login(scenario, "admin@example.test", "long-test-only-passphrase");
        var memberCookie = await Login(scenario, "planner-member@example.test", "member-test-passphrase");
        await using var own = Connect(scenario, cookie); await using var second = Connect(scenario, cookie); await using var other = Connect(scenario, memberCookie);
        await Task.WhenAll(own.StartAsync(), second.StartAsync(), other.StartAsync());
        await Task.WhenAll(own.InvokeAsync("SubscribeHousehold"), second.InvokeAsync("SubscribeHousehold"), other.InvokeAsync("SubscribeHousehold"));
        await own.InvokeAsync("SubscribeList", id); await second.InvokeAsync("SubscribeList", id);
        var ownList = Completion(); var otherList = Completion(); var ownPersonal = Completion(); var secondPersonal = Completion(); var otherPersonal = Completion();
        var householdCount = 0;
        own.On<JsonElement>("ShoppingListChanged", value => ownList.TrySetResult(value));
        other.On<JsonElement>("ShoppingListChanged", value => otherList.TrySetResult(value));
        own.On<JsonElement>("PersonalChanged", value => ownPersonal.TrySetResult(value));
        second.On<JsonElement>("PersonalChanged", value => secondPersonal.TrySetResult(value));
        other.On<JsonElement>("PersonalChanged", value => otherPersonal.TrySetResult(value));
        other.On<JsonElement>("HouseholdChanged", _ => Interlocked.Increment(ref householdCount));
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(member, HttpMethod.Put, $"/api/v1/shopping-lists/{id}/items/{item}", new { purchased = true }, created.Headers.ETag!.ToString())).StatusCode);
        var changed = await ownList.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(id, changed.GetProperty("listId").GetGuid()); Assert.Equal("2", changed.GetProperty("revision").GetString());
        Assert.True(changed.TryGetProperty("changeId", out _));
        await Task.Delay(250); Assert.False(otherList.Task.IsCompleted); Assert.True(Volatile.Read(ref householdCount) > 0);
        var before = Volatile.Read(ref householdCount); var (_, recipeTag) = await MealScenario.Read(scenario.Admin, "/api/v1/recipes/" + scenario.RecipeId);
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/recipes/{scenario.RecipeId}/favourite", new { favourite = true }, recipeTag)).StatusCode);
        await Task.WhenAll(ownPersonal.Task, secondPersonal.Task).WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(250); Assert.False(otherPersonal.Task.IsCompleted); Assert.Equal(before, Volatile.Read(ref householdCount));
        await Assert.ThrowsAnyAsync<Exception>(() => own.InvokeAsync("SubscribeList", Guid.NewGuid()));
        var deleted = Completion(); own.On<JsonElement>("ShoppingListDeleted", value => deleted.TrySetResult(value));
        var (_, currentTag) = await MealScenario.Read(scenario.Admin, $"/api/v1/shopping-lists/{id}");
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, $"/api/v1/shopping-lists/{id}", etag: currentTag)).StatusCode);
        Assert.Equal("3", (await deleted.Task.WaitAsync(TimeSpan.FromSeconds(2))).GetProperty("revision").GetString());
    }

    [Theory]
    [InlineData(RevocationCause.Logout)]
    [InlineData(RevocationCause.PasswordChange)]
    [InlineData(RevocationCause.PasswordReset)]
    [InlineData(RevocationCause.MembershipRemoval)]
    [InlineData(RevocationCause.AccountDeactivation)]
    [InlineData(RevocationCause.SecurityStampRotation)]
    public async Task Every_revocation_cause_terminates_production_connections_and_blocks_old_cookie_reconnection(RevocationCause cause)
    {
        await using var scenario = await MealScenario.Create(postgres); using var member = await scenario.Member();
        var subject = await member.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        var admin = await scenario.Admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        var cookie = await Login(scenario, "planner-member@example.test", "member-test-passphrase");
        await using var hub = Connect(scenario, cookie); await hub.StartAsync(); await hub.InvokeAsync("SubscribeHousehold");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        await using (var scope = scenario.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
            async Task<RequestActor> Actor(JsonElement identity)
            {
                var id = identity.GetProperty("id").GetGuid();
                var stamp = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => x.SecurityStamp).SingleAsync();
                return new(id, identity.GetProperty("householdId").GetGuid(), stamp!, DateTimeOffset.UtcNow.AddHours(1));
            }
            var requester = cause == RevocationCause.MembershipRemoval ? await Actor(admin)
                : cause is RevocationCause.Logout or RevocationCause.PasswordChange ? await Actor(subject) : null;
            await scope.ServiceProvider.GetRequiredService<SessionRevocationService>().RevokeAsync(subject.GetProperty("id").GetGuid(), cause, requester,
                currentPassword: cause == RevocationCause.PasswordChange ? "member-test-passphrase" : null,
                newPassword: cause is RevocationCause.PasswordChange or RevocationCause.PasswordReset ? "replacement-test-passphrase" : null);
        }
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await using var old = Connect(scenario, cookie); await Assert.ThrowsAnyAsync<Exception>(() => old.StartAsync());
        if (cause is not RevocationCause.MembershipRemoval and not RevocationCause.AccountDeactivation)
        {
            var password = cause is RevocationCause.PasswordChange or RevocationCause.PasswordReset ? "replacement-test-passphrase" : "member-test-passphrase";
            await using var fresh = Connect(scenario, await Login(scenario, "planner-member@example.test", password));
            await fresh.StartAsync(); await fresh.InvokeAsync("SubscribeHousehold");
        }
    }

    [Fact]
    public async Task Conditional_sync_recovers_accepted_shopping_changes_missed_while_disconnected()
    {
        await using var scenario = await MealScenario.Create(postgres); using var member = await scenario.Member();
        var created = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/shopping-lists", new { name = "Disconnected shopping" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var list = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = list.GetProperty("id").GetGuid();
        var (before, syncTag) = await MealScenario.Read(member, "/api/v1/household/sync");
        var added = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, $"/api/v1/shopping-lists/{id}/items", new { label = "Bread" }, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/household/sync"); conditional.Headers.TryAddWithoutValidation("If-None-Match", syncTag);
        var recovered = await member.SendAsync(conditional); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var state = await recovered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(before.GetProperty("shoppingRevision").GetString(), state.GetProperty("shoppingRevision").GetString());
        var (current, _) = await MealScenario.Read(member, $"/api/v1/shopping-lists/{id}");
        Assert.Equal("Bread", Assert.Single(current.GetProperty("items").EnumerateArray()).GetProperty("label").GetString());
    }

    [Fact]
    public async Task Logout_disconnects_active_production_connections_and_old_cookie_cannot_reconnect()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var cookie = await Login(scenario, "admin@example.test", "long-test-only-passphrase");
        await using var hub = Connect(scenario, cookie); await hub.StartAsync(); await hub.InvokeAsync("SubscribeHousehold");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/auth/logout")).StatusCode);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var me = await scenario.Admin.GetAsync("/api/v1/auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        await using var stale = Connect(scenario, cookie); await Assert.ThrowsAnyAsync<Exception>(() => stale.StartAsync());
        await using var fresh = Connect(scenario, await Login(scenario, "admin@example.test", "long-test-only-passphrase"));
        await fresh.StartAsync(); await fresh.InvokeAsync("SubscribeHousehold");
    }

    [Fact]
    public async Task Idle_expired_connection_is_closed_without_waiting_for_a_broadcast_and_cross_origin_start_is_denied()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var scenario = await MealScenario.Create(postgres, clock);
        var cookie = await Login(scenario, "admin@example.test", "long-test-only-passphrase");
        await using var forbidden = Connect(scenario, cookie, "https://foreign.example.test");
        await Assert.ThrowsAnyAsync<Exception>(() => forbidden.StartAsync());
        await using var hub = Connect(scenario, cookie); await hub.StartAsync(); await hub.InvokeAsync("SubscribeHousehold");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        clock.Advance(TimeSpan.FromDays(15));
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static TaskCompletionSource<JsonElement> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task<string> Login(MealScenario scenario, string email, string password)
    {
        using var client = scenario.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var response = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(IdentityConfiguration.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
    }
    private static HubConnection Connect(MealScenario scenario, string cookie, string origin = "https://localhost") => new HubConnectionBuilder()
        .WithUrl("https://localhost/hubs/shopping", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => scenario.Host.Server.CreateHandler();
            options.Headers["Cookie"] = cookie; options.Headers["Origin"] = origin;
        }).Build();
}
