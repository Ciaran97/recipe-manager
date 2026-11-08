using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Api.Hubs;

public sealed record DomainRevisions(string RecipesRevision, string PlannerRevision, string ShoppingRevision, string SettingsRevision, string MembershipRevision);
public sealed record HouseholdChanged(string ContentRevision, DomainRevisions DomainRevisions);

public sealed class HouseholdBroadcaster(IServiceScopeFactory scopes, AuthorizedConnectionRegistry connections, PersonalNotifications personal)
{
    public async Task PublishAsync(IReadOnlyList<RevisionInvalidation> changes, CancellationToken ct)
    {
        foreach (var change in changes)
        {
            if (change.UserId is { } user) { await personal.PublishAsync(change.HouseholdId, user, change.Revision, ct); continue; }
            if (change.ListId is { } list)
            {
                if (change.ListDeleted) await connections.DispatchHouseholdAsync(change.HouseholdId, "ShoppingListDeleted", new ShoppingListDeleted(list, Text(change.ListRevision!.Value)), ct);
                else await connections.DispatchHouseholdAsync(change.HouseholdId, "ShoppingListChanged", new ShoppingListChanged(list, Text(change.ListRevision!.Value), Guid.NewGuid()), ct);
                continue;
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
            db.BindHouseholdScope(change.HouseholdId);
            var household = await db.Households.AsNoTracking().SingleOrDefaultAsync(x => x.Id == change.HouseholdId, ct);
            if (household is null) continue;
            var payload = new HouseholdChanged(Text(household.ContentRevision), new(Text(household.RecipesRevision), Text(household.PlannerRevision),
                Text(household.ShoppingRevision), Text(household.SettingsRevision), Text(household.MembershipRevision)));
            await connections.DispatchHouseholdAsync(change.HouseholdId, "HouseholdChanged", payload, ct);
        }
    }
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}

public sealed class PostCommitNotificationsMiddleware(RequestDelegate next, ILogger<PostCommitNotificationsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, RevisionCoordinator revisions, HouseholdBroadcaster broadcaster, IHostApplicationLifetime lifetime)
    {
        try { await next(context); }
        finally
        {
            var changes = revisions.DrainCommitted();
            if (changes.Count > 0)
            {
                try { await broadcaster.PublishAsync(changes, lifetime.ApplicationStopping); }
                catch { logger.LogWarning("Post-commit invalidation delivery failed {TraceId}", context.TraceIdentifier); }
            }
        }
    }
}
