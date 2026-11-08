namespace RecipeManager.Core.Modules.Households.Contracts;

// Server-only decrypted settings, never an API response or diagnostic record.
public sealed class RecipeImportProviderSettings(bool enabled, string baseUrl, string model, string? apiKey)
{
    public bool Enabled { get; } = enabled;
    public string BaseUrl { get; } = baseUrl;
    public string Model { get; } = model;
    public string? ApiKey { get; } = apiKey;
}
public interface IRecipeImportProviderSettings
{
    Task<RecipeImportProviderSettings> ReadProviderAsync(RequestActor actor, CancellationToken cancellationToken);
}
