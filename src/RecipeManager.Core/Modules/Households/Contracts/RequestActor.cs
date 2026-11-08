namespace RecipeManager.Core.Modules.Households.Contracts;

// Created only from authenticated server state; never bind this type from request JSON.
public sealed class RequestActor(Guid userId, Guid householdId, string securityStamp, DateTimeOffset expiresAt)
{
    public Guid UserId { get; } = Shared.DomainRules.NonEmptyId(userId, "userId");
    public Guid HouseholdId { get; } = Shared.DomainRules.NonEmptyId(householdId, "householdId");
    public string SecurityStamp { get; } = securityStamp;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
}
