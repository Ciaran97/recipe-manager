using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Endpoints;

public sealed record ArchiveRecipeWrite(bool Archived);
public sealed record FavouriteRecipeWrite(bool Favourite);
public sealed record ServingPreviewWrite(decimal TargetServings);
public sealed record RecipeActionWrite;
public sealed record RecipeListPage(IReadOnlyList<RecipeRead> Items, string? NextCursor = null);

public static class RecipeEndpoints
{
    public static void MapRecipeEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/recipes").WithTags("Recipes").RequireAuthorization(IdentityConfiguration.MemberPolicy);
        api.MapGet("/", async (string? q, string? archived, bool? favouriteOnly, [FromQuery] Guid[]? collectionIds, [FromQuery] Guid[]? tagIds,
            string? cursor, int? limit, HttpContext context, CurrentMembership membership, RecipeService recipes, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            var result = await recipes.ListAsync(actor, new RecipeQuery(q ?? "", archived ?? "exclude", favouriteOnly ?? false,
                collectionIds ?? [], tagIds ?? [], TransportConventions.ReadCursor(cursor), TransportConventions.PageSize(limit)), ct);
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.QueryString.Value ?? "")));
            var etag = $"\"recipes:{actor.HouseholdId:N}:{result.HouseholdRecipesRevision.ToString(CultureInfo.InvariantCulture)}:{actor.UserId:N}:{result.PersonalRevision.ToString(CultureInfo.InvariantCulture)}:{fingerprint}\"";
            context.Response.Headers.ETag = etag; context.Response.Headers.CacheControl = "private, no-cache";
            return context.Request.Headers.IfNoneMatch == etag ? Results.StatusCode(304)
                : Results.Ok(new RecipeListPage(result.Page.Items, result.Page.NextId is { } id ? TransportConventions.Cursor(id) : null));
        }).WithName("ListRecipes").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<RecipeListPage>().Produces(304);
        api.MapGet("/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, RecipeService recipes, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var result = await recipes.ReadAsync(actor, id, ct);
            var etag = ETag(id, new RecipeVersion(actor.UserId, result.Recipe.Revision, result.PersonalRevision));
            context.Response.Headers.ETag = etag; context.Response.Headers.CacheControl = "private, no-cache";
            return context.Request.Headers.IfNoneMatch == etag ? Results.StatusCode(304) : Results.Ok(result.Recipe);
        }).WithName("GetRecipe").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<RecipeRead>().Produces(304);
        api.MapPost("/", async (RecipeWrite request, HttpContext context, CurrentMembership membership, RecipeService recipes, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            return Created(context, actor, await recipes.CreateAsync(actor, request, RetryKey(context), ct));
        }).WithName("CreateRecipe").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<RecipeRead>(201);
        api.MapPut("/{id:guid}", async (Guid id, RecipeWrite request, HttpContext context, CurrentMembership membership, RecipeService recipes, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var version = await Expected(context, actor, id, recipes, ct);
            return Updated(context, actor, await recipes.UpdateAsync(actor, id, version, request, ct));
        }).WithName("UpdateRecipe").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<RecipeRead>();
        api.MapPost("/{id:guid}/duplicate", async (Guid id, RecipeActionWrite request, HttpContext context, CurrentMembership membership, RecipeService recipes, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var version = await Expected(context, actor, id, recipes, ct);
            return Created(context, actor, await recipes.DuplicateAsync(actor, id, version, RetryKey(context), ct));
        }).WithName("DuplicateRecipe").WithMetadata(new RetryableCreationContract(), new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<RecipeRead>(201);
        api.MapPut("/{id:guid}/archive", async (Guid id, ArchiveRecipeWrite request, HttpContext context, CurrentMembership membership, RecipeService recipes, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var version = await Expected(context, actor, id, recipes, ct);
            return Updated(context, actor, await recipes.ArchiveAsync(actor, id, version, request.Archived, ct));
        }).WithName("ArchiveRecipe").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<RecipeRead>();
        api.MapPut("/{id:guid}/favourite", async (Guid id, FavouriteRecipeWrite request, HttpContext context, CurrentMembership membership, RecipeService recipes, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var version = await Expected(context, actor, id, recipes, ct);
            return Updated(context, actor, await recipes.FavouriteAsync(actor, id, version, request.Favourite, ct));
        }).WithName("FavouriteRecipe").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<RecipeRead>();
        api.MapDelete("/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, RecipeService recipes, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var version = await Expected(context, actor, id, recipes, ct);
            await recipes.DeleteAsync(actor, id, version, ct); return TypedResults.NoContent();
        }).WithName("DeleteRecipe").WithMetadata(new AggregateContract(RequiresIfMatch: true));
        api.MapPost("/{id:guid}/serving-preview", async (Guid id, ServingPreviewWrite request, CurrentMembership membership, RecipeService recipes, CancellationToken ct)
            => TypedResults.Ok(await recipes.PreviewAsync(await Actor(membership, ct), id, request.TargetServings, ct))).WithName("PreviewRecipeServings");
    }
    private static async Task<RequestActor> Actor(CurrentMembership membership, CancellationToken ct)
        => await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
    private static Guid? RetryKey(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var value)) return null;
        if (!Guid.TryParseExact(value, "D", out var key) || key == Guid.Empty) throw new ApiContractException(422, "invalid_idempotency_key");
        return key;
    }
    private static string ETag(Guid id, RecipeVersion version)
        => $"\"{id:N}:{version.Revision.ToString(CultureInfo.InvariantCulture)}:{version.UserId:N}:{version.PersonalRevision.ToString(CultureInfo.InvariantCulture)}\"";
    private static async Task<RecipeVersion> Expected(HttpContext context, RequestActor actor, Guid id, RecipeService recipes, CancellationToken ct)
    {
        var supplied = context.Request.Headers.IfMatch.ToString();
        var parts = supplied.Trim('"').Split(':');
        if (parts.Length == 4 && Guid.TryParseExact(parts[0], "N", out var resource) && resource == id
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            && Guid.TryParseExact(parts[2], "N", out var user) && user != Guid.Empty
            && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var personal))
        {
            var result = new RecipeVersion(user, revision, personal);
            if (supplied == ETag(id, result)) return result;
        }
        // Resolve the scoped target before reporting a malformed/missing validator. Successful
        // creation retries with valid original validators reach the durable retry lookup first.
        _ = await recipes.ReadAsync(actor, id, ct);
        throw new ApiContractException(string.IsNullOrWhiteSpace(supplied) ? 428 : 412,
            string.IsNullOrWhiteSpace(supplied) ? "precondition_required" : "stale_revision");
    }
    private static IResult Created(HttpContext context, RequestActor actor, RecipeResult result)
    {
        context.Response.Headers.ETag = ETag(result.Recipe.Id, new RecipeVersion(actor.UserId, result.Recipe.Revision, result.PersonalRevision));
        return Results.Created("/api/v1/recipes/" + result.Recipe.Id, result.Recipe);
    }
    private static IResult Updated(HttpContext context, RequestActor actor, RecipeResult result)
    {
        context.Response.Headers.ETag = ETag(result.Recipe.Id, new RecipeVersion(actor.UserId, result.Recipe.Revision, result.PersonalRevision));
        return Results.Ok(result.Recipe);
    }
}
