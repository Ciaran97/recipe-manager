using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;

namespace RecipeManager.Infrastructure.Persistence;

public sealed class TransactionBoundary(RecipeManagerDbContext database, TimeProvider clock, RevisionCoordinator? revisions = null)
{
    // The callback reloads state and validates client revisions on EVERY attempt. It must not
    // perform external I/O or publish messages. Cross-module work uses this same scoped context.
    public async Task<T> ExecuteAsync<T>(RequestActor actor, bool administratorOnly,
        IReadOnlyCollection<AggregateLock> locks, Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Start a transaction before loading or modifying command state.");
        }

        var ordered = locks.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
        if (ordered.Select(x => (x.Kind, x.Id)).Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("Duplicate aggregate lock.", nameof(locks));
        }

        database.BindHouseholdScope(actor.HouseholdId);
        for (var attempt = 1; ; attempt++)
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            revisions?.Begin(actor);
            T result;
            try
            {
                var household = await database.Households.FromSqlInterpolated(
                    $"SELECT * FROM households.\"Households\" WHERE \"Id\" = {actor.HouseholdId} FOR UPDATE")
                    .AsNoTracking().ToListAsync(cancellationToken);
                var role = await database.Memberships.AsNoTracking()
                    .Where(x => x.UserId == actor.UserId && x.IsActive)
                    .Join(database.Users.AsNoTracking().Where(x => x.IsActive && x.SecurityStamp == actor.SecurityStamp),
                        member => member.UserId, user => user.Id, (member, user) => (HouseholdRole?)member.Role)
                    .SingleOrDefaultAsync(cancellationToken);
                if (household.Count != 1 || role is null || actor.ExpiresAt <= clock.GetUtcNow()
                    || (administratorOnly && role != HouseholdRole.Administrator))
                {
                    throw new TransactionAccessException();
                }

                foreach (var aggregate in ordered) await aggregate.Acquire(cancellationToken);
                result = await operation(cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception error)
            {
                revisions?.Discard();
                await transaction.RollbackAsync(CancellationToken.None);
                database.ChangeTracker.Clear();
                if (attempt >= 3 || !Transient(error)) throw;
                // Deterministic bounded retry; no retry of revision conflicts or client intent.
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
                continue;
            }
            // Never roll back or retry a committed mutation because notification preparation fails.
            revisions?.Committed();
            return result;
        }
    }

    private static bool Transient(Exception error)
        => (error is PostgresException postgres && postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            || (error is DbUpdateException { InnerException: PostgresException inner }
                && inner.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected);
}

// A lock callback is repository-owned, not client-supplied SQL. Keys establish a total order.
public sealed record AggregateLock(string Kind, Guid Id, Func<CancellationToken, Task> Acquire);
public sealed class TransactionAccessException : Exception;
