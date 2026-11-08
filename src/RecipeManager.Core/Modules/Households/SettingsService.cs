using System.Text.Json;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Households;

public enum AiProviderKind { OpenAICompatible }
public sealed record AiSettingsWrite(bool Enabled, AiProviderKind Provider, string Model, string BaseUrl, string? ApiKey = null, bool ClearApiKey = false);
public sealed record AiSettingsRead(bool Enabled, AiProviderKind Provider, string Model, string BaseUrl, bool HasApiKey);
public sealed record ProfileRead(Guid Id, string Name, JsonElement Preferences, long Revision);
public sealed record HouseholdSettingsRead(Guid Id, string Name, string Timezone, AiSettingsRead Ai, long Revision);
public interface ISettingsStore
{
    Task<ProfileRead> ProfileAsync(RequestActor actor, CancellationToken cancellationToken);
    Task<ProfileRead> UpdateProfileAsync(RequestActor actor, long revision, string name, JsonElement preferences, CancellationToken cancellationToken);
    Task<HouseholdSettingsRead> HouseholdAsync(RequestActor actor, CancellationToken cancellationToken);
    Task<HouseholdSettingsRead> UpdateHouseholdAsync(RequestActor actor, long revision, string name, string timezone, AiSettingsWrite? ai, CancellationToken cancellationToken);
}

public sealed class SettingsService(ISettingsStore store, SessionRevocationService revocation)
{
    public Task<ProfileRead> ProfileAsync(RequestActor actor, CancellationToken cancellationToken = default) => store.ProfileAsync(actor, cancellationToken);
    public Task<ProfileRead> UpdateProfileAsync(RequestActor actor, long revision, string name, JsonElement preferences, CancellationToken cancellationToken = default)
    {
        if (preferences.ValueKind != JsonValueKind.Object || preferences.GetRawText().Length > 16384)
            throw new DomainValidationException("preferences", "invalid", "Preferences must be a bounded JSON object.");
        return store.UpdateProfileAsync(actor, DomainRules.Revision(revision), string.IsNullOrWhiteSpace(name) ? "" : DomainRules.Name(name), preferences.Clone(), cancellationToken);
    }
    public Task<HouseholdSettingsRead> HouseholdAsync(RequestActor actor, CancellationToken cancellationToken = default) => store.HouseholdAsync(actor, cancellationToken);
    public Task<HouseholdSettingsRead> UpdateHouseholdAsync(RequestActor actor, long revision, string name, string timezone, AiSettingsWrite? ai, CancellationToken cancellationToken = default)
    {
        if (ai is not null)
        {
            ai = ai with { Model = ai.Model ?? "", BaseUrl = ai.BaseUrl ?? "" };
            if (!Enum.IsDefined(ai.Provider)) throw new DomainValidationException("ai.provider", "unsupported", "Choose a supported AI provider.");
            if (ai.Model.Length > 200 || (ai.Enabled && string.IsNullOrWhiteSpace(ai.Model)))
                throw new DomainValidationException("ai.model", "invalid", "An enabled provider requires a model name up to 200 characters.");
            if (ai.Enabled && string.IsNullOrWhiteSpace(ai.BaseUrl))
                throw new DomainValidationException("ai.baseUrl", "required", "An enabled provider requires an HTTP or HTTPS base URL.");
            _ = DomainRules.HttpUrl(ai.BaseUrl);
            if (ai.ApiKey is { } key && (key.Length is < 1 or > 4096 || key.Contains('\r') || key.Contains('\n')))
                throw new DomainValidationException("ai.apiKey", "invalid", "Provider key must be bounded and contain no line breaks.");
            if (ai.ClearApiKey && ai.ApiKey is not null)
                throw new DomainValidationException("ai.apiKey", "conflict", "Choose either replacing or clearing the provider key.");
        }
        return store.UpdateHouseholdAsync(actor, DomainRules.Revision(revision), DomainRules.Name(name), HouseholdDates.TimeZone(timezone).Id, ai, cancellationToken);
    }
    public Task ChangePasswordAsync(RequestActor actor, long revision, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        => revocation.RevokeAsync(actor.UserId, RevocationCause.PasswordChange, actor, currentPassword, newPassword,
            expectedPreferencesRevision: DomainRules.Revision(revision), cancellationToken: cancellationToken);
}
