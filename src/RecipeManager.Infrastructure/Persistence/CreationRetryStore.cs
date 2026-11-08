using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Shared;

namespace RecipeManager.Infrastructure.Persistence;

public sealed class CreationRetryRecord
{
    public Guid HouseholdId { get; init; }
    public Guid UserId { get; init; }
    public string Route { get; init; } = "";
    public Guid Key { get; init; }
    public string PayloadHash { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public int Status { get; init; }
    public string Body { get; init; } = "";
    public string? Location { get; init; }
    public string? ETag { get; init; }
}

public sealed record CreationOutcome(int Status, string Body, string? Location, string? ETag);
public sealed class RetryPayloadConflictException : Exception;

// Called only inside TransactionBoundary after household lock/access validation. No import handoffs.
public sealed class CreationRetryStore(RecipeManagerDbContext database, TimeProvider clock, IDataProtectionProvider protection)
{
    public async Task<CreationOutcome> ExecuteAsync(Guid householdId, Guid userId, string route, Guid key,
        ReadOnlyMemory<byte> canonicalPayload, Func<CancellationToken, Task<CreationOutcome>> create,
        CancellationToken cancellationToken = default)
    {
        DomainRules.NonEmptyId(key, "Idempotency-Key");
        if (database.Database.CurrentTransaction is null || database.CurrentHouseholdId != householdId)
            throw new InvalidOperationException("Creation retries require a household transaction.");
        if (route.Length is < 1 or > 200 || !route.StartsWith("/api/v1/", StringComparison.Ordinal))
            throw new ArgumentException("A stable API route is required.", nameof(route));
        if (!await database.Memberships.AnyAsync(x => x.UserId == userId && x.IsActive, cancellationToken))
            throw new TransactionAccessException();
        var hash = Convert.ToHexString(SHA256.HashData(canonicalPayload.Span));
        var prior = await database.CreationRetries.SingleOrDefaultAsync(x => x.UserId == userId && x.Route == route && x.Key == key, cancellationToken);
        var now = clock.GetUtcNow();
        if (prior is not null && prior.ExpiresAt > now)
        {
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(prior.PayloadHash), Convert.FromHexString(hash)))
                throw new RetryPayloadConflictException();
            return new(prior.Status, Protector(householdId, userId, route).Unprotect(prior.Body), prior.Location, prior.ETag);
        }
        if (prior is not null)
        {
            database.Remove(prior);
            await database.SaveChangesAsync(cancellationToken);
        }
        var result = await create(cancellationToken);
        if (result.Status is < 200 or >= 300) throw new InvalidOperationException("Only successful creation outcomes may be cached.");
        database.CreationRetries.Add(new CreationRetryRecord
        {
            HouseholdId = householdId,
            UserId = userId,
            Route = route,
            Key = key,
            PayloadHash = hash,
            ExpiresAt = now.AddHours(24),
            Status = result.Status,
            Body = Protector(householdId, userId, route).Protect(result.Body),
            Location = result.Location,
            ETag = result.ETag
        });
        return result;
    }
    public async Task<CreationOutcome?> TryReadAsync(Guid householdId, Guid userId, string route, Guid key,
        ReadOnlyMemory<byte> canonicalPayload, CancellationToken ct)
    {
        DomainRules.NonEmptyId(key, "Idempotency-Key");
        if (database.Database.CurrentTransaction is null || database.CurrentHouseholdId != householdId)
            throw new InvalidOperationException("Creation retries require a household transaction.");
        if (!await database.Memberships.AnyAsync(x => x.UserId == userId && x.IsActive, ct)) throw new TransactionAccessException();
        var prior = await database.CreationRetries.SingleOrDefaultAsync(x => x.UserId == userId && x.Route == route && x.Key == key, ct);
        if (prior is null || prior.ExpiresAt <= clock.GetUtcNow()) return null;
        var hash = Convert.ToHexString(SHA256.HashData(canonicalPayload.Span));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(prior.PayloadHash), Convert.FromHexString(hash)))
            throw new RetryPayloadConflictException();
        return new(prior.Status, Protector(householdId, userId, route).Unprotect(prior.Body), prior.Location, prior.ETag);
    }
    private IDataProtector Protector(Guid householdId, Guid userId, string route)
        => protection.CreateProtector("RecipeManager.CreationRetry.v1", householdId.ToString("N"), userId.ToString("N"), route);

}
