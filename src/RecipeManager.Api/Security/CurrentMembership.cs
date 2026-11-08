using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Security;

public sealed record MembershipAccess(Guid UserId, Guid HouseholdId, HouseholdRole Role, long MembershipRevision);

public sealed class CurrentMembership(RecipeManagerDbContext database, IOptions<IdentityOptions> identity, IHttpContextAccessor accessor)
{
    public Task<RequestActor?> ResolveActorAsync(CancellationToken cancellationToken = default)
        => ResolveActorAsync(accessor.HttpContext, cancellationToken);

    public async Task<RequestActor?> ResolveActorAsync(HttpContext? context, CancellationToken cancellationToken = default)
    {
        if (context is null) return null;
        var ticket = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!ticket.Succeeded || ticket.Properties?.ExpiresUtc is not { } expiry) return null;
        var member = await ResolveAsync(ticket.Principal, cancellationToken);
        if (member is null) return null;
        return new RequestActor(member.UserId, member.HouseholdId,
            ticket.Principal!.FindFirstValue(identity.Value.ClaimsIdentity.SecurityStampClaimType)!, expiry);
    }

    public async Task<MembershipAccess?> ResolveAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        database.ClearHouseholdScope();
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(identity.Value.ClaimsIdentity.UserIdClaimType), out var userId)
            || userId == Guid.Empty)
        {
            return null;
        }

        var stamp = principal.FindFirstValue(identity.Value.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrEmpty(stamp))
        {
            return null;
        }

        // This narrow pre-scope lookup uses only a signed identity, never a household/role hint.
        // AsNoTracking and a fresh projection prevent a tracked stale user/stamp from authorizing.
        var access = await database.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(member => member.UserId == userId && member.IsActive)
            .Join(database.Users.AsNoTracking().Where(user => user.IsActive && user.SecurityStamp == stamp),
                member => member.UserId, user => user.Id,
                (member, user) => new MembershipAccess(user.Id, member.HouseholdId, member.Role, member.Revision))
            .SingleOrDefaultAsync(cancellationToken);
        if (access is not null)
        {
            database.BindHouseholdScope(access.HouseholdId);
        }

        return access;
    }
}
