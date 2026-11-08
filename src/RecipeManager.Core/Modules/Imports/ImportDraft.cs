using System.Text;
using System.Text.Json;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Imports;

/// <summary>Private, temporary review material. Saving this aggregate never creates a recipe.</summary>
public sealed class ImportDraft
{
    private ImportDraft() { }
    public ImportDraft(Guid id, Guid householdId, Guid requestedBy, Guid? handoffId, ImportSourceKind sourceKind,
        string originalInput, string extractedJson, string uncertaintyJson, MeasurementStandard? measurementStandard, DateTimeOffset now)
    {
        Id = DomainRules.NonEmptyId(id, "draftId");
        HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        RequestedBy = DomainRules.NonEmptyId(requestedBy, "requestedBy");
        if (handoffId is { } handoff) DomainRules.NonEmptyId(handoff, "handoffId");
        ImportInput.Validate(sourceKind, originalInput);
        ValidateDocument(extractedJson, uncertaintyJson, measurementStandard);
        HandoffId = handoffId;
        SourceKind = sourceKind;
        OriginalInput = originalInput;
        ExtractedJson = extractedJson;
        UncertaintyJson = uncertaintyJson;
        MeasurementStandard = measurementStandard;
        CreatedAt = now.ToUniversalTime();
        ExpiresAt = CreatedAt.AddHours(24);
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public Guid? HandoffId { get; private set; }
    public ImportSourceKind SourceKind { get; private set; }
    public string OriginalInput { get; private set; } = "";
    public string ExtractedJson { get; private set; } = "{}";
    public string UncertaintyJson { get; private set; } = "[]";
    public MeasurementStandard? MeasurementStandard { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public long Revision { get; private set; } = 1;

    public void RequireLive(DateTimeOffset now)
    {
        if (now >= ExpiresAt) throw new ImportStateException("draft_expired");
    }
    public void Review(string extractedJson, string uncertaintyJson, MeasurementStandard measurementStandard, DateTimeOffset now)
    {
        RequireLive(now);
        ValidateDocument(extractedJson, uncertaintyJson, measurementStandard);
        ExtractedJson = extractedJson;
        UncertaintyJson = uncertaintyJson;
        MeasurementStandard = measurementStandard;
        ReviewedAt = now.ToUniversalTime();
        Revision = checked(Revision + 1);
    }
    public void RequireReviewed(DateTimeOffset now)
    {
        RequireLive(now);
        if (MeasurementStandard is null)
            throw new DomainValidationException("measurementStandard", "review_required", "Confirm AU or US source measures before saving.");
        if (ReviewedAt is null)
            throw new DomainValidationException("review", "review_required", "Review imported recipe fields before saving.");
    }
    private static void ValidateDocument(string extractedJson, string uncertaintyJson, MeasurementStandard? standard)
    {
        if (standard is { } known && !Enum.IsDefined(known))
            throw new DomainValidationException("measurementStandard", "unsupported", "Choose AU or US measures.");
        ValidateJson(extractedJson, JsonValueKind.Object, "extractedRecipe");
        ValidateJson(uncertaintyJson, JsonValueKind.Array, "uncertaintyFields");
    }
    private static void ValidateJson(string json, JsonValueKind expected, string field)
    {
        try
        {
            if (json is null || new UTF8Encoding(false, true).GetByteCount(json) > 1024 * 1024)
                throw new DomainValidationException(field, "too_long", "Import documents must not exceed 1 MiB of UTF-8.");
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (parsed.RootElement.ValueKind != expected)
                throw new DomainValidationException(field, "invalid_shape", "The import document has an invalid shape.");
        }
        catch (JsonException)
        {
            throw new DomainValidationException(field, "invalid_json", "The import document must contain valid bounded JSON.");
        }
        catch (EncoderFallbackException)
        {
            throw new DomainValidationException(field, "invalid_unicode", "The import document must contain valid Unicode text.");
        }
    }
}
