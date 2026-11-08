using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Households;

public sealed class AuthenticationFoundationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Foreign_household_resource_returns_404_while_own_resource_is_visible()
    {
        var actor = await SeedAsync(HouseholdRole.Member);
        var other = await SeedAsync(HouseholdRole.Member);
        await using var factory = Factory();
        using var client = Client(factory);
        var cookie = await CookieAsync(client, actor.UserId);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/__fixture/households/" + actor.HouseholdId, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, "/__fixture/households/" + other.HouseholdId, cookie)).StatusCode);
    }

    [Fact]
    public async Task Signed_out_requests_are_denied_without_redirects()
    {
        await using var factory = Factory();
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/__fixture/current")).StatusCode);
    }

    [Fact]
    public async Task Membership_comes_from_database_not_a_claimed_household()
    {
        var actor = await SeedAsync(HouseholdRole.Member);
        await using var factory = Factory();
        using var client = Client(factory);
        var cookie = await CookieAsync(client, actor.UserId);
        using var response = await GetAsync(client, "/__fixture/current", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(actor.HouseholdId.ToString("D"), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_claimed_admin_role_cannot_override_member_permissions()
    {
        var actor = await SeedAsync(HouseholdRole.Member);
        await using var factory = Factory();
        using var client = Client(factory);
        var cookie = await CookieAsync(client, actor.UserId);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(client, "/__fixture/admin", cookie)).StatusCode);
    }

    [Theory]
    [InlineData("stamp")]
    [InlineData("inactive")]
    [InlineData("membership")]
    public async Task Revoked_account_or_membership_cannot_reuse_an_old_cookie(string cause)
    {
        var actor = await SeedAsync(HouseholdRole.Administrator);
        await using var factory = Factory();
        using var client = Client(factory);
        var cookie = await CookieAsync(client, actor.UserId);
        await using (var db = Context())
        {
            var user = await db.Users.SingleAsync(x => x.Id == actor.UserId);
            if (cause == "stamp")
            {
                user.SecurityStamp = Guid.NewGuid().ToString("N");
            }
            else if (cause == "inactive")
            {
                db.Entry(user).Property(x => x.IsActive).CurrentValue = false;
            }
            else
            {
                var member = await db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.UserId == actor.UserId);
                db.Entry(member).Property(x => x.IsActive).CurrentValue = false;
            }

            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, "/__fixture/current", cookie)).StatusCode);
    }

    [Fact]
    public async Task Expired_ticket_is_rejected_even_if_manually_sent()
    {
        var actor = await SeedAsync(HouseholdRole.Member);
        await using var factory = Factory();
        using var client = Client(factory);
        var cookie = await CookieAsync(client, actor.UserId, expired: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, "/__fixture/current", cookie)).StatusCode);
    }

    [Fact]
    public async Task Authenticated_unsafe_api_request_requires_a_csrf_token()
    {
        var actor = await SeedAsync(HouseholdRole.Member);
        await using var factory = Factory();
        using var client = Client(factory);
        var cookie = await CookieAsync(client, actor.UserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/__fixture/change");
        request.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Administrator_permission_does_not_require_an_admin_role_claim()
    {
        var actor = await SeedAsync(HouseholdRole.Administrator);
        await using var factory = Factory();
        using var client = Client(factory);
        var cookie = await CookieAsync(client, actor.UserId, roleClaim: false);
        Assert.Equal(HttpStatusCode.NoContent, (await GetAsync(client, "/__fixture/admin", cookie)).StatusCode);
    }

    [Fact]
    public async Task Valid_csrf_cookie_and_request_token_allow_an_authorized_write()
    {
        var actor = await SeedAsync(HouseholdRole.Member);
        await using var factory = Factory();
        using var client = Client(factory);
        var cookie = await CookieAsync(client, actor.UserId);
        using var bootstrap = await GetAsync(client, "/__fixture/csrf", cookie);
        bootstrap.EnsureSuccessStatusCode();
        var token = await bootstrap.Content.ReadFromJsonAsync<CsrfResponse>();
        var csrfCookie = bootstrap.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/__fixture/change");
        request.Headers.Add("Cookie", cookie + "; " + csrfCookie);
        request.Headers.Add("X-XSRF-TOKEN", token!.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
    }

    private sealed record CsrfResponse(string Token);

    private AuthenticationFactory Factory() => new(database.ConnectionString);
    private RecipeManagerDbContext Context() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(database.ConnectionString).Options);
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = false
    });

    private async Task<(Guid UserId, Guid HouseholdId)> SeedAsync(HouseholdRole role)
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid().ToString("N") + "@example.test");
        var household = new Household(Guid.NewGuid(), "Home", "UTC");
        db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, role));
        await db.SaveChangesAsync();
        return (user.Id, household.Id);
    }

    private static async Task<string> CookieAsync(HttpClient client, Guid userId, bool expired = false, bool roleClaim = true)
    {
        using var response = await client.GetAsync($"/__fixture/issue?userId={userId:D}&expired={expired}&omitRole={!roleClaim}");
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(IdentityConfiguration.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }

    private sealed class AuthenticationFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Database"] = connectionString }));
            builder.ConfigureServices(services =>
            {
                if (!services.Any(x => x.ServiceType == typeof(RecipeManagerDbContext)))
                    services.AddDbContext<RecipeManagerDbContext>(options => options.UseNpgsql(connectionString));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                if (!services.Any(x => x.ServiceType == typeof(CurrentMembership)))
                {
                    services.AddRecipeManagerAuthentication();
                }

                services.AddSingleton<IStartupFilter>(new FixtureRoutes());
            });
        }
    }

    // Test-server-only routes mint signed framework cookies; no fixture route is in the production API.
    private sealed class FixtureRoutes : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<ApiCsrfMiddleware>();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/__fixture/issue", async (HttpContext context, RecipeManagerDbContext db) =>
                {
                    var user = await db.Users.SingleAsync(x => x.Id == Guid.Parse(context.Request.Query["userId"].ToString()));
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
                        new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!),
                        new Claim("household_id", Guid.NewGuid().ToString("D")),
                        new Claim(ClaimTypes.Role, "Administrator")
                    };
                    var principal = new ClaimsPrincipal(new ClaimsIdentity(bool.TryParse(context.Request.Query["omitRole"].ToString(), out var omit) && omit
                        ? claims.Where(x => x.Type != ClaimTypes.Role) : claims, IdentityConstants.ApplicationScheme));
                    var expiry = bool.Parse(context.Request.Query["expired"].ToString()) ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddDays(1);
                    await context.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties { ExpiresUtc = expiry });
                    return Results.NoContent();
                }).AllowAnonymous();
                endpoints.MapGet("/__fixture/csrf", (HttpContext context, IAntiforgery antiforgery) =>
                    Results.Json(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }))
                    .RequireAuthorization(IdentityConfiguration.MemberPolicy);
                endpoints.MapGet("/__fixture/current", async (HttpContext context, CurrentMembership membership) =>
                    Results.Json(new { householdId = (await membership.ResolveAsync(context.User))?.HouseholdId }))
                    .RequireAuthorization(IdentityConfiguration.MemberPolicy);
                endpoints.MapGet("/__fixture/households/{id:guid}", async (Guid id, RecipeManagerDbContext db) =>
                    await db.Households.AsNoTracking().AnyAsync(x => x.Id == id) ? Results.Ok() : Results.NotFound())
                    .RequireAuthorization(IdentityConfiguration.MemberPolicy);
                endpoints.MapGet("/__fixture/admin", () => Results.NoContent()).RequireAuthorization(IdentityConfiguration.AdministratorPolicy);
                endpoints.MapPost("/api/v1/__fixture/change", () => Results.NoContent()).RequireAuthorization(IdentityConfiguration.MemberPolicy);
            });
            next(app);
        };
    }
}
