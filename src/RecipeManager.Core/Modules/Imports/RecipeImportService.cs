using RecipeManager.Core.Modules.Households.Contracts;

namespace RecipeManager.Core.Modules.Imports;

public interface IWebsiteRecipeProvider
{
    Task<ImportExtraction> ImportAsync(string url, CancellationToken cancellationToken);
}
public interface ITextRecipeImporter
{
    Task<ImportExtraction> ImportAsync(string originalText, AiRecipeProviderConfiguration configuration, CancellationToken cancellationToken);
}
public sealed record ImportStartResult(ImportDraftRead? Draft, ImportCommitResult? CommitResult);
public sealed record ImportStartOutcome(ImportStartResult Result, bool Replayed);

public sealed class RecipeImportService(IImportStore drafts, IWebsiteRecipeProvider websites, ITextRecipeImporter text, IRecipeImportProviderSettings settings)
{
    public async Task<ImportStartOutcome> StartAsync(RequestActor actor, ImportSource source, Guid? retryKey, CancellationToken ct = default)
    {
        ImportInput.Validate(source.Kind, source.Input);
        var prior = await drafts.ResumeExtractionAsync(actor, source, retryKey, ct);
        if (prior is not null) return new(prior, true);
        // External work occurs between two fresh authenticated transactions, never under locks.
        ImportExtraction extracted;
        if (source.Kind == ImportSourceKind.Url) extracted = await websites.ImportAsync(source.Input, ct);
        else
        {
            var provider = await settings.ReadProviderAsync(actor, ct);
            extracted = await text.ImportAsync(source.Input, new(provider.Enabled, provider.BaseUrl, provider.Model, provider.ApiKey), ct);
        }
        return await drafts.SaveImportedExtractionAsync(actor, source, extracted, retryKey, ct);
    }
}
