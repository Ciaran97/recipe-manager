using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Imports;

public sealed record ImportCleanupBatch(Guid? NextHousehold, int Households, int Drafts, int RetryRecords, int ExpiredHandoffs);

// Operator maintenance, never an end-user API. Each bounded batch has explicit tenant predicates
// and takes the same household lock as commands. Durable receipt rows are never removed.
public sealed class ExpiredDraftCleanup(RecipeManagerDbContext database, TimeProvider clock)
{
    public async Task<ImportCleanupBatch> SweepAsync(Guid? after, CancellationToken cancellationToken)
    {
        const int householdLimit = 100;
        const int rowLimit = 100;
        var households = await database.Households.IgnoreQueryFilters().AsNoTracking()
            .Where(x => after == null || x.Id.CompareTo(after.Value) > 0)
            .OrderBy(x => x.Id).Select(x => x.Id).Take(householdLimit).ToArrayAsync(cancellationToken);
        var drafts = 0; var retries = 0; var expired = 0;
        foreach (var household in households)
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var acquired = await database.Households.FromSqlInterpolated(
                $"SELECT * FROM households.\"Households\" WHERE \"Id\" = {household} FOR UPDATE SKIP LOCKED")
                .IgnoreQueryFilters().AsNoTracking().ToArrayAsync(cancellationToken);
            if (acquired.Length == 0) continue; // Try busy households again on the next pass.
            var now = clock.GetUtcNow();
            var oldDrafts = await database.ImportDrafts.IgnoreQueryFilters()
                .Where(x => x.HouseholdId == household && x.ExpiresAt <= now)
                .OrderBy(x => x.Id).Take(rowLimit).ToArrayAsync(cancellationToken);
            var oldRetries = await database.CreationRetries.IgnoreQueryFilters()
                .Where(x => x.HouseholdId == household && x.ExpiresAt <= now)
                .OrderBy(x => x.UserId).ThenBy(x => x.Route).ThenBy(x => x.Key).Take(rowLimit).ToArrayAsync(cancellationToken);
            var expiredIds = await database.ImportHandoffs.IgnoreQueryFilters()
                .Where(x => x.HouseholdId == household && x.State == ImportHandoffState.Open && x.ExpiresAt <= now)
                .OrderBy(x => x.HandoffId).Select(x => new { x.UserId, x.HandoffId }).Take(rowLimit).ToArrayAsync(cancellationToken);
            // Both components of the owner key are retained, including if different users choose
            // the same handoff UUID. Bounded individual updates avoid a cross-product IN filter.
            foreach (var key in expiredIds)
                expired += await database.ImportHandoffs.IgnoreQueryFilters()
                    .Where(x => x.HouseholdId == household && x.UserId == key.UserId && x.HandoffId == key.HandoffId
                        && x.State == ImportHandoffState.Open && x.ExpiresAt <= now)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.State, ImportHandoffState.Expired), cancellationToken);
            database.ImportDrafts.RemoveRange(oldDrafts);
            database.CreationRetries.RemoveRange(oldRetries);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            drafts += oldDrafts.Length; retries += oldRetries.Length;
        }
        database.ChangeTracker.Clear();
        return new(households.Length == householdLimit ? households[^1] : null, households.Length, drafts, retries, expired);
    }
}
