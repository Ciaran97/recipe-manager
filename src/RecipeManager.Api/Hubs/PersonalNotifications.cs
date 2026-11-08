using System.Globalization;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Api.Hubs;

public sealed record PersonalChanged(string OwnPreferencesRevision);
public sealed class PersonalNotifications(AuthorizedConnectionRegistry connections)
{
    public Task PublishAsync(Guid householdId, Guid userId, long revision, CancellationToken ct)
        => connections.DispatchPersonalAsync(householdId, userId, "PersonalChanged", new PersonalChanged(revision.ToString(CultureInfo.InvariantCulture)), ct);
}
