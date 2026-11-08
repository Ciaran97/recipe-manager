using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Imports;

public enum ImportHandoffState { Open, Committed, Expired }
public sealed record ImportCommitResult(Guid RecipeId, long CommittedRevision, DateTimeOffset CommittedAt);
public sealed class ImportStateException(string code, ImportCommitResult? result = null) : InvalidOperationException(code)
{
    public string Code { get; } = code;
    public ImportCommitResult? Result { get; } = result;
}

/// <summary>Durable owner-bound receipt. Contains no source text or credentials.
/// Callers must serialize transitions under the household/receipt database transaction.</summary>
public sealed class ImportHandoff
{
    private ImportHandoff() { }
    public ImportHandoff(Guid handoffId, Guid householdId, Guid userId, DateTimeOffset capturedAt,
        DateTimeOffset registeredAt, string inputHash)
    {
        HandoffId = DomainRules.NonEmptyId(handoffId, "handoffId");
        HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        UserId = DomainRules.NonEmptyId(userId, "userId");
        CapturedAt = capturedAt.ToUniversalTime();
        RegisteredAt = registeredAt.ToUniversalTime();
        if (CapturedAt <= RegisteredAt.AddDays(-7) || CapturedAt > RegisteredAt.AddMinutes(5))
            throw new ImportStateException("handoff_expired");
        ExpiresAt = CapturedAt.AddDays(7) < RegisteredAt.AddDays(7) ? CapturedAt.AddDays(7) : RegisteredAt.AddDays(7);
        InputHash = ImportInput.Digest(inputHash, "inputHash");
    }
    public Guid HandoffId { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string InputHash { get; private set; } = "";
    public ImportHandoffState State { get; private set; }
    public string? CommitHash { get; private set; }
    // Historical identity, not a live recipe FK: deleting a recipe must retain its receipt.
    public Guid? OriginalRecipeId { get; private set; }
    public long? CommittedRevision { get; private set; }
    public DateTimeOffset? CommittedAt { get; private set; }
    public ImportCommitResult? Result => State == ImportHandoffState.Committed
        ? new(OriginalRecipeId!.Value, CommittedRevision!.Value, CommittedAt!.Value) : null;

    public void RequireOpen(DateTimeOffset now)
    {
        if (State == ImportHandoffState.Committed) throw new ImportStateException("handoff_already_committed");
        if (now >= ExpiresAt || State == ImportHandoffState.Expired)
        {
            State = ImportHandoffState.Expired;
            throw new ImportStateException("handoff_expired");
        }
    }
    public ImportCommitResult? Replay(string commitHash)
    {
        var digest = ImportInput.Digest(commitHash, "commitHash");
        if (State != ImportHandoffState.Committed) return null;
        if (!string.Equals(digest, CommitHash, StringComparison.Ordinal)) throw new ImportStateException("handoff_already_committed");
        return Result;
    }
    public void Commit(string commitHash, ImportCommitResult result, DateTimeOffset now)
    {
        RequireOpen(now);
        var digest = ImportInput.Digest(commitHash, "commitHash");
        _ = DomainRules.NonEmptyId(result.RecipeId, "recipeId");
        if (result.CommittedRevision < 1 || result.CommittedAt != now)
            throw new DomainValidationException("commitResult", "invalid", "The result must identify the recipe revision committed now.");
        CommitHash = digest;
        OriginalRecipeId = result.RecipeId;
        CommittedRevision = result.CommittedRevision;
        CommittedAt = result.CommittedAt.ToUniversalTime();
        State = ImportHandoffState.Committed;
    }
}
