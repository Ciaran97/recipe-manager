using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Persistence;

public sealed class CreationRetryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private RecipeManagerDbContext Context() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);

    [Fact]
    public async Task Replays_are_scoped_payload_bound_and_expire_after_24_hours()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid() + "@example.test");
        var household = new Household(Guid.NewGuid(), "Test", "UTC");
        db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Member));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        var actor = new RequestActor(user.Id, household.Id, user.SecurityStamp!, clock.GetUtcNow().AddDays(2));
        var boundary = new TransactionBoundary(db, clock);
        var store = new CreationRetryStore(db, clock, new EphemeralDataProtectionProvider());
        var key = Guid.NewGuid();
        var count = 0;
        Task<CreationOutcome> Create(CancellationToken _) { count++; return Task.FromResult(new CreationOutcome(201, "{\"id\":\"created\"}", "/api/v1/recipes/created", "\"1\"")); }
        Task<CreationOutcome> Execute(string route, string body) => boundary.ExecuteAsync(actor, false, [], ct =>
            store.ExecuteAsync(actor.HouseholdId, actor.UserId, route, key, Encoding.UTF8.GetBytes(body), Create, ct));
        var original = await Execute("/api/v1/recipes", "original");
        Assert.Equal(original, await Execute("/api/v1/recipes", "original"));
        Assert.Equal(1, count);
        await Assert.ThrowsAsync<RetryPayloadConflictException>(() => Execute("/api/v1/recipes", "changed"));
        Assert.Equal(1, count);
        await Execute("/api/v1/shopping-lists", "original");
        Assert.Equal(2, count);
        clock.Advance(TimeSpan.FromHours(24));
        await Execute("/api/v1/recipes", "changed");
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Failed_creation_does_not_leave_retry_record()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid() + "@example.test");
        var household = new Household(Guid.NewGuid(), "Test", "UTC");
        db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Member));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var actor = new RequestActor(user.Id, household.Id, user.SecurityStamp!, DateTimeOffset.UtcNow.AddHours(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TransactionBoundary(db, TimeProvider.System)
            .ExecuteAsync(actor, false, [], ct => new CreationRetryStore(db, TimeProvider.System, new EphemeralDataProtectionProvider()).ExecuteAsync(
                actor.HouseholdId, actor.UserId, "/api/v1/recipes", Guid.NewGuid(), new byte[] { 1 },
                _ => throw new InvalidOperationException("fixture"), ct)));
        Assert.Empty(await db.CreationRetries.ToListAsync());
    }
}
