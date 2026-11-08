using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Households;

public sealed class HouseholdContractTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private async Task<ApiFixture> Fresh()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(database.ConnectionString).Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        return new ApiFixture(database.ConnectionString, new Dictionary<string, string?> { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" });
    }
    private static HttpClient Client(ApiFixture host) => host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    internal static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string path, object? body = null, string? etag = null, Guid? retryKey = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (retryKey is not null) request.Headers.Add("Idempotency-Key", retryKey.Value.ToString());
        if (etag is not null) request.Headers.Add("If-Match", etag);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    internal static object SetupBody(string email = "admin@example.test") => new
    {
        setupToken = "test-only-operator-secret-which-is-never-a-production-default",
        email,
        password = "long-test-only-passphrase",
        householdName = "Our household",
        timezone = "Australia/Perth"
    };
    private static async Task Setup(HttpClient client)
        => Assert.Equal(HttpStatusCode.Created, (await Write(client, HttpMethod.Post, "/api/v1/setup", SetupBody())).StatusCode);

    [Fact]
    public async Task First_setup_creates_admin_and_closes_initial_registration()
    {
        await using var host = await Fresh();
        using var admin = Client(host);
        Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/v1/setup")).GetProperty("setupRequired").GetBoolean());
        await Setup(admin);
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal("Administrator", me.GetProperty("role").GetString());
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/v1/setup")).GetProperty("setupRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Write(admin, HttpMethod.Post, "/api/v1/setup", SetupBody("other@example.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(admin, HttpMethod.Post, "/api/v1/auth/register", new { })).StatusCode);
    }

    [Fact]
    public async Task Invitations_are_48_hour_single_use_and_members_cannot_administer_the_household()
    {
        await using var host = await Fresh();
        using var admin = Client(host);
        using var member = Client(host);
        using var outsider = Client(host);
        await Setup(admin);
        var issued = await Write(admin, HttpMethod.Post, "/api/v1/household/invitations", new { });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var invitation = await issued.Content.ReadFromJsonAsync<JsonElement>();
        Assert.InRange(invitation.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromHours(47.99), TimeSpan.FromHours(48.01));
        var token = invitation.GetProperty("token").GetString();
        object Accept(string email) => new { token, email, password = "member-test-passphrase", name = "Member" };
        Assert.Equal(HttpStatusCode.Created, (await Write(member, HttpMethod.Post, "/api/v1/invitations/accept", Accept("member@example.test"))).StatusCode);
        Assert.Equal("Member", (await member.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Write(outsider, HttpMethod.Post, "/api/v1/invitations/accept", Accept("outsider@example.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(member, HttpMethod.Post, "/api/v1/household/invitations", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(member, HttpMethod.Put, "/api/v1/household/settings", new { }, "\"1\"")).StatusCode);
        var members = await admin.GetFromJsonAsync<JsonElement>("/api/v1/household/members");
        var entries = members.GetProperty("items").EnumerateArray().ToArray();
        var adminRow = entries.Single(x => x.GetProperty("role").GetString() == "Administrator");
        var memberRow = entries.Single(x => x.GetProperty("role").GetString() == "Member");
        Assert.Equal(HttpStatusCode.Conflict, (await Write(admin, HttpMethod.Delete, "/api/v1/household/members/" + adminRow.GetProperty("id").GetString(), etag: adminRow.GetProperty("etag").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Write(admin, HttpMethod.Delete, "/api/v1/household/members/" + memberRow.GetProperty("id").GetString(), etag: memberRow.GetProperty("etag").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/household/settings")).StatusCode);
    }

    [Fact]
    public async Task Profile_updates_are_personal_and_ai_credentials_are_write_only()
    {
        await using var host = await Fresh();
        using var admin = Client(host);
        await Setup(admin);
        using var before = await admin.GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var profile = await Write(admin, HttpMethod.Put, "/api/v1/profile", new { name = "Ciaran", preferences = new { weekStartsOn = "Monday" } }, before.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Contains("Ciaran", await profile.Content.ReadAsStringAsync());
        using var settings = await admin.GetAsync("/api/v1/household/settings");
        var changed = await Write(admin, HttpMethod.Put, "/api/v1/household/settings", new
        {
            name = "Our household",
            timezone = "Australia/Perth",
            ai = new { enabled = true, provider = "OpenAICompatible", model = "fixture-model", baseUrl = "https://ai.example.test", apiKey = "private-provider-fixture-key" }
        }, settings.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.DoesNotContain("private-provider-fixture-key", await changed.Content.ReadAsStringAsync());
        Assert.DoesNotContain("private-provider-fixture-key", await admin.GetStringAsync("/api/v1/household/settings"));
    }

    [Fact]
    public async Task Lost_invitation_response_replays_the_same_link_and_cache_is_encrypted()
    {
        await using var host = await Fresh();
        using var admin = Client(host);
        await Setup(admin);
        var key = Guid.NewGuid();
        var first = await Write(admin, HttpMethod.Post, "/api/v1/household/invitations", new { }, retryKey: key);
        var second = await Write(admin, HttpMethod.Post, "/api/v1/household/invitations", new { }, retryKey: key);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        var token = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(database.ConnectionString).Options);
        Assert.Single(await db.Invitations.IgnoreQueryFilters().ToListAsync());
        var cache = Assert.Single(await db.CreationRetries.IgnoreQueryFilters().ToListAsync());
        Assert.DoesNotContain(token, cache.Body);
    }

    [Fact]
    public async Task Member_pagination_has_no_missing_or_duplicate_entries_and_rejects_invalid_limits()
    {
        await using var host = await Fresh();
        using var admin = Client(host);
        await Setup(admin);
        var householdId = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("householdId").GetGuid();
        await using (var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(database.ConnectionString).Options))
        {
            for (var index = 0; index < 31; index++)
            {
                var user = new ApplicationUser(Guid.NewGuid(), $"fixture-{index}@example.test");
                db.AddRange(user, new Membership(Guid.NewGuid(), householdId, user.Id, HouseholdRole.Member));
            }
            await db.SaveChangesAsync();
        }
        string? cursor = null;
        var seen = new HashSet<Guid>();
        do
        {
            var page = await admin.GetFromJsonAsync<JsonElement>("/api/v1/household/members?limit=10" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            var items = page.GetProperty("items").EnumerateArray().ToArray();
            Assert.InRange(items.Length, 1, 10);
            foreach (var item in items) Assert.True(seen.Add(item.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(32, seen.Count);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/v1/household/members?limit=101")).StatusCode);
    }

    [Fact]
    public async Task Authentication_failures_and_throttling_use_consistent_safe_problem_details()
    {
        await using var host = await Fresh();
        using var client = Client(host);
        using var denied = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var problem = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("authentication_required", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        for (var index = 0; index < 20; index++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Write(client, HttpMethod.Post, "/api/v1/auth/login",
                new { email = "unknown@example.test", password = "private-request-password" })).StatusCode);
        var throttled = await Write(client, HttpMethod.Post, "/api/v1/auth/login", new { email = "unknown@example.test", password = "private-request-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal("rate_limited", (await throttled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.DoesNotContain("private-request-password", await throttled.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logout_and_password_change_revoke_all_clients_and_allow_fresh_login()
    {
        await using var host = await Fresh();
        using var first = Client(host);
        using var second = Client(host);
        await Setup(first);
        var credentials = new { email = "admin@example.test", password = "long-test-only-passphrase" };
        Assert.Equal(HttpStatusCode.OK, (await Write(second, HttpMethod.Post, "/api/v1/auth/login", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Write(first, HttpMethod.Post, "/api/v1/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(first, HttpMethod.Post, "/api/v1/auth/login", credentials)).StatusCode);
        using var profile = await first.GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.NoContent, (await Write(first, HttpMethod.Post, "/api/v1/profile/password", new
        { currentPassword = "long-test-only-passphrase", newPassword = "replacement-test-passphrase" }, profile.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Write(second, HttpMethod.Post, "/api/v1/auth/login", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(second, HttpMethod.Post, "/api/v1/auth/login", new { credentials.email, password = "replacement-test-passphrase" })).StatusCode);
    }
}
