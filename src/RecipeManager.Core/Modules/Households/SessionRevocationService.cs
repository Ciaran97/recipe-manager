using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Households;

public enum RevocationCause { Logout, PasswordChange, PasswordReset, MembershipRemoval, AccountDeactivation, SecurityStampRotation }
public sealed record RevocationRequest(Guid UserId, RevocationCause Cause, RequestActor? Requester, string? CurrentPassword,
    string? NewPassword, long? ExpectedMembershipRevision, long? ExpectedPreferencesRevision);
public interface ISessionRevocationStore
{
    Task RevokeAsync(RevocationRequest request, CancellationToken cancellationToken);
}
public sealed class HouseholdAccessDeniedException : Exception;

public sealed class SessionRevocationService(ISessionRevocationStore store)
{
    // Null requester is reserved for the authenticated host-local maintenance adapter, never a DTO.
    public Task RevokeAsync(Guid userId, RevocationCause cause, RequestActor? requester,
        string? currentPassword = null, string? newPassword = null, long? expectedMembershipRevision = null,
        long? expectedPreferencesRevision = null, CancellationToken cancellationToken = default)
    {
        DomainRules.NonEmptyId(userId, "userId");
        if (!Enum.IsDefined(cause)) throw new ArgumentOutOfRangeException(nameof(cause));
        if (cause is RevocationCause.Logout or RevocationCause.PasswordChange)
        {
            if (requester?.UserId != userId) throw new HouseholdAccessDeniedException();
        }
        else if (cause is RevocationCause.PasswordReset or RevocationCause.AccountDeactivation or RevocationCause.SecurityStampRotation)
        {
            if (requester is not null) throw new HouseholdAccessDeniedException();
        }
        if (cause is RevocationCause.PasswordChange or RevocationCause.PasswordReset) AccountRules.Password(newPassword);
        if (cause == RevocationCause.PasswordChange && (string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 256))
            throw new DomainValidationException("currentPassword", "required", "Current password is required.");
        return store.RevokeAsync(new(userId, cause, requester, currentPassword, newPassword, expectedMembershipRevision, expectedPreferencesRevision), cancellationToken);
    }
}
