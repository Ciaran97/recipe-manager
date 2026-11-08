using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Households;

public sealed class MembershipServiceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private RecipeManagerDbContext Context() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);

    [Fact]
    public async Task Invitation_acceptance_is_single_use_and_removal_preserves_household()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        await using var host = new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?>
        { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" });
        HouseholdSession admin;
        await using (var scope = host.Services.CreateAsyncScope())
            admin = await scope.ServiceProvider.GetRequiredService<SetupHousehold>().CreateAsync(new(
                "test-only-operator-secret-which-is-never-a-production-default", "admin@example.test", "admin-test-passphrase", "Household", "UTC"));
        var stamp = (await db.Users.AsNoTracking().SingleAsync(x => x.Id == admin.Id)).SecurityStamp!;
        var actor = new RequestActor(admin.Id, admin.HouseholdId, stamp, DateTimeOffset.UtcNow.AddHours(1));
        InvitationIssued invitation;
        await using (var scope = host.Services.CreateAsyncScope())
            invitation = await scope.ServiceProvider.GetRequiredService<MembershipService>().IssueAsync(actor);
        Assert.InRange(invitation.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromHours(47.99), TimeSpan.FromHours(48.01));
        async Task<HouseholdSession?> Accept(string email)
        {
            await using var scope = host.Services.CreateAsyncScope();
            try { return await scope.ServiceProvider.GetRequiredService<MembershipService>().AcceptAsync(new(invitation.Token, email, "member-test-passphrase", "Member")); }
            catch (HouseholdConflictException) { return null; }
        }
        var accepted = await Task.WhenAll(Accept("first@example.test"), Accept("second@example.test"));
        var member = Assert.Single(accepted, x => x is not null)!;
        Assert.Equal(2, await db.Users.CountAsync());
        db.BindHouseholdScope(admin.HouseholdId);
        var memberRow = await db.Memberships.AsNoTracking().SingleAsync(x => x.UserId == member.Id);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MembershipService>().RemoveAsync(actor, memberRow.Id, memberRow.Revision);
        Assert.False((await db.Memberships.AsNoTracking().SingleAsync(x => x.UserId == member.Id)).IsActive);
        Assert.Single(await db.Households.ToListAsync());
        var adminRow = await db.Memberships.AsNoTracking().SingleAsync(x => x.UserId == admin.Id);
        await using var lastScope = host.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<HouseholdConflictException>(() => lastScope.ServiceProvider.GetRequiredService<MembershipService>()
            .RemoveAsync(actor, adminRow.Id, adminRow.Revision));
        await using var invalidScope = host.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<DomainValidationException>(() => invalidScope.ServiceProvider.GetRequiredService<MembershipService>()
            .AcceptAsync(new("not-an-invitation", "unknown@example.test", "member-test-passphrase", "Unknown")));
    }
}
