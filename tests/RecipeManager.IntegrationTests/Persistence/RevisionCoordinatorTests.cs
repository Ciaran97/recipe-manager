using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Persistence;

public sealed class RevisionCoordinatorTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Shopping_list_metadata_is_committed_once_and_rollback_discards_it()
    {
        await using var db = Context(); var actor = await Seed(db);
        var revisions = new RevisionCoordinator(db); var boundary = new TransactionBoundary(db, TimeProvider.System, revisions);
        var list = Guid.NewGuid();
        await boundary.ExecuteAsync(actor, false, [], async ct =>
        {
            await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Shopping, false, ct);
            revisions.ShoppingListChanged(actor.HouseholdId, list, 2, false);
            revisions.ShoppingListChanged(actor.HouseholdId, list, 3, true);
            Assert.Empty(revisions.DrainCommitted()); return 1;
        });
        var committed = revisions.DrainCommitted(); Assert.Equal(2, committed.Count);
        var metadata = Assert.Single(committed, x => x.ListId is not null);
        Assert.Equal(list, metadata.ListId); Assert.Equal(3, metadata.ListRevision); Assert.True(metadata.ListDeleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => boundary.ExecuteAsync<int>(actor, false, [], async ct =>
        {
            await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Shopping, false, ct);
            revisions.ShoppingListChanged(actor.HouseholdId, list, 4, false);
            throw new InvalidOperationException("Deliberate rollback");
        }));
        Assert.Empty(revisions.DrainCommitted());
        Assert.Equal(1, (await db.Households.AsNoTracking().SingleAsync()).ShoppingRevision);
    }
    private RecipeManagerDbContext Context() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
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
    public async Task Shared_counters_increment_once_per_transaction_and_only_publish_after_commit()
    {
        await using var db = Context();
        var actor = await Seed(db);
        var revisions = new RevisionCoordinator(db);
        await new TransactionBoundary(db, TimeProvider.System, revisions).ExecuteAsync(actor, false, [], async ct =>
        {
            await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Recipes, true, ct);
            await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Recipes | ChangedDomain.Planner, true, ct);
            Assert.Empty(revisions.DrainCommitted());
            return 1;
        });
        var household = await db.Households.AsNoTracking().SingleAsync();
        Assert.Equal(1, household.ContentRevision);
        Assert.Equal(1, household.RecipesRevision);
        Assert.Equal(1, household.PlannerRevision);
        Assert.Equal(1, household.ShoppingSourcesRevision);
        Assert.Single(revisions.DrainCommitted());
        Assert.Empty(revisions.DrainCommitted());
    }

    [Fact]
    public async Task Personal_and_shopping_changes_do_not_increment_source_revision()
    {
        await using var db = Context();
        var actor = await Seed(db);
        var revisions = new RevisionCoordinator(db);
        await new TransactionBoundary(db, TimeProvider.System, revisions).ExecuteAsync(actor, false, [], async ct =>
        {
            await revisions.PersonalAsync(actor.HouseholdId, actor.UserId, ct);
            await revisions.PersonalAsync(actor.HouseholdId, actor.UserId, ct);
            return 1;
        });
        Assert.Equal(0, (await db.Households.AsNoTracking().SingleAsync()).ContentRevision);
        Assert.Equal(1, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId)).PreferencesRevision);
        Assert.Equal(actor.UserId, Assert.Single(revisions.DrainCommitted()).UserId);
        await new TransactionBoundary(db, TimeProvider.System, revisions).ExecuteAsync(actor, false, [], async ct =>
        {
            await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Shopping, false, ct);
            return 1;
        });
        var household = await db.Households.AsNoTracking().SingleAsync();
        Assert.Equal(1, household.ContentRevision);
        Assert.Equal(0, household.ShoppingSourcesRevision);
    }

    [Fact]
    public async Task Rollback_drops_counters_and_notifications()
    {
        await using var db = Context();
        var actor = await Seed(db);
        var revisions = new RevisionCoordinator(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TransactionBoundary(db, TimeProvider.System, revisions)
            .ExecuteAsync<int>(actor, false, [], async ct =>
            {
                await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Planner, true, ct);
                throw new InvalidOperationException("fixture");
            }));
        Assert.Equal(0, (await db.Households.AsNoTracking().SingleAsync()).ContentRevision);
        Assert.Empty(revisions.DrainCommitted());
    }

    [Fact]
    public async Task Recording_without_transaction_is_rejected()
    {
        await using var db = Context();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RevisionCoordinator(db)
            .SharedAsync(Guid.NewGuid(), ChangedDomain.Recipes, true));
    }
}
