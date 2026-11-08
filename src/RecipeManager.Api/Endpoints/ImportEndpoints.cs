using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Endpoints;

public sealed record UrlImportWrite(string Url, Guid? HandoffId = null);
public sealed record TextImportWrite(string Text, Guid? HandoffId = null);

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this WebApplication app)
    {
        var handoffs = app.MapGroup("/api/v1/import-handoffs").WithTags("Imports").RequireAuthorization(IdentityConfiguration.MemberPolicy);
        handoffs.MapPost("/", async (RegisterImportHandoff request, CurrentMembership membership, ImportHandoffService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            var result = await imports.RegisterAsync(await Actor(membership, ct), request, ct);
            return TypedResults.Created("/api/v1/import-handoffs/" + result.HandoffId, result);
        }).WithName("RegisterImportHandoff");
        handoffs.MapGet("/{handoffId:guid}", async (Guid handoffId, CurrentMembership membership, ImportHandoffService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            return TypedResults.Ok(await imports.StatusAsync(await Actor(membership, ct), handoffId, ct));
        }).WithName("GetImportHandoff");
        handoffs.MapPost("/{handoffId:guid}/commit", async (Guid handoffId, CommitImport request, CurrentMembership membership, ImportHandoffService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            var outcome = await imports.CommitHandoffAsync(await Actor(membership, ct), handoffId, request, ct);
            return outcome.Replayed ? Results.Ok(outcome.Result) : Results.Created("/api/v1/recipes/" + outcome.Result.RecipeId, outcome.Result);
        }).WithName("CommitImportHandoff").Produces<ImportCommitResult>(201).Produces<ImportCommitResult>();
        var drafts = app.MapGroup("/api/v1/imports").WithTags("Imports").RequireAuthorization(IdentityConfiguration.MemberPolicy);
        drafts.MapPost("/url", async (UrlImportWrite request, CurrentMembership membership, RecipeImportService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            var result = await imports.StartAsync(await Actor(membership, ct), new(ImportSourceKind.Url, request.Url, request.HandoffId), RetryKey(context), ct);
            return Started(result, context);
        }).WithName("ImportRecipeUrl").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<ImportStartResult>(201).Produces<ImportStartResult>();
        drafts.MapPost("/text", async (TextImportWrite request, CurrentMembership membership, RecipeImportService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            var result = await imports.StartAsync(await Actor(membership, ct), new(ImportSourceKind.Text, request.Text, request.HandoffId), RetryKey(context), ct);
            return Started(result, context);
        }).WithName("ImportRecipeText").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<ImportStartResult>(201).Produces<ImportStartResult>();
        drafts.MapPost("/{id:guid}/commit", async (Guid id, ImportReview request, CurrentMembership membership, ImportHandoffService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            var revision = TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id);
            var key = RetryKey(context);
            var outcome = await imports.CommitDraftAsync(await Actor(membership, ct), id, revision, request, key, ct);
            return outcome.Replayed ? Results.Ok(outcome.Result) : Results.Created("/api/v1/recipes/" + outcome.Result.RecipeId, outcome.Result);
        }).WithName("CommitImportDraft").WithMetadata(new RetryableCreationContract(), new AggregateContract(RequiresIfMatch: true)).Produces<ImportCommitResult>(201).Produces<ImportCommitResult>();
        drafts.MapGet("/{id:guid}", async (Guid id, CurrentMembership membership, ImportHandoffService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            var result = await imports.ReadDraftAsync(await Actor(membership, ct), id, ct);
            var etag = TransportConventions.ETag(id, result.Revision); context.Response.Headers.ETag = etag;
            return context.Request.Headers.IfNoneMatch == etag ? Results.StatusCode(304) : Results.Ok(result);
        }).WithName("GetImportDraft").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<ImportDraftRead>().Produces(304);
        drafts.MapPut("/{id:guid}", async (Guid id, ImportReview request, CurrentMembership membership, ImportHandoffService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            var result = await imports.ReviewAsync(await Actor(membership, ct), id,
                TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), request, ct);
            context.Response.Headers.ETag = TransportConventions.ETag(id, result.Revision);
            return TypedResults.Ok(result);
        }).WithName("ReviewImportDraft").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true));
        drafts.MapDelete("/{id:guid}", async (Guid id, CurrentMembership membership, ImportHandoffService imports, HttpContext context, CancellationToken ct) =>
        {
            Private(context);
            await imports.DeleteDraftAsync(await Actor(membership, ct), id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), ct);
            return TypedResults.NoContent();
        }).WithName("DeleteImportDraft").WithMetadata(new AggregateContract(RequiresIfMatch: true));
    }
    private static IResult Started(ImportStartOutcome outcome, HttpContext context)
    {
        if (outcome.Result.Draft is { } draft) context.Response.Headers.ETag = TransportConventions.ETag(draft.Id, draft.Revision);
        return outcome.Replayed ? Results.Ok(outcome.Result) : Results.Created($"/api/v1/imports/{outcome.Result.Draft!.Id}", outcome.Result);
    }
    private static Guid? RetryKey(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var supplied)) return null;
        if (!Guid.TryParseExact(supplied, "D", out var parsed) || parsed == Guid.Empty) throw new ApiContractException(422, "invalid_idempotency_key");
        return parsed;
    }
    private static void Private(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-cache";
        context.Response.Headers.Vary = "Cookie";
    }
    private static async Task<RequestActor> Actor(CurrentMembership membership, CancellationToken ct)
        => await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
}
