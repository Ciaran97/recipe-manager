using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Infrastructure.Households;

public sealed class SettingsStore(RecipeManagerDbContext database, TransactionBoundary transactions, RevisionCoordinator revisions,
    ProtectedProviderSettings providers) : ISettingsStore, IRecipeImportProviderSettings
{
    public Task<RecipeImportProviderSettings> ReadProviderAsync(RequestActor actor, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var household = await database.Households.AsNoTracking().SingleAsync(x => x.Id == actor.HouseholdId, ct);
            var configuration = providers.Read(household.Id, household.ProtectedAiSettings);
            return new RecipeImportProviderSettings(configuration.Enabled, configuration.BaseUrl, configuration.Model, configuration.ApiKey);
        }, cancellationToken);
    public Task<ProfileRead> ProfileAsync(RequestActor actor, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], ct => ReadProfile(actor.UserId, ct), cancellationToken);

    public Task<ProfileRead> UpdateProfileAsync(RequestActor actor, long revision, string name, JsonElement preferences, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var user = await database.Users.SingleAsync(x => x.Id == actor.UserId, ct);
            if (user.PreferencesRevision != revision) throw new RevisionConflictException();
            database.Entry(user).Property(x => x.DisplayName).CurrentValue = name;
            database.Entry(user).Property(x => x.PreferencesJson).CurrentValue = preferences.GetRawText();
            await revisions.PersonalAsync(actor.HouseholdId, actor.UserId, ct);
            return Profile(user.Id, user.DisplayName, user.PreferencesJson, user.PreferencesRevision);
        }, cancellationToken);

    public Task<HouseholdSettingsRead> HouseholdAsync(RequestActor actor, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var household = await database.Households.AsNoTracking().SingleAsync(x => x.Id == actor.HouseholdId, ct);
            return Read(household);
        }, cancellationToken);

    public Task<HouseholdSettingsRead> UpdateHouseholdAsync(RequestActor actor, long revision, string name, string timezone, AiSettingsWrite? ai, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, true, [], async ct =>
        {
            var household = await database.Households.SingleAsync(x => x.Id == actor.HouseholdId, ct);
            if (household.Revision != revision) throw new RevisionConflictException();
            database.Entry(household).Property(x => x.Name).CurrentValue = name;
            database.Entry(household).Property(x => x.TimeZone).CurrentValue = timezone;
            if (ai is not null)
            {
                var existing = providers.Read(household.Id, household.ProtectedAiSettings);
                var key = ai.ClearApiKey ? null : ai.ApiKey ?? existing.ApiKey;
                database.Entry(household).Property(x => x.ProtectedAiSettings).CurrentValue = providers.Protect(household.Id,
                    new(ai.Enabled, ai.Provider, ai.Model, ai.BaseUrl, key));
            }
            database.Entry(household).Property(x => x.Revision).CurrentValue = checked(household.Revision + 1);
            await revisions.SharedAsync(household.Id, ChangedDomain.Settings, false, ct);
            return Read(household);
        }, cancellationToken);

    private HouseholdSettingsRead Read(Household household)
        => new(household.Id, household.Name, household.TimeZone, providers.Read(household.Id, household.ProtectedAiSettings).Public(), household.Revision);
    private async Task<ProfileRead> ReadProfile(Guid userId, CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking().SingleAsync(x => x.Id == userId, cancellationToken);
        return Profile(user.Id, user.DisplayName, user.PreferencesJson, user.PreferencesRevision);
    }
    private static ProfileRead Profile(Guid id, string name, string json, long revision)
    {
        using var preferences = JsonDocument.Parse(json);
        return new(id, name, preferences.RootElement.Clone(), revision);
    }
}
