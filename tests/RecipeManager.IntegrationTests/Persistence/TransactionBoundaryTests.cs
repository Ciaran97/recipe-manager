using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Persistence;

public sealed class TransactionBoundaryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private RecipeManagerDbContext Context() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>()
        .UseNpgsql(postgres.ConnectionString).Options);

    private async Task<RequestActor> Seed(RecipeManagerDbContext db)
    {
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid() + "@example.test");
        var household = new Household(Guid.NewGuid(), "Test", "UTC");
        db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Member));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(user.Id, household.Id, user.SecurityStamp!, DateTimeOffset.UtcNow.AddHours(1));
    }

    [Fact]
    public async Task Locks_are_sorted_and_success_is_committed()
    {
        await using var db = Context();
        var actor = await Seed(db);
        var order = new List<string>();
        var boundary = new TransactionBoundary(db, TimeProvider.System);
        var locks = new[] {
            new AggregateLock("z", Guid.NewGuid(), _ => { order.Add("z"); return Task.CompletedTask; }),
            new AggregateLock("a", Guid.NewGuid(), _ => { order.Add("a"); return Task.CompletedTask; }) };
        await boundary.ExecuteAsync(actor, false, locks, async ct =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE households.\"Households\" SET \"Name\" = 'Committed' WHERE \"Id\" = {actor.HouseholdId}", ct);
            return 1;
        });
        Assert.Equal(new[] { "a", "z" }, order);
        Assert.Equal("Committed", (await db.Households.SingleAsync()).Name);
    }

    [Fact]
    public async Task Failure_rolls_back_and_is_not_retried()
    {
        await using var db = Context();
        var actor = await Seed(db);
        var attempts = 0;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new TransactionBoundary(db, TimeProvider.System)
            .ExecuteAsync<int>(actor, false, [], async ct =>
            {
                attempts++;
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM households.\"Memberships\" WHERE \"HouseholdId\" = {actor.HouseholdId}", ct);
                throw new DbUpdateConcurrencyException();
            }));
        Assert.Equal(1, attempts);
        Assert.Single(await db.Memberships.ToListAsync());
    }

    [Theory]
    [InlineData("40001", 2, 3)]
    [InlineData("40P01", 2, 3)]
    [InlineData("40001", 3, 3)]
    [InlineData("23505", 1, 1)]
    public async Task Only_transient_database_failures_retry_with_three_attempt_limit(string code, int failures, int expected)
    {
        await using var db = Context();
        var actor = await Seed(db);
        var attempts = 0;
        var task = new TransactionBoundary(db, TimeProvider.System).ExecuteAsync(actor, false, [], async ct =>
        {
            attempts++;
            if (attempts <= failures)
            {
                // Test-controlled SQLSTATE, never application input.
                var sql = code switch
                {
                    "40001" => "DO $$ BEGIN RAISE EXCEPTION 'fixture' USING ERRCODE = '40001'; END $$",
                    "40P01" => "DO $$ BEGIN RAISE EXCEPTION 'fixture' USING ERRCODE = '40P01'; END $$",
                    _ => "DO $$ BEGIN RAISE EXCEPTION 'fixture' USING ERRCODE = '23505'; END $$"
                };
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            return attempts;
        });
        if (failures >= expected) await Assert.ThrowsAsync<Npgsql.PostgresException>(() => task);
        else Assert.Equal(expected, await task);
        Assert.Equal(expected, attempts);
    }

    [Fact]
    public async Task Concurrent_commands_revalidate_state_after_waiting_for_household_lock()
    {
        await using var first = Context();
        await using var second = Context();
        var actor = await Seed(first);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCommand = new TransactionBoundary(first, TimeProvider.System).ExecuteAsync(actor, false, [], async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            await first.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.\"Users\" SET \"SecurityStamp\" = 'rotated' WHERE \"Id\" = {actor.UserId}", ct);
            return 1;
        });
        await entered.Task;
        var secondCommand = new TransactionBoundary(second, TimeProvider.System)
            .ExecuteAsync(actor, false, [], _ => Task.FromResult(2));
        release.SetResult();
        Assert.Equal(1, await firstCommand);
        await Assert.ThrowsAsync<TransactionAccessException>(() => secondCommand);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("stamp")]
    [InlineData("foreign")]
    [InlineData("admin")]
    public async Task Revalidates_actor_under_household_lock(string failure)
    {
        await using var db = Context();
        var actor = await Seed(db);
        actor = new(actor.UserId, failure == "foreign" ? Guid.NewGuid() : actor.HouseholdId,
            failure == "stamp" ? "invalid" : actor.SecurityStamp,
            failure == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-1) : actor.ExpiresAt);
        await Assert.ThrowsAsync<TransactionAccessException>(() => new TransactionBoundary(db, TimeProvider.System)
            .ExecuteAsync(actor, failure == "admin", [], _ => Task.FromResult(1)));
    }
}
