using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Households;

public sealed class HouseholdSyncReader(RecipeManagerDbContext database, TransactionBoundary transactions) : IHouseholdSyncReader
{
    public Task<HouseholdSyncRead> ReadAsync(RequestActor actor, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var household = await database.Households.AsNoTracking().SingleAsync(x => x.Id == actor.HouseholdId, ct);
            var personal = await database.Users.AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => x.PreferencesRevision).SingleAsync(ct);
            return new HouseholdSyncRead(household.ContentRevision, household.RecipesRevision, household.PlannerRevision,
                household.ShoppingRevision, household.SettingsRevision, household.MembershipRevision, personal);
        }, cancellationToken);
}
