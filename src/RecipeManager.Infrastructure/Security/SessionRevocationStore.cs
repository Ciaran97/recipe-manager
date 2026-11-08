using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Security;

public sealed class SessionRevocationStore(RecipeManagerDbContext database, UserManager<ApplicationUser> users,
    AuthorizedConnectionRegistry registry, RevisionCoordinator revisions, TimeProvider clock) : ISessionRevocationStore
{
    public Task RevokeAsync(RevocationRequest request, CancellationToken cancellationToken)
        => registry.RevokeAsync(request.UserId, async (markCommitAttempted, ct) =>
        {
            if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Revocation must acquire its user gate before database locks.");
            database.ChangeTracker.Clear();
            var target = await database.Memberships.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == request.UserId && x.IsActive, ct) ?? throw new ResourceMissingException();
            if (request.Requester is { } actor && actor.HouseholdId != target.HouseholdId) throw new ResourceMissingException();
            database.BindHouseholdScope(target.HouseholdId);
            await using var transaction = await database.Database.BeginTransactionAsync(ct);
            _ = await database.Households.FromSqlInterpolated($"SELECT * FROM households.\"Households\" WHERE \"Id\" = {target.HouseholdId} FOR UPDATE")
                .AsNoTracking().ToListAsync(ct);
            if (request.Requester is { } requester)
            {
                var role = await database.Memberships.AsNoTracking().Where(x => x.UserId == requester.UserId && x.IsActive)
                    .Join(database.Users.AsNoTracking().Where(x => x.IsActive && x.SecurityStamp == requester.SecurityStamp),
                        member => member.UserId, user => user.Id, (member, user) => (HouseholdRole?)member.Role).SingleOrDefaultAsync(ct);
                if (role is null || requester.ExpiresAt <= clock.GetUtcNow()) throw new TransactionAccessException();
                if (request.Cause == RevocationCause.MembershipRemoval && role != HouseholdRole.Administrator)
                    throw new HouseholdAccessDeniedException();
            }
            var member = await database.Memberships.SingleOrDefaultAsync(x => x.UserId == request.UserId && x.IsActive, ct)
                ?? throw new ResourceMissingException();
            var user = await database.Users.SingleOrDefaultAsync(x => x.Id == request.UserId && x.IsActive, ct)
                ?? throw new ResourceMissingException();
            if (request.ExpectedPreferencesRevision is { } profileRevision && user.PreferencesRevision != profileRevision) throw new RevisionConflictException();
            if (request.ExpectedMembershipRevision is { } expected && member.Revision != expected) throw new RevisionConflictException();
            if (request.Cause is RevocationCause.MembershipRemoval or RevocationCause.AccountDeactivation)
            {
                var administratorCount = await database.Memberships.Where(x => x.IsActive && x.Role == HouseholdRole.Administrator)
                    .Join(database.Users.Where(x => x.IsActive), membership => membership.UserId, account => account.Id, (membership, account) => account.Id).CountAsync(ct);
                MembershipService.PreserveAdministrator(member.Role, administratorCount);
            }
            if (request.Cause == RevocationCause.PasswordChange)
            {
                var changed = await users.ChangePasswordAsync(user, request.CurrentPassword!, request.NewPassword!);
                if (!changed.Succeeded) throw new DomainValidationException("currentPassword", "invalid_credentials", "Password could not be changed. Check the current password.");
            }
            else if (request.Cause == RevocationCause.PasswordReset)
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                if (!(await users.ResetPasswordAsync(user, token, request.NewPassword!)).Succeeded)
                    throw new DomainValidationException("newPassword", "invalid", "Password could not be reset.");
            }
            // Every cause uses the same final stamp mutation, inside this gate and transaction.
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) throw new RevisionConflictException();
            if (request.Cause == RevocationCause.MembershipRemoval)
            {
                database.Entry(member).Property(x => x.IsActive).CurrentValue = false;
                database.Entry(member).Property(x => x.Revision).CurrentValue = checked(member.Revision + 1);
            }
            if (request.Cause == RevocationCause.AccountDeactivation) database.Entry(user).Property(x => x.IsActive).CurrentValue = false;
            if (request.Cause is RevocationCause.MembershipRemoval or RevocationCause.AccountDeactivation)
            {
                revisions.Begin(new RequestActor(user.Id, target.HouseholdId, user.SecurityStamp!, clock.GetUtcNow().AddDays(14)));
                await revisions.SharedAsync(target.HouseholdId, ChangedDomain.Membership, false, ct);
            }
            try
            {
                await database.SaveChangesAsync(ct);
                markCommitAttempted();
                await transaction.CommitAsync(ct);
            }
            catch { revisions.Discard(); throw; }
            // No external I/O or notifications occurs before the gate aborts revoked sockets.
            revisions.Committed();
        }, cancellationToken);
}
