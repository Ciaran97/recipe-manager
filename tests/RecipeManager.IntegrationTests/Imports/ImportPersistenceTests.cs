using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Imports;

public sealed class ImportPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Receipts_survive_draft_cleanup_and_enforce_owner_binding_and_fail_closed_queries()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var home = new Household(Guid.NewGuid(), "Import home", "UTC");
        var user = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid() + "@example.test");
        var other = new ApplicationUser(Guid.NewGuid(), Guid.NewGuid() + "@example.test");
        var now = DateTimeOffset.UtcNow;
        var receipt = new ImportHandoff(Guid.NewGuid(), home.Id, user.Id, now, now, ImportInput.Hash(ImportSourceKind.Text, "salt to taste"));
        var draft = new ImportDraft(Guid.NewGuid(), home.Id, user.Id, receipt.HandoffId, ImportSourceKind.Text,
            "salt to taste", "{}", "[]", null, now);
        db.AddRange(home, user, other, receipt, draft);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Empty(await db.ImportHandoffs.ToArrayAsync());
        Assert.Empty(await db.ImportDrafts.ToArrayAsync());
        db.BindHouseholdScope(home.Id);
        var loaded = await db.ImportHandoffs.SingleAsync();
        var recipeId = Guid.NewGuid();
        loaded.Commit(new string('A', 64), new(recipeId, 1, now), now);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.ImportDrafts.ExecuteDeleteAsync();
        loaded = await db.ImportHandoffs.SingleAsync();
        Assert.Equal(recipeId, loaded.Replay(new string('A', 64))!.RecipeId);
        Assert.Empty(await db.ImportDrafts.ToArrayAsync());
        // Historical recipe identity has no live FK: a deleted recipe must not erase its receipt.
        var wrongOwner = new ImportDraft(Guid.NewGuid(), home.Id, other.Id, receipt.HandoffId, ImportSourceKind.Text, "salt", "{}", "[]", null, now);
        db.ImportDrafts.Add(wrongOwner);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.BindHouseholdScope(Guid.NewGuid());
        Assert.Empty(await db.ImportHandoffs.ToArrayAsync());
    }
}
