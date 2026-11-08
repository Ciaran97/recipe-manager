using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Households;

public sealed record HouseholdPage<T>(IReadOnlyList<T> Items, Guid? NextId);
public sealed record InvitationIssued(Guid Id, string Token, DateTimeOffset ExpiresAt, long Revision);
public sealed record InvitationSummary(Guid Id, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt, long Revision);
public sealed record MemberSummary(Guid Id, Guid UserId, string Email, string Name, HouseholdRole Role, long Revision);
public sealed record AcceptInvitationRequest(string Token, string Email, string Password, string Name);
public interface IMembershipStore
{
    Task<InvitationIssued> IssueAsync(RequestActor actor, Guid? retryKey, CancellationToken cancellationToken);
    Task<HouseholdPage<InvitationSummary>> InvitationsAsync(RequestActor actor, Guid? afterId, int limit, CancellationToken cancellationToken);
    Task RevokeInvitationAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken);
    Task<HouseholdSession> AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken);
    Task<HouseholdPage<MemberSummary>> MembersAsync(RequestActor actor, Guid? afterId, int limit, CancellationToken cancellationToken);
    Task RemoveAsync(RequestActor actor, Guid membershipId, long revision, CancellationToken cancellationToken);
}
public sealed class ResourceMissingException : Exception;
public sealed class RevisionConflictException : Exception;

public sealed class MembershipService(IMembershipStore store)
{
    public Task<InvitationIssued> IssueAsync(RequestActor actor, CancellationToken cancellationToken = default, Guid? retryKey = null) => store.IssueAsync(actor, retryKey, cancellationToken);
    public Task<HouseholdPage<InvitationSummary>> InvitationsAsync(RequestActor actor, CancellationToken cancellationToken = default, Guid? afterId = null, int limit = 25)
        => store.InvitationsAsync(actor, afterId, Limit(limit), cancellationToken);
    public Task RevokeInvitationAsync(RequestActor actor, Guid id, long revision, CancellationToken cancellationToken = default)
        => store.RevokeInvitationAsync(actor, DomainRules.NonEmptyId(id, "invitationId"), DomainRules.Revision(revision), cancellationToken);
    public Task<HouseholdSession> AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 1024)
            throw new DomainValidationException("token", "invalid", "A valid invitation is required.");
        AccountRules.Password(request.Password);
        return store.AcceptAsync(request with { Email = AccountRules.Email(request.Email), Name = string.IsNullOrWhiteSpace(request.Name) ? "" : DomainRules.Name(request.Name) }, cancellationToken);
    }
    public Task<HouseholdPage<MemberSummary>> MembersAsync(RequestActor actor, CancellationToken cancellationToken = default, Guid? afterId = null, int limit = 25)
        => store.MembersAsync(actor, afterId, Limit(limit), cancellationToken);
    public Task RemoveAsync(RequestActor actor, Guid membershipId, long revision, CancellationToken cancellationToken = default)
        => store.RemoveAsync(actor, DomainRules.NonEmptyId(membershipId, "membershipId"), DomainRules.Revision(revision), cancellationToken);

    private static int Limit(int limit) => limit is >= 1 and <= 100 ? limit
        : throw new DomainValidationException("limit", "range", "Limit must be between 1 and 100.");

    public static void PreserveAdministrator(HouseholdRole targetRole, int activeAdministratorCount)
    {
        if (targetRole == HouseholdRole.Administrator && activeAdministratorCount <= 1)
            throw new HouseholdConflictException("last_administrator");
    }
}
