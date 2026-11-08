using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RecipeManager.Core.Modules.Households;

namespace RecipeManager.Infrastructure.Security;

public sealed record ProviderConfiguration(bool Enabled, AiProviderKind Provider, string Model, string BaseUrl, string? ApiKey)
{
    public AiSettingsRead Public() => new(Enabled, Provider, Model, BaseUrl, !string.IsNullOrEmpty(ApiKey));
}

public sealed class ProtectedProviderSettings(IDataProtectionProvider provider)
{
    public ProviderConfiguration Read(Guid householdId, string? encrypted)
        => encrypted is null ? new(false, AiProviderKind.OpenAICompatible, "", "", null)
            : JsonSerializer.Deserialize<ProviderConfiguration>(Protector(householdId).Unprotect(encrypted))
                ?? throw new InvalidOperationException("Provider configuration could not be read.");
    public string Protect(Guid householdId, ProviderConfiguration configuration)
        => Protector(householdId).Protect(JsonSerializer.Serialize(configuration));
    private IDataProtector Protector(Guid householdId)
        => provider.CreateProtector("RecipeManager.AiSettings.v1", householdId.ToString("N"));
}
