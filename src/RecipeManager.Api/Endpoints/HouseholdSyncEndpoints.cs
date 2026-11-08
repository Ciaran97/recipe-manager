using System.Globalization;
using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Endpoints;

public static class HouseholdSyncEndpoints
{
    public static void MapHouseholdSyncEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/household/sync", async (HttpContext context, CurrentMembership membership, IHouseholdSyncReader reader, CancellationToken ct) =>
        {
            var actor = await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
            // Both actor validation and the household transaction precede conditional-cache handling.
            var state = await reader.ReadAsync(actor, ct);
            var tag = $"\"sync:{actor.HouseholdId:N}:{actor.UserId:N}:{state.ContentRevision.ToString(CultureInfo.InvariantCulture)}:{state.OwnPreferencesRevision.ToString(CultureInfo.InvariantCulture)}\"";
            context.Response.Headers.ETag = tag;
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers.Vary = "Cookie";
            return context.Request.Headers.IfNoneMatch == tag ? Results.StatusCode(304) : Results.Ok(state);
        }).WithName("GetHouseholdSync").WithTags("Synchronization").RequireAuthorization(IdentityConfiguration.MemberPolicy)
            .WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<HouseholdSyncRead>().Produces(304);
    }
}
