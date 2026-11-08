using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Infrastructure.Households;

public sealed class MembershipStore(RecipeManagerDbContext database, TransactionBoundary transactions, RevisionCoordinator revisions,
    UserManager<ApplicationUser> users, TimeProvider clock, SessionRevocationService revocation, CreationRetryStore retryStore) : IMembershipStore
{
    public Task<InvitationIssued> IssueAsync(RequestActor actor, Guid? retryKey, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, true, [], async ct =>
        {
            async Task<InvitationIssued> Create(CancellationToken cancellation)
            {
                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var invitation = new Invitation(Guid.NewGuid(), actor.HouseholdId, actor.UserId, Hash(token), clock.GetUtcNow().AddHours(48));
                database.Invitations.Add(invitation);
                await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Membership, false, cancellation);
                return new InvitationIssued(invitation.Id, token, invitation.ExpiresAt, invitation.Revision);
            }
            if (retryKey is null) return await Create(ct);
            var result = await retryStore.ExecuteAsync(actor.HouseholdId, actor.UserId, "/api/v1/household/invitations", retryKey.Value,
                Encoding.UTF8.GetBytes("{}"), async cancellation => new CreationOutcome(201,
                    JsonSerializer.Serialize(await Create(cancellation)), null, null), ct);
            return JsonSerializer.Deserialize<InvitationIssued>(result.Body) ?? throw new InvalidOperationException("Retry result unavailable.");
        }, cancellationToken);

    public Task<HouseholdPage<InvitationSummary>> InvitationsAsync(RequestActor actor, Guid? afterId, int limit, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, true, [], async ct =>
        {
            var rows = await database.Invitations.AsNoTracking().Where(x => afterId == null || x.Id.CompareTo(afterId.Value) > 0)
                .OrderBy(x => x.Id).Take(limit + 1)
                .Select(x => new InvitationSummary(x.Id, x.ExpiresAt, x.AcceptedAt, x.RevokedAt, x.Revision)).ToListAsync(ct);
            var items = rows.Take(limit).ToArray();
            return new HouseholdPage<InvitationSummary>(items, rows.Count > limit ? items[^1].Id : null);
        }, cancellationToken);

    public Task RevokeInvitationAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, true, [], async ct =>
        {
            var invitation = await database.Invitations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissingException();
            if (invitation.Revision != revision) throw new RevisionConflictException();
            if (invitation.AcceptedAt is not null) throw new HouseholdConflictException("invitation_already_used");
            database.Entry(invitation).Property(x => x.RevokedAt).CurrentValue = clock.GetUtcNow();
            database.Entry(invitation).Property(x => x.Revision).CurrentValue = checked(invitation.Revision + 1);
            await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Membership, false, ct);
            return true;
        }, cancellationToken);

    public async Task<HouseholdSession> AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        var hash = Hash(request.Token);
        var householdId = await database.Invitations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TokenHash == hash)
            .Select(x => (Guid?)x.HouseholdId).SingleOrDefaultAsync(cancellationToken);
        if (householdId is null) throw InvalidInvitation();
        database.BindHouseholdScope(householdId.Value);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        _ = await database.Households.FromSqlInterpolated($"SELECT * FROM households.\"Households\" WHERE \"Id\" = {householdId.Value} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        var invitation = (await database.Invitations.FromSqlInterpolated($"SELECT * FROM households.\"Invitations\" WHERE \"HouseholdId\" = {householdId.Value} AND \"TokenHash\" = {hash} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw InvalidInvitation();
        if (invitation.AcceptedAt is not null) throw new HouseholdConflictException("invitation_already_used");
        if (invitation.RevokedAt is not null || invitation.ExpiresAt <= clock.GetUtcNow()) throw InvalidInvitation();
        var user = new ApplicationUser(Guid.NewGuid(), request.Email);
        database.Entry(user).Property(x => x.DisplayName).CurrentValue = request.Name;
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) throw new DomainValidationException("email", "account_invalid", "Account details could not be accepted.");
        database.Memberships.Add(new Membership(Guid.NewGuid(), householdId.Value, user.Id, HouseholdRole.Member));
        database.Entry(invitation).Property(x => x.AcceptedAt).CurrentValue = clock.GetUtcNow();
        database.Entry(invitation).Property(x => x.Revision).CurrentValue = checked(invitation.Revision + 1);
        // This is a just-created identity, inside the same locked acceptance transaction.
        revisions.Begin(new RequestActor(user.Id, householdId.Value, user.SecurityStamp!, clock.GetUtcNow().AddDays(14)));
        try
        {
            await revisions.SharedAsync(householdId.Value, ChangedDomain.Membership, false, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { revisions.Discard(); throw; }
        revisions.Committed();
        return new(user.Id, householdId.Value, HouseholdRole.Member);
    }

    public Task<HouseholdPage<MemberSummary>> MembersAsync(RequestActor actor, Guid? afterId, int limit, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var rows = await database.Memberships.AsNoTracking().Where(x => x.IsActive && (afterId == null || x.Id.CompareTo(afterId.Value) > 0))
                .Join(database.Users.Where(x => x.IsActive), member => member.UserId, user => user.Id,
                    (member, user) => new { Member = member, User = user })
                .OrderBy(x => x.Member.Id).Take(limit + 1)
                .Select(x => new MemberSummary(x.Member.Id, x.User.Id, x.User.Email!, x.User.DisplayName, x.Member.Role, x.Member.Revision)).ToListAsync(ct);
            var items = rows.Take(limit).ToArray();
            return new HouseholdPage<MemberSummary>(items, rows.Count > limit ? items[^1].Id : null);
        }, cancellationToken);

    public async Task RemoveAsync(RequestActor actor, Guid membershipId, long revision, CancellationToken cancellationToken)
    {
        database.BindHouseholdScope(actor.HouseholdId);
        var member = await database.Memberships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == membershipId && x.IsActive, cancellationToken)
            ?? throw new ResourceMissingException();
        // The common service obtains the user gate BEFORE the household lock and checks revision there.
        await revocation.RevokeAsync(member.UserId, RevocationCause.MembershipRemoval, actor,
            expectedMembershipRevision: revision, cancellationToken: cancellationToken);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DomainValidationException InvalidInvitation() => new("token", "invitation_invalid", "The invitation is invalid, revoked or expired.");
}
