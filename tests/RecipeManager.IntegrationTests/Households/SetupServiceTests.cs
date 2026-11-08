using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Households;

public sealed class SetupServiceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Setup_service_serializes_initial_registration_and_invalid_secret_leaves_no_account()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.MigrateAsync();
        await using var host = new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?>
        { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" });
        async Task<HouseholdSession> Create(string token, string email)
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<SetupHousehold>().CreateAsync(new(token, email,
                "long-test-only-passphrase", "Our household", "Australia/Perth"));
        }
        await Assert.ThrowsAsync<Core.Shared.DomainValidationException>(() => Create("incorrect", "invalid@example.test"));
        Assert.Empty(await db.Users.ToListAsync());
        var first = Create("test-only-operator-secret-which-is-never-a-production-default", "first@example.test");
        var second = Create("test-only-operator-secret-which-is-never-a-production-default", "second@example.test");
        var results = await Task.WhenAll(Observe(first), Observe(second));
        Assert.Equal(1, results.Count(x => x));
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.Households.IgnoreQueryFilters().ToListAsync());
        Assert.NotNull((await db.Installations.SingleAsync()).SetupCompletedAt);
    }

    private static async Task<bool> Observe(Task<HouseholdSession> task)
    {
        try { await task; return true; }
        catch (HouseholdConflictException) { return false; }
    }
}
