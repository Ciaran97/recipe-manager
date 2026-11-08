using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Imports;

public sealed record RegisterImportHandoff(Guid HandoffId, DateTimeOffset CapturedAt, string InputHash);
public sealed record ImportSource(ImportSourceKind Kind, string Input, Guid? HandoffId = null);
public sealed record ImportReview(RecipeWrite Recipe, MeasurementStandard? ConfirmedMeasurementStandard, bool ReviewConfirmed);
public sealed record CommitImport(Guid DraftId, long ExpectedDraftRevision, ImportReview Review);
public sealed record ImportCommitOutcome(ImportCommitResult Result, bool Replayed);
public sealed record ImportHandoffRead(Guid HandoffId, ImportHandoffState State, DateTimeOffset ExpiresAt, ImportCommitResult? Result);
public sealed record ImportDraftRead(Guid Id, long Revision, Guid? HandoffId, ImportSourceKind SourceKind, string OriginalInput,
    IReadOnlyList<string> UncertaintyFields, MeasurementStandard? MeasurementStandard, DateTimeOffset ExpiresAt, ImportExtraction? Extraction = null, RecipeWrite? ReviewedRecipe = null);

public interface IImportStore
{
    Task<ImportStartResult?> ResumeExtractionAsync(RequestActor actor, ImportSource source, Guid? retryKey, CancellationToken cancellationToken);
    Task<ImportStartOutcome> SaveImportedExtractionAsync(RequestActor actor, ImportSource source, ImportExtraction extracted, Guid? retryKey, CancellationToken cancellationToken);
    Task<ImportHandoffRead> RegisterAsync(RequestActor actor, RegisterImportHandoff request, CancellationToken cancellationToken);
    Task<ImportHandoffRead> StatusAsync(RequestActor actor, Guid handoffId, CancellationToken cancellationToken);
    Task<ImportDraftRead> SaveExtractionAsync(RequestActor actor, ImportSource source, string extractedJson, string uncertaintyJson, MeasurementStandard? standard, CancellationToken cancellationToken);
    Task<ImportDraftRead> ReadDraftAsync(RequestActor actor, Guid id, CancellationToken cancellationToken);
    Task<ImportDraftRead> ReviewAsync(RequestActor actor, Guid id, long expectedRevision, ImportReview review, CancellationToken cancellationToken);
    Task DeleteDraftAsync(RequestActor actor, Guid id, long expectedRevision, CancellationToken cancellationToken);
    Task<ImportCommitOutcome> CommitDraftAsync(RequestActor actor, Guid id, long expectedRevision, ImportReview review, Guid? retryKey, CancellationToken cancellationToken);
    Task<ImportCommitOutcome> CommitHandoffAsync(RequestActor actor, Guid handoffId, CommitImport request, CancellationToken cancellationToken);
}

public sealed class ImportHandoffService(IImportStore store)
{
    public Task<ImportHandoffRead> RegisterAsync(RequestActor actor, RegisterImportHandoff request, CancellationToken ct = default)
    {
        DomainRules.NonEmptyId(request.HandoffId, "handoffId");
        _ = ImportInput.Digest(request.InputHash, "inputHash");
        return store.RegisterAsync(actor, request, ct);
    }
    public Task<ImportHandoffRead> StatusAsync(RequestActor actor, Guid id, CancellationToken ct = default)
        => store.StatusAsync(actor, DomainRules.NonEmptyId(id, "handoffId"), ct);
    // Internal server-side extraction result entry point, never bound directly to an HTTP request.
    public Task<ImportDraftRead> SaveExtractionAsync(RequestActor actor, ImportSource source, string extractedJson, string uncertaintyJson, MeasurementStandard? standard, CancellationToken ct = default)
    {
        ImportInput.Validate(source.Kind, source.Input);
        return store.SaveExtractionAsync(actor, source, extractedJson, uncertaintyJson, standard, ct);
    }
    public Task<ImportDraftRead> ReadDraftAsync(RequestActor actor, Guid id, CancellationToken ct = default)
        => store.ReadDraftAsync(actor, DomainRules.NonEmptyId(id, "draftId"), ct);
    public Task<ImportDraftRead> ReviewAsync(RequestActor actor, Guid id, long revision, ImportReview review, CancellationToken ct = default)
        => store.ReviewAsync(actor, DomainRules.NonEmptyId(id, "draftId"), DomainRules.Revision(revision), review, ct);
    public Task DeleteDraftAsync(RequestActor actor, Guid id, long revision, CancellationToken ct = default)
        => store.DeleteDraftAsync(actor, DomainRules.NonEmptyId(id, "draftId"), DomainRules.Revision(revision), ct);
    public Task<ImportCommitOutcome> CommitDraftAsync(RequestActor actor, Guid id, long revision, ImportReview review, Guid? retryKey, CancellationToken ct = default)
        => store.CommitDraftAsync(actor, DomainRules.NonEmptyId(id, "draftId"), DomainRules.Revision(revision), review, retryKey, ct);
    public Task<ImportCommitOutcome> CommitHandoffAsync(RequestActor actor, Guid id, CommitImport request, CancellationToken ct = default)
    {
        DomainRules.NonEmptyId(request.DraftId, "draftId"); DomainRules.Revision(request.ExpectedDraftRevision);
        return store.CommitHandoffAsync(actor, DomainRules.NonEmptyId(id, "handoffId"), request, ct);
    }
    public static void ValidateReview(ImportReview review)
    {
        if (review.ConfirmedMeasurementStandard is not { } standard || !Enum.IsDefined(standard))
            throw new DomainValidationException("confirmedMeasurementStandard", "review_required", "Confirm AU or US source measures before saving.");
        if (!review.ReviewConfirmed)
            throw new DomainValidationException("reviewConfirmed", "review_required", "Review all imported fields before saving.");
        if (review.Recipe.MeasurementStandard != standard)
            throw new DomainValidationException("measurementStandard", "standard_mismatch", "Recipe measures must match the confirmed source standard.");
    }
}
