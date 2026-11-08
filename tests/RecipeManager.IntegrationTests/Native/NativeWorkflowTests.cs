using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;

namespace RecipeManager.IntegrationTests.Native;

public sealed class NativeWorkflowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Explicit_cookie_and_csrf_requests_share_etags_business_results_and_absolute_session_renewal()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var scenario = await MealScenario.Create(postgres, clock);
        using var native = new NativeRequests(scenario);
        var login = await native.Write(HttpMethod.Post, "/api/v1/auth/login", new { email = "admin@example.test", password = "long-test-only-passphrase" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("RecipeManager.Session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var sync = await native.Read("/api/v1/household/sync");
        Assert.Equal(HttpStatusCode.NotModified, (await native.Read("/api/v1/household/sync", sync.Headers.ETag!.ToString())).StatusCode);
        var generated = await native.Write(HttpMethod.Post, "/api/v1/shopping-lists/generate", new { name = "Native selected recipe", selection = new { kind = "recipes", entries = new[] { new { recipeId = scenario.RecipeId, servings = "2.125" } } } });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        var list = await generated.Content.ReadFromJsonAsync<JsonElement>(); var id = list.GetProperty("id").GetGuid();
        var web = await scenario.Admin.GetFromJsonAsync<JsonElement>("/api/v1/shopping-lists/" + id);
        Assert.Equal(list.GetProperty("items")[0].GetProperty("effectiveExact").GetRawText(), web.GetProperty("items")[0].GetProperty("effectiveExact").GetRawText());
        var item = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        var path = $"/api/v1/shopping-lists/{id}/items/{item}";
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await native.Write(HttpMethod.Put, path, new { purchased = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await native.Write(HttpMethod.Put, path, new { purchased = true }, generated.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await native.Write(HttpMethod.Put, path, new { purchased = false }, generated.Headers.ETag!.ToString())).StatusCode);
        clock.Advance(TimeSpan.FromDays(13));
        Assert.Equal(HttpStatusCode.OK, (await native.Read("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await native.Write(HttpMethod.Post, "/api/v1/auth/login", new { email = "admin@example.test", password = "long-test-only-passphrase" })).StatusCode);
        clock.Advance(TimeSpan.FromDays(2)); // Fresh sign-in renews the absolute ticket; ordinary reads do not.
        Assert.Equal(HttpStatusCode.OK, (await native.Read("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await scenario.Admin.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Native_conditional_cache_cannot_bypass_current_security_stamp()
    {
        await using var scenario = await MealScenario.Create(postgres);
        using var native = new NativeRequests(scenario);
        Assert.Equal(HttpStatusCode.OK, (await native.Write(HttpMethod.Post, "/api/v1/auth/login", new { email = "admin@example.test", password = "long-test-only-passphrase" })).StatusCode);
        var me = await (await native.Read("/api/v1/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        var sync = await native.Read("/api/v1/household/sync");
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var userId = me.GetProperty("id").GetGuid();
        var user = await db.Users.SingleAsync(x => x.Id == userId); user.SecurityStamp = Guid.NewGuid().ToString("N"); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await native.Read("/api/v1/household/sync", sync.Headers.ETag!.ToString())).StatusCode);
    }

    [Fact]
    public async Task Removed_membership_is_rejected_before_native_conditional_cache_evaluation()
    {
        await using var scenario = await MealScenario.Create(postgres);
        using var member = await scenario.Member();
        using var native = new NativeRequests(scenario);
        Assert.Equal(HttpStatusCode.OK, (await native.Write(HttpMethod.Post, "/api/v1/auth/login", new { email = "planner-member@example.test", password = "member-test-passphrase" })).StatusCode);
        var sync = await native.Read("/api/v1/household/sync");
        var rows = await scenario.Admin.GetFromJsonAsync<JsonElement>("/api/v1/household/members");
        var membership = rows.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("email").GetString() == "planner-member@example.test");
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete,
            "/api/v1/household/members/" + membership.GetProperty("id").GetGuid(), etag: membership.GetProperty("etag").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await native.Read("/api/v1/household/sync", sync.Headers.ETag!.ToString())).StatusCode);
    }

    private sealed class NativeRequests : IDisposable
    {
        private readonly HttpClient _client;
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
        public NativeRequests(MealScenario scenario) => _client = scenario.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        public Task<HttpResponseMessage> Read(string path, string? tag = null) => Send(HttpMethod.Get, path, null, tag, null);
        public async Task<HttpResponseMessage> Write(HttpMethod method, string path, object body, string? tag = null)
        {
            var bootstrap = await Read("/api/v1/auth/csrf"); bootstrap.EnsureSuccessStatusCode();
            var token = (await bootstrap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            return await Send(method, path, body, tag, token);
        }
        private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, string? tag, string? csrf)
        {
            using var request = new HttpRequestMessage(method, path);
            if (_cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(x => x.Key + "=" + x.Value)));
            if (body is not null) request.Content = JsonContent.Create(body);
            if (tag is not null) request.Headers.TryAddWithoutValidation(method == HttpMethod.Get ? "If-None-Match" : "If-Match", tag);
            if (csrf is not null) request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", csrf);
            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (var cookie in cookies)
                {
                    var pair = cookie.Split(';', 2)[0].Split('=', 2);
                    if (pair.Length == 2) _cookies[pair[0]] = pair[1];
                }
            return response;
        }
        public void Dispose() => _client.Dispose();
    }
}
