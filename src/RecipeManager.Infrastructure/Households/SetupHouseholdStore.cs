using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Configuration;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Infrastructure.Households;

public sealed class SetupHouseholdStore(RecipeManagerDbContext database, UserManager<ApplicationUser> users,
    IConfiguration configuration, TimeProvider clock, IEnumerable<IHouseholdInitializer> initializers) : ISetupHouseholdStore
{
    public async Task<bool> SetupRequiredAsync(CancellationToken cancellationToken)
        => !(await database.Installations.AsNoTracking().SingleAsync(cancellationToken)).SetupCompletedAt.HasValue;

    public async Task<HouseholdSession> CreateAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var installation = (await database.Installations.FromSqlInterpolated(
            $"SELECT * FROM households.\"Installation\" WHERE \"Id\" = {Installation.SingletonId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        if (installation.SetupCompletedAt is not null) throw new HouseholdConflictException("setup_already_completed");
        var configured = configuration["Setup:TokenFile"] is { Length: > 0 } path
            ? SecretConfiguration.ReadSecret(path) : configuration["Setup:Token"];
        if (string.IsNullOrWhiteSpace(configured) || configured.Length < 32)
            throw new InvalidOperationException("Configure a strong operator setup secret before initial registration.");
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(request.SetupToken))))
            throw new DomainValidationException("setupToken", "invalid", "The operator setup secret is invalid.");
        var user = new ApplicationUser(Guid.NewGuid(), request.Email);
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) throw new DomainValidationException("email", "account_invalid", "Account details could not be accepted.");
        var household = new Household(Guid.NewGuid(), request.HouseholdName, request.Timezone);
        database.BindHouseholdScope(household.Id);
        database.AddRange(household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Administrator));
        await database.SaveChangesAsync(cancellationToken);
        foreach (var initializer in initializers) await initializer.InitializeAsync(household.Id, cancellationToken);
        database.Entry(installation).Property(x => x.SetupCompletedAt).CurrentValue = clock.GetUtcNow();
        database.Entry(installation).Property(x => x.Revision).CurrentValue = checked(installation.Revision + 1);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(user.Id, household.Id, HouseholdRole.Administrator);
    }
}
