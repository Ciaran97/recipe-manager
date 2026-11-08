using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Imports;

public sealed class ImportStore(RecipeManagerDbContext database, TransactionBoundary transactions,
    IImportedRecipeWriter recipes, TimeProvider clock, CreationRetryStore retries, IImportIngredientMatcher matcher) : IImportStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    public Task<ImportStartResult?> ResumeExtractionAsync(RequestActor actor, ImportSource source, Guid? retryKey, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], token => ResumeWithinTransaction(actor, source, retryKey, token), ct);
    private async Task<ImportStartResult?> ResumeWithinTransaction(RequestActor actor, ImportSource source, Guid? retryKey, CancellationToken ct)
    {
        if (source.HandoffId is { } handoff)
        {
            var receipt = await RequiredHandoff(actor, handoff, ct);
            if (receipt.InputHash != ImportInput.Hash(source.Kind, source.Input)) throw new ImportStateException("handoff_input_changed");
            var status = await Status(receipt, ct);
            if (status.Result is not null) return new(null, status.Result);
            var existing = await database.ImportDrafts.Where(x => x.RequestedBy == actor.UserId && x.HandoffId == handoff && x.ExpiresAt > clock.GetUtcNow())
                .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
            return existing is null ? null : new(Read(existing), null);
        }
        if (retryKey is not { } key) return null;
        var prior = await retries.TryReadAsync(actor.HouseholdId, actor.UserId, SourceRoute(source), key, SourcePayload(source), ct);
        if (prior is null) return null;
        var stored = JsonSerializer.Deserialize<ImportDraftRead>(prior.Body, Json) ?? throw new InvalidOperationException("Missing import creation result.");
        return new(Read(await RequiredDraft(actor, stored.Id, ct)), null);
    }
    public Task<ImportStartOutcome> SaveImportedExtractionAsync(RequestActor actor, ImportSource source, ImportExtraction extracted, Guid? retryKey, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async token =>
        {
            var prior = await ResumeWithinTransaction(actor, source, retryKey, token);
            if (prior is not null) return new ImportStartOutcome(prior, true);
            var matches = await matcher.MatchWithinTransactionAsync(actor.HouseholdId, extracted.Recipes.SelectMany(x => x.Ingredients).Select(x => x.IngredientName).ToArray(), token);
            extracted = extracted with
            {
                Recipes = extracted.Recipes.Select(recipe => recipe with
                {
                    Ingredients = recipe.Ingredients.Select(row => row with
                    {
                        MatchedIngredientId = matches.GetValueOrDefault(row.IngredientName)?.Id,
                        MatchedIngredientName = matches.GetValueOrDefault(row.IngredientName)?.CanonicalName
                    }).ToArray(),
                    UncertaintyFields = recipe.UncertaintyFields.Concat(recipe.Ingredients.Select((row, index) => (row, index))
                    .Where(x => !matches.ContainsKey(x.row.IngredientName)).Select(x => $"ingredients[{x.index}].ingredientId")).ToArray()
                }).ToArray()
            };
            ImportDraftRead Create()
            {
                var standards = extracted.Recipes.Select(x => x.MeasurementStandard).Distinct().ToArray();
                var standard = standards.Length == 1 ? standards[0] : null;
                var flags = extracted.Recipes.SelectMany((recipe, index) => recipe.UncertaintyFields.Select(field => $"recipes[{index}].{field}")).ToArray();
                var draft = new ImportDraft(Guid.NewGuid(), actor.HouseholdId, actor.UserId, source.HandoffId, source.Kind, source.Input,
                    JsonSerializer.Serialize(extracted, Json), JsonSerializer.Serialize(flags, Json), standard, clock.GetUtcNow());
                database.ImportDrafts.Add(draft); return Read(draft);
            }
            if (retryKey is null || source.HandoffId is not null) return new ImportStartOutcome(new(Create(), null), false);
            var replayed = true;
            var outcome = await retries.ExecuteAsync(actor.HouseholdId, actor.UserId, SourceRoute(source), retryKey.Value, SourcePayload(source), _ =>
            {
                replayed = false; var created = Create();
                return Task.FromResult(new CreationOutcome(201, JsonSerializer.Serialize(created, Json), $"/api/v1/imports/{created.Id}", null));
            }, token);
            var result = JsonSerializer.Deserialize<ImportDraftRead>(outcome.Body, Json) ?? throw new InvalidOperationException("Missing import creation result.");
            return new ImportStartOutcome(new(result, null), replayed);
        }, ct);
    private static string SourceRoute(ImportSource source) => source.Kind == ImportSourceKind.Url ? "/api/v1/imports/url" : "/api/v1/imports/text";
    private static byte[] SourcePayload(ImportSource source) => JsonSerializer.SerializeToUtf8Bytes(source, Json);
    public Task<ImportHandoffRead> RegisterAsync(RequestActor actor, RegisterImportHandoff request, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async token =>
        {
            var receipt = await FindHandoff(actor, request.HandoffId, token);
            if (receipt is null)
            {
                receipt = new(request.HandoffId, actor.HouseholdId, actor.UserId, request.CapturedAt, clock.GetUtcNow(), request.InputHash);
                database.ImportHandoffs.Add(receipt);
            }
            else if (!string.Equals(receipt.InputHash, request.InputHash, StringComparison.OrdinalIgnoreCase))
                throw new ImportStateException("handoff_input_changed");
            return await Status(receipt, token);
        }, ct);
    public Task<ImportHandoffRead> StatusAsync(RequestActor actor, Guid id, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async token => await Status(await RequiredHandoff(actor, id, token), token), ct);

    public Task<ImportDraftRead> SaveExtractionAsync(RequestActor actor, ImportSource source, string extractedJson, string uncertaintyJson,
        MeasurementStandard? standard, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async token =>
        {
            if (source.HandoffId is { } id)
            {
                var receipt = await RequiredHandoff(actor, id, token);
                _ = await Status(receipt, token); // Committed/expired input cannot produce a replacement draft.
                receipt.RequireOpen(clock.GetUtcNow());
                if (!string.Equals(receipt.InputHash, ImportInput.Hash(source.Kind, source.Input), StringComparison.Ordinal))
                    throw new ImportStateException("handoff_input_changed");
            }
            var draft = new ImportDraft(Guid.NewGuid(), actor.HouseholdId, actor.UserId, source.HandoffId, source.Kind,
                source.Input, extractedJson, uncertaintyJson, standard, clock.GetUtcNow());
            database.ImportDrafts.Add(draft);
            return Read(draft);
        }, ct);
    public Task<ImportDraftRead> ReadDraftAsync(RequestActor actor, Guid id, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async token => Read(await RequiredDraft(actor, id, token)), ct);
    public Task<ImportDraftRead> ReviewAsync(RequestActor actor, Guid id, long expectedRevision, ImportReview review, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async token =>
        {
            var draft = await RequiredDraft(actor, id, token);
            Match(draft, expectedRevision);
            ImportHandoffService.ValidateReview(review);
            var recipe = recipes.Validate(review.Recipe);
            draft.Review(ReviewDocument(draft, recipe), ReviewFlags(recipe), review.ConfirmedMeasurementStandard!.Value, clock.GetUtcNow());
            return Read(draft);
        }, ct);
    public Task DeleteDraftAsync(RequestActor actor, Guid id, long expectedRevision, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async token =>
        {
            var draft = await RequiredDraft(actor, id, token); Match(draft, expectedRevision);
            database.ImportDrafts.Remove(draft); return true;
        }, ct);
    public Task<ImportCommitOutcome> CommitDraftAsync(RequestActor actor, Guid id, long expectedRevision, ImportReview review, Guid? retryKey, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], async token =>
        {
            // A bound draft always uses its durable receipt, even if the caller chooses the
            // ordinary route or supplies a new retry key. No ordinary retry record is created.
            var draft = await database.ImportDrafts.SingleOrDefaultAsync(x => x.Id == id && x.RequestedBy == actor.UserId, token);
            if (draft?.HandoffId is { } handoff)
                return await CommitHandoffWithinTransaction(actor, handoff, new(id, expectedRevision, review), token);
            if (retryKey is null)
                return new ImportCommitOutcome(await CreateOrdinary(draft, actor, expectedRevision, review, token), false);
            var replayed = true;
            var outcome = await retries.ExecuteAsync(actor.HouseholdId, actor.UserId, $"/api/v1/imports/{id}/commit", retryKey.Value,
                System.Text.Encoding.UTF8.GetBytes(CommitHash(review)), async cancellation =>
                {
                    replayed = false;
                    var result = await CreateOrdinary(draft, actor, expectedRevision, review, cancellation);
                    return new CreationOutcome(201, JsonSerializer.Serialize(result, Json), $"/api/v1/recipes/{result.RecipeId}", null);
                }, token);
            var original = JsonSerializer.Deserialize<ImportCommitResult>(outcome.Body, Json)
                ?? throw new InvalidOperationException("A successful import retry must contain its commit result.");
            await RequireRecipeExists(original, token);
            return new ImportCommitOutcome(original, replayed);
        }, ct);
    private async Task<ImportCommitResult> CreateOrdinary(ImportDraft? draft, RequestActor actor, long expectedRevision, ImportReview review, CancellationToken ct)
    {
        if (draft is null) throw new ImportStateException("draft_expired");
        var recipe = await CreateReviewed(actor, draft, expectedRevision, review, ct);
        // The temporary draft cannot be committed twice under a different retry key. The
        // ordinary retry result is saved atomically with recipe creation and this removal.
        database.ImportDrafts.Remove(draft);
        return new(recipe.RecipeId, recipe.Revision, StorageTime(clock.GetUtcNow()));
    }
    public Task<ImportCommitOutcome> CommitHandoffAsync(RequestActor actor, Guid id, CommitImport request, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], token => CommitHandoffWithinTransaction(actor, id, request, token), ct);
    private async Task<ImportCommitOutcome> CommitHandoffWithinTransaction(RequestActor actor, Guid id, CommitImport request, CancellationToken token)
    {
        var receipt = await RequiredHandoff(actor, id, token);
        var hash = CommitHash(request.Review);
        // Authorization precedes this point. Replay precedes draft/expiry/review checks.
        if (receipt.Replay(hash) is { } previous)
        {
            await RequireRecipeExists(previous, token);
            return new ImportCommitOutcome(previous, true);
        }
        receipt.RequireOpen(clock.GetUtcNow());
        var draft = await RequiredDraft(actor, request.DraftId, token);
        if (draft.HandoffId != id) throw new ImportStateException("handoff_required");
        var recipe = await CreateReviewed(actor, draft, request.ExpectedDraftRevision, request.Review, token);
        var now = StorageTime(clock.GetUtcNow());
        var result = new ImportCommitResult(recipe.RecipeId, recipe.Revision, now);
        receipt.Commit(hash, result, now);
        return new ImportCommitOutcome(result, false);
    }
    private async Task<ImportedRecipeIdentity> CreateReviewed(RequestActor actor, ImportDraft draft, long expectedRevision, ImportReview review, CancellationToken ct)
    {
        draft.RequireLive(clock.GetUtcNow());
        Match(draft, expectedRevision);
        ImportHandoffService.ValidateReview(review);
        var validated = recipes.Validate(review.Recipe);
        draft.Review(ReviewDocument(draft, validated), ReviewFlags(validated), review.ConfirmedMeasurementStandard!.Value, clock.GetUtcNow());
        draft.RequireReviewed(clock.GetUtcNow());
        return await recipes.CreateWithinTransactionAsync(actor, validated, ct);
    }
    // PostgreSQL timestamptz stores microseconds. First responses and later replays must
    // expose identical timestamps even when a TimeProvider supplies 100-nanosecond ticks.
    private static DateTimeOffset StorageTime(DateTimeOffset value)
        => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    private static string CommitHash(ImportReview review)
    {
        // Decimal scale is a wire-format detail: 2 and 2.0 describe the same serving quantity.
        var normalized = review with
        {
            Recipe = review.Recipe with
            {
                DefaultServings = decimal.Parse(review.Recipe.DefaultServings.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)
            }
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, Json)));
    }
    private async Task<ImportHandoffRead> Status(ImportHandoff receipt, CancellationToken ct)
    {
        if (receipt.Result is { } result) await RequireRecipeExists(result, ct);
        else receipt.RequireOpen(clock.GetUtcNow());
        return new(receipt.HandoffId, receipt.State, StorageTime(receipt.ExpiresAt), receipt.Result);
    }
    private async Task RequireRecipeExists(ImportCommitResult result, CancellationToken ct)
    {
        if (!await database.Recipes.AnyAsync(x => x.Id == result.RecipeId, ct)) throw new ImportStateException("committed_recipe_deleted", result);
    }
    private Task<ImportHandoff?> FindHandoff(RequestActor actor, Guid id, CancellationToken ct)
        => database.ImportHandoffs.FromSqlInterpolated($"SELECT * FROM imports.\"Handoffs\" WHERE \"HouseholdId\" = {actor.HouseholdId} AND \"UserId\" = {actor.UserId} AND \"HandoffId\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
    private async Task<ImportHandoff> RequiredHandoff(RequestActor actor, Guid id, CancellationToken ct)
        => await FindHandoff(actor, id, ct) ?? throw new ResourceMissingException();
    private async Task<ImportDraft> RequiredDraft(RequestActor actor, Guid id, CancellationToken ct)
    {
        var draft = await database.ImportDrafts.SingleOrDefaultAsync(x => x.Id == id && x.RequestedBy == actor.UserId, ct)
            ?? throw new ImportStateException("draft_expired");
        draft.RequireLive(clock.GetUtcNow()); return draft;
    }
    private static void Match(ImportDraft draft, long expected)
    {
        if (draft.Revision != expected) throw new RevisionConflictException();
    }
    private static string ReviewDocument(ImportDraft draft, RecipeWrite recipe)
    {
        using var document = JsonDocument.Parse(draft.ExtractedJson);
        var root = document.RootElement;
        JsonElement? original = root.TryGetProperty("extraction", out var nested) ? nested
            : root.TryGetProperty("recipes", out _) ? root : null;
        return JsonSerializer.Serialize(new { extraction = original, reviewedRecipe = recipe }, Json);
    }
    private static string ReviewFlags(RecipeWrite recipe)
        => JsonSerializer.Serialize(recipe.Ingredients.SelectMany((row, index) => row.Kind == QuantityKind.Unknown
            ? new[] { $"ingredients[{index}].quantity" } : row.Uncertain ? new[] { $"ingredients[{index}].review" } : Array.Empty<string>()), Json);
    private static ImportDraftRead Read(ImportDraft draft)
    {
        using var document = JsonDocument.Parse(draft.ExtractedJson);
        var root = document.RootElement;
        ImportExtraction? extracted = null; RecipeWrite? reviewed = null;
        if (root.TryGetProperty("recipes", out _)) extracted = root.Deserialize<ImportExtraction>(Json);
        else if (root.TryGetProperty("extraction", out var nested) && nested.ValueKind == JsonValueKind.Object) extracted = nested.Deserialize<ImportExtraction>(Json);
        if (root.TryGetProperty("reviewedRecipe", out var saved)) reviewed = saved.Deserialize<RecipeWrite>(Json);
        return new(draft.Id, draft.Revision, draft.HandoffId, draft.SourceKind, draft.OriginalInput,
            JsonSerializer.Deserialize<string[]>(draft.UncertaintyJson, Json) ?? [], draft.MeasurementStandard, StorageTime(draft.ExpiresAt), extracted, reviewed);
    }
}
