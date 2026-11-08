namespace RecipeManager.Core.Modules.Households.Contracts;

public sealed record HouseholdSyncRead(long ContentRevision, long RecipesRevision, long PlannerRevision,
    long ShoppingRevision, long SettingsRevision, long MembershipRevision, long OwnPreferencesRevision);
public interface IHouseholdSyncReader
{
    Task<HouseholdSyncRead> ReadAsync(RequestActor actor, CancellationToken cancellationToken);
}
