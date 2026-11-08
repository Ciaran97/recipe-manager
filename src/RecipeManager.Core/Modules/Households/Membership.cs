using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Households;

public enum HouseholdRole
{
    Administrator,
    Member
}

public sealed class Membership
{
    private Membership() { }

    public Membership(Guid id, Guid householdId, Guid userId, HouseholdRole role)
    {
        Id = DomainRules.NonEmptyId(id, "membershipId");
        HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        UserId = DomainRules.NonEmptyId(userId, "userId");
        if (!Enum.IsDefined(role))
        {
            throw new DomainValidationException("role", "invalid_role", "Choose Administrator or Member.");
        }

        Role = role;
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid UserId { get; private set; }
    public HouseholdRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Revision { get; private set; } = 1;
}
