using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Households;

public sealed class SessionRevocationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(RevocationCause.Logout)]
    [InlineData(RevocationCause.PasswordChange)]
    [InlineData(RevocationCause.PasswordReset)]
    [InlineData(RevocationCause.MembershipRemoval)]
    [InlineData(RevocationCause.AccountDeactivation)]
    [InlineData(RevocationCause.SecurityStampRotation)]
    public async Task Every_revocation_cause_stops_broadcasts_invocations_and_reconnection(RevocationCause cause)
    {
        var actor = await Seed();
        await using var host = new HubHost(postgres.ConnectionString);
        var cookie = await Cookie(host, actor.UserId);
        await using var hub = Connect(host, cookie);
        await hub.StartAsync();
        await hub.InvokeAsync<string>("ConnectionIdentity");
        var registry = host.Services.GetRequiredService<AuthorizedConnectionRegistry>();
        var probe = host.Services.GetRequiredService<DispatchProbe>();
        await registry.DispatchHouseholdAsync(actor.HouseholdId, "HouseholdChanged", new { contentRevision = "1" });
        Assert.Equal(1, probe.Starts);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SessionRevocationService>().RevokeAsync(
                actor.UserId, cause, cause is RevocationCause.MembershipRemoval or RevocationCause.AccountDeactivation or RevocationCause.PasswordReset or RevocationCause.SecurityStampRotation ? null : actor,
                currentPassword: cause == RevocationCause.PasswordChange ? "original-test-passphrase" : null,
                newPassword: cause is RevocationCause.PasswordChange or RevocationCause.PasswordReset ? "replacement-test-passphrase" : null);
        }
        await registry.DispatchHouseholdAsync(actor.HouseholdId, "HouseholdChanged", new { contentRevision = "2" });
        Assert.Equal(1, probe.Starts);
        await Assert.ThrowsAnyAsync<Exception>(() => hub.InvokeAsync<string>("ConnectionIdentity"));
        await using var reconnect = Connect(host, cookie);
        await Assert.ThrowsAnyAsync<Exception>(() => reconnect.StartAsync());
        if (cause is not RevocationCause.MembershipRemoval and not RevocationCause.AccountDeactivation)
        {
            await using var fresh = Connect(host, await Cookie(host, actor.UserId));
            await fresh.StartAsync();
            Assert.NotEmpty(await fresh.InvokeAsync<string>("ConnectionIdentity"));
        }
    }

    [Fact]
    public async Task Revocation_waits_for_started_send_and_blocks_queued_send_after_durable_commit()
    {
        var actor = await Seed();
        await using var host = new HubHost(postgres.ConnectionString);
        await using var hub = Connect(host, await Cookie(host, actor.UserId));
        await hub.StartAsync();
        await hub.InvokeAsync<string>("ConnectionIdentity");
        var registry = host.Services.GetRequiredService<AuthorizedConnectionRegistry>();
        var probe = host.Services.GetRequiredService<DispatchProbe>();
        probe.Block = true;
        var firstSend = registry.DispatchHouseholdAsync(actor.HouseholdId, "HouseholdChanged", new { contentRevision = "1" });
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var scope = host.Services.CreateAsyncScope();
        var revocation = scope.ServiceProvider.GetRequiredService<SessionRevocationService>().RevokeAsync(actor.UserId, RevocationCause.Logout, actor);
        var queued = registry.DispatchHouseholdAsync(actor.HouseholdId, "HouseholdChanged", new { contentRevision = "2" });
        probe.Release.TrySetResult();
        await Task.WhenAll(firstSend, revocation, queued);
        await registry.DispatchHouseholdAsync(actor.HouseholdId, "HouseholdChanged", new { contentRevision = "3" });
        Assert.Equal(1, probe.Starts);
        await using var db = Context();
        Assert.NotEqual(actor.SecurityStamp, (await db.Users.SingleAsync(x => x.Id == actor.UserId)).SecurityStamp);
    }

    [Fact]
    public async Task Expired_credentials_cannot_establish_a_hub_connection()
    {
        var actor = await Seed();
        await using var host = new HubHost(postgres.ConnectionString);
        await using var hub = Connect(host, await Cookie(host, actor.UserId, true));
        await Assert.ThrowsAnyAsync<Exception>(() => hub.StartAsync());
    }

    [Fact]
    public async Task Stalled_transport_is_cancelled_before_revocation_can_commit()
    {
        var actor = await Seed();
        await using var host = new HubHost(postgres.ConnectionString);
        await using var hub = Connect(host, await Cookie(host, actor.UserId));
        await hub.StartAsync();
        await hub.InvokeAsync<string>("ConnectionIdentity");
        var registry = host.Services.GetRequiredService<AuthorizedConnectionRegistry>();
        var probe = host.Services.GetRequiredService<DispatchProbe>();
        probe.Block = true;
        var send = registry.DispatchHouseholdAsync(actor.HouseholdId, "HouseholdChanged", new { contentRevision = "1" });
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var scope = host.Services.CreateAsyncScope();
        var revoke = scope.ServiceProvider.GetRequiredService<SessionRevocationService>().RevokeAsync(actor.UserId, RevocationCause.Logout, actor);
        await Task.WhenAll(send, revoke).WaitAsync(TimeSpan.FromSeconds(8));
        await probe.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(probe.Cancelled);
        Assert.Equal(1, probe.Starts);
    }

    [Fact]
    public async Task Expiry_during_an_open_connection_prevents_further_dispatch()
    {
        var actor = await Seed();
        await using var host = new HubHost(postgres.ConnectionString);
        await using var hub = Connect(host, await Cookie(host, actor.UserId));
        await hub.StartAsync();
        await hub.InvokeAsync<string>("ConnectionIdentity");
        host.Clock.Advance(TimeSpan.FromHours(2));
        await host.Services.GetRequiredService<AuthorizedConnectionRegistry>()
            .DispatchHouseholdAsync(actor.HouseholdId, "HouseholdChanged", new { contentRevision = "1" });
        Assert.Equal(0, host.Services.GetRequiredService<DispatchProbe>().Starts);
    }

    [Fact]
    public async Task Noncooperative_transport_cannot_be_followed_by_a_successful_revocation_commit()
    {
        var actor = await Seed();
        await using var host = new HubHost(postgres.ConnectionString);
        var registry = host.Services.GetRequiredService<AuthorizedConnectionRegistry>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await registry.ConnectAsync(actor, "noncooperative-fixture", () => { }, (_, _, _) => release.Task);
        await registry.DispatchHouseholdAsync(actor.HouseholdId, "HouseholdChanged", new { contentRevision = "1" });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SessionRevocationService>();
        var failure = await Assert.ThrowsAsync<HouseholdConflictException>(() => service.RevokeAsync(actor.UserId, RevocationCause.Logout, actor));
        Assert.Equal("session_drain_pending", failure.Code);
        await using var db = Context();
        Assert.Equal(actor.SecurityStamp, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId)).SecurityStamp);
        release.SetResult();
        await service.RevokeAsync(actor.UserId, RevocationCause.Logout, actor);
        Assert.NotEqual(actor.SecurityStamp, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId)).SecurityStamp);
    }

    private RecipeManagerDbContext Context() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
    private async Task<RequestActor> Seed()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid() + "@example.test");
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, "original-test-passphrase");
        var administrator = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid() + "@example.test");
        var household = new Household(Guid.NewGuid(), "Household", "UTC");
        db.AddRange(user, administrator, household,
            new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Member),
            new Membership(Guid.NewGuid(), household.Id, administrator.Id, HouseholdRole.Administrator));
        await db.SaveChangesAsync();
        return new(user.Id, household.Id, user.SecurityStamp!, DateTimeOffset.UtcNow.AddHours(1));
    }
    private static async Task<string> Cookie(HubHost host, Guid userId, bool expired = false)
    {
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        using var response = await client.GetAsync($"/__revocation/issue?userId={userId}&expired={expired}");
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(IdentityConfiguration.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
    }
    private static HubConnection Connect(HubHost host, string cookie) => new HubConnectionBuilder().WithUrl("https://localhost/__revocation/hub", options =>
    {
        options.Transports = HttpTransportType.LongPolling;
        options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
        options.Headers["Cookie"] = cookie;
    }).Build();

    private sealed class HubHost(string connectionString) : WebApplicationFactory<Program>
    {
        public ControlledClock Clock { get; } = new(DateTimeOffset.UtcNow);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Database"] = connectionString }));
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                services.AddSingleton<DispatchProbe>();
                services.TryAddSingleton<AuthorizedConnectionRegistry>();
                services.TryAddScoped<SessionRevocationService>();
                services.AddSignalR(options => options.AddFilter<HouseholdHubFilter>());
                services.AddSingleton<IStartupFilter>(new Routes());
            });
        }
    }
    private sealed class Routes : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<ProtectedFixtureHub>("/__revocation/hub");
                endpoints.MapGet("/__revocation/issue", async (HttpContext context, RecipeManagerDbContext db) =>
                {
                    var user = await db.Users.SingleAsync(x => x.Id == Guid.Parse(context.Request.Query["userId"].ToString()));
                    var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!) }, IdentityConstants.ApplicationScheme));
                    await context.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties
                    { ExpiresUtc = bool.Parse(context.Request.Query["expired"].ToString()) ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddHours(1) });
                    return Results.NoContent();
                }).AllowAnonymous();
            });
            next(app);
        };
    }
}

[Authorize(Policy = IdentityConfiguration.MemberPolicy)]
public sealed class ProtectedFixtureHub(CurrentMembership membership, AuthorizedConnectionRegistry registry, IHubContext<ProtectedFixtureHub> hub, DispatchProbe probe) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var actor = await membership.ResolveActorAsync(Context.GetHttpContext(), Context.ConnectionAborted) ?? throw new HubException("session_invalid");
        await registry.ConnectAsync(actor, Context.ConnectionId, Context.Abort, async (method, payload, cancellation) =>
        {
            await probe.BeforeSend(cancellation);
            await hub.Clients.Client(Context.ConnectionId).SendCoreAsync(method, new[] { payload }, cancellation);
        }, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
    public string ConnectionIdentity() => Context.ConnectionId;
    public override Task OnDisconnectedAsync(Exception? exception) { registry.Disconnect(Context.ConnectionId); return base.OnDisconnectedAsync(exception); }
}

public sealed class DispatchProbe
{
    private int _starts;
    public int Starts => Volatile.Read(ref _starts);
    public bool Block { get; set; }
    public bool Cancelled { get; private set; }
    public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task BeforeSend(CancellationToken cancellation)
    {
        Interlocked.Increment(ref _starts);
        Started.TrySetResult();
        if (Block)
        {
            try { await Release.Task.WaitAsync(cancellation); }
            catch (OperationCanceledException) { Cancelled = true; CancellationObserved.TrySetResult(); throw; }
        }
    }
}
