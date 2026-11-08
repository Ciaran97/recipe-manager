using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Households;

public sealed class Invitation
{
    private Invitation() { }

    public Invitation(Guid id, Guid householdId, Guid createdBy, string tokenHash, DateTimeOffset expiresAt)
    {
        Id = DomainRules.NonEmptyId(id, "invitationId");
        HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        CreatedBy = DomainRules.NonEmptyId(createdBy, "createdBy");
        if (tokenHash.Length != 64 || !tokenHash.All(Uri.IsHexDigit))
        {
            throw new DomainValidationException("tokenHash", "invalid_hash", "A SHA-256 digest is required.");
        }

        TokenHash = tokenHash.ToUpperInvariant();
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public long Revision { get; private set; } = 1;
}
