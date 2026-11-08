using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Households;

public sealed class Household
{
    private Household() { }

    public Household(Guid id, string name, string timeZone)
    {
        Id = DomainRules.NonEmptyId(id, "householdId");
        Name = DomainRules.Name(name);
        TimeZone = HouseholdDates.TimeZone(timeZone).Id;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string TimeZone { get; private set; } = "UTC";
    public long Revision { get; private set; } = 1;
    public long ContentRevision { get; private set; }
    public long RecipesRevision { get; private set; }
    public long PlannerRevision { get; private set; }
    public long ShoppingRevision { get; private set; }
    public long SettingsRevision { get; private set; }
    public long MembershipRevision { get; private set; }
    public long ShoppingSourcesRevision { get; private set; }
    public string? ProtectedAiSettings { get; private set; }
}

public sealed class Installation
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid Id { get; private set; } = SingletonId;
    public DateTimeOffset? SetupCompletedAt { get; private set; }
    public long Revision { get; private set; } = 1;
}
