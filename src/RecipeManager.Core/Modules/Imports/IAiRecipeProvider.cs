namespace RecipeManager.Core.Modules.Imports;

// Server-only configuration. A class deliberately avoids a record's generated credential-bearing ToString.
public sealed class AiRecipeProviderConfiguration(bool enabled, string baseUrl, string model, string? apiKey)
{
    public bool Enabled { get; } = enabled;
    public string BaseUrl { get; } = baseUrl;
    public string Model { get; } = model;
    public string? ApiKey { get; } = apiKey;
}
public interface IAiRecipeProvider
{
    Task<ImportExtraction> ExtractAsync(string originalText, AiRecipeProviderConfiguration configuration, CancellationToken cancellationToken);
}
