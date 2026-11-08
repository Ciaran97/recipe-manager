using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Households;

public sealed class HouseholdPersistenceTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Migration_seeds_exactly_one_unconfigured_installation()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var installation = Assert.Single(await db.Installations.ToListAsync());
        Assert.Equal(Installation.SingletonId, installation.Id);
        Assert.Null(installation.SetupCompletedAt);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Normalized_email_is_unique_in_the_database()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        db.Users.Add(new ApplicationUser(Guid.NewGuid(), email));
        await db.SaveChangesAsync();
        var duplicate = new ApplicationUser(Guid.NewGuid(), email.ToUpperInvariant())
        {
            UserName = "different-username",
            NormalizedUserName = "DIFFERENT-USERNAME"
        };
        db.Users.Add(duplicate);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Invitation_cannot_reference_a_creator_membership_from_another_household()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid().ToString("N") + "@example.test");
        var first = new Household(Guid.NewGuid(), "First", "Australia/Perth");
        var other = new Household(Guid.NewGuid(), "Other", "UTC");
        db.AddRange(user, first, other, new Membership(Guid.NewGuid(), first.Id, user.Id, HouseholdRole.Administrator));
        await db.SaveChangesAsync();
        db.Invitations.Add(new Invitation(Guid.NewGuid(), other.Id, user.Id, new string('A', 64), DateTimeOffset.UtcNow.AddDays(2)));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task User_cannot_have_two_household_memberships_in_v1()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid().ToString("N") + "@example.test");
        var first = new Household(Guid.NewGuid(), "First", "UTC");
        var other = new Household(Guid.NewGuid(), "Other", "UTC");
        db.AddRange(user, first, other, new Membership(Guid.NewGuid(), first.Id, user.Id, HouseholdRole.Member));
        await db.SaveChangesAsync();
        db.Memberships.Add(new Membership(Guid.NewGuid(), other.Id, user.Id, HouseholdRole.Member));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Invitation_expiry_preserves_the_instant_in_utc()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid().ToString("N") + "@example.test");
        var household = new Household(Guid.NewGuid(), "Home", "Australia/Perth");
        var expected = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(8));
        var invitation = new Invitation(Guid.NewGuid(), household.Id, user.Id, new string('B', 64), expected);
        db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Administrator), invitation);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var saved = await db.Invitations.IgnoreQueryFilters().SingleAsync(x => x.Id == invitation.Id);
        Assert.Equal(expected.ToUniversalTime(), saved.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, saved.ExpiresAt.Offset);
    }

    private RecipeManagerDbContext Context() => new(
        new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(database.ConnectionString).Options);
}
