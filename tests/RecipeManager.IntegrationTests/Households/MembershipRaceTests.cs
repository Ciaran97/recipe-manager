using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Households;

public sealed class MembershipRaceTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private async Task<ApiFixture> Fresh()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(database.ConnectionString).Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        return new ApiFixture(database.ConnectionString, new Dictionary<string, string?> { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" });
    }
    private static HttpClient Client(ApiFixture host) => host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    [Fact]
    public async Task Concurrent_first_registration_creates_exactly_one_household_and_admin()
    {
        await using var host = await Fresh();
        using var first = Client(host);
        using var second = Client(host);
        var results = await Task.WhenAll(
            HouseholdContractTests.Write(first, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody("first@example.test")),
            HouseholdContractTests.Write(second, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody("second@example.test")));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(database.ConnectionString).Options);
        Assert.Single(await db.Households.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.Memberships.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_invitation_acceptance_creates_one_personal_account()
    {
        await using var host = await Fresh();
        using var admin = Client(host);
        using var first = Client(host);
        using var second = Client(host);
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);
        var invite = await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/household/invitations", new { });
        var token = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        object Body(string email) => new { token, email, password = "personal-test-passphrase", name = "Member" };
        var results = await Task.WhenAll(
            HouseholdContractTests.Write(first, HttpMethod.Post, "/api/v1/invitations/accept", Body("one@example.test")),
            HouseholdContractTests.Write(second, HttpMethod.Post, "/api/v1/invitations/accept", Body("two@example.test")));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(database.ConnectionString).Options);
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(2, await db.Memberships.IgnoreQueryFilters().CountAsync());
    }
    [Fact]
    public async Task Concurrent_member_removals_have_one_winner_and_revoke_access()
    {
        await using var host = await Fresh();
        using var admin = Client(host);
        using var otherAdminDevice = Client(host);
        using var member = Client(host);
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(otherAdminDevice, HttpMethod.Post, "/api/v1/auth/login",
            new { email = "admin@example.test", password = "long-test-only-passphrase" })).StatusCode);
        var issued = await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/household/invitations", new { });
        var token = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/invitations/accept",
            new { token, email = "member@example.test", password = "personal-test-passphrase", name = "Member" })).StatusCode);
        var entries = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/household/members")).GetProperty("items").EnumerateArray().ToArray();
        var row = entries.Single(x => x.GetProperty("role").GetString() == "Member");
        var path = "/api/v1/household/members/" + row.GetProperty("id").GetString();
        var etag = row.GetProperty("etag").GetString();
        var results = await Task.WhenAll(
            HouseholdContractTests.Write(admin, HttpMethod.Delete, path, etag: etag),
            HouseholdContractTests.Write(otherAdminDevice, HttpMethod.Delete, path, etag: etag));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, results.Count(x => x.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound));
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/v1/auth/me")).StatusCode);
    }

}
