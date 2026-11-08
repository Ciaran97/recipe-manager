using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Endpoints;

public sealed record CreateIngredientWrite(string CanonicalName, Guid? CategoryId = null);
public sealed record UpdateIngredientWrite(string CanonicalName, Guid? CategoryId = null, bool RetainOldNameAsAlias = false);
public sealed record AliasWrite(string Name);
public sealed record CategoryWrite(string Name, int SortOrder = 0);
public sealed record IngredientPage(IReadOnlyList<IngredientRead> Items, string? NextCursor = null);
public sealed record CategoryPage(IReadOnlyList<CategoryRead> Items, string? NextCursor = null);

public static class IngredientEndpoints
{
    public static void MapIngredientEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Ingredients").RequireAuthorization(IdentityConfiguration.MemberPolicy);
        api.MapGet("/ingredients", async (string? q, string? cursor, int? limit, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var page = await catalogue.IngredientsAsync(await Actor(membership, ct), q, TransportConventions.ReadCursor(cursor), TransportConventions.PageSize(limit), ct);
            return TypedResults.Ok(new IngredientPage(page.Items, page.NextId is { } id ? TransportConventions.Cursor(id) : null));
        }).WithName("ListIngredients");
        api.MapGet("/ingredients/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var result = await catalogue.IngredientAsync(await Actor(membership, ct), id, ct);
            return Read(context, result, id, result.Revision);
        }).WithName("GetIngredient").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<IngredientRead>().Produces(304);
        api.MapPost("/ingredients", async (CreateIngredientWrite request, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var result = await catalogue.CreateAsync(await Actor(membership, ct), request.CanonicalName, request.CategoryId, RetryKey(context), ct);
            context.Response.Headers.ETag = TransportConventions.ETag(result.Id, result.Revision);
            return TypedResults.Created("/api/v1/ingredients/" + result.Id, result);
        }).WithName("CreateIngredient").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true));
        api.MapPut("/ingredients/{id:guid}", async (Guid id, UpdateIngredientWrite request, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); _ = await catalogue.IngredientAsync(actor, id, ct);
            var result = await catalogue.UpdateAsync(actor, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), request.CanonicalName,
                request.CategoryId, request.RetainOldNameAsAlias, ct);
            return Write(context, result, id, result.Revision);
        }).WithName("UpdateIngredient").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<IngredientRead>();
        api.MapDelete("/ingredients/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); _ = await catalogue.IngredientAsync(actor, id, ct);
            await catalogue.DeleteAsync(actor, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), ct);
            return TypedResults.NoContent();
        }).WithName("DeleteIngredient").WithMetadata(new AggregateContract(RequiresIfMatch: true));
        api.MapPost("/ingredients/{id:guid}/aliases", async (Guid id, AliasWrite request, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            var revision = await Expected(context, actor, id, catalogue, ct);
            var result = await catalogue.AddAliasAsync(actor, id, revision, request.Name, RetryKey(context), ct);
            return Write(context, result, id, result.Revision);
        }).WithName("AddIngredientAlias").WithMetadata(new RetryableCreationContract(), new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<IngredientRead>();
        api.MapPut("/ingredients/{id:guid}/aliases/{aliasId:guid}", async (Guid id, Guid aliasId, AliasWrite request, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); _ = await catalogue.IngredientAsync(actor, id, ct);
            var result = await catalogue.UpdateAliasAsync(actor, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), aliasId, request.Name, ct);
            return Write(context, result, id, result.Revision);
        }).WithName("UpdateIngredientAlias").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<IngredientRead>();
        api.MapDelete("/ingredients/{id:guid}/aliases/{aliasId:guid}", async (Guid id, Guid aliasId, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); _ = await catalogue.IngredientAsync(actor, id, ct);
            var result = await catalogue.RemoveAliasAsync(actor, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), aliasId, ct);
            return Write(context, result, id, result.Revision);
        }).WithName("RemoveIngredientAlias").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<IngredientRead>();
        api.MapGet("/ingredient-categories", async (string? cursor, int? limit, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var page = await catalogue.CategoriesAsync(await Actor(membership, ct), TransportConventions.ReadCursor(cursor), TransportConventions.PageSize(limit), ct);
            return TypedResults.Ok(new CategoryPage(page.Items, page.NextId is { } id ? TransportConventions.Cursor(id) : null));
        }).WithName("ListIngredientCategories");
        api.MapGet("/ingredient-categories/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var result = await catalogue.CategoryAsync(await Actor(membership, ct), id, ct);
            return Read(context, result, id, result.Revision);
        }).WithName("GetIngredientCategory").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<CategoryRead>().Produces(304);
        api.MapPost("/ingredient-categories", async (CategoryWrite request, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var result = await catalogue.CreateCategoryAsync(await Actor(membership, ct), request.Name, request.SortOrder, RetryKey(context), ct);
            context.Response.Headers.ETag = TransportConventions.ETag(result.Id, result.Revision);
            return TypedResults.Created("/api/v1/ingredient-categories/" + result.Id, result);
        }).WithName("CreateIngredientCategory").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true));
        api.MapPut("/ingredient-categories/{id:guid}", async (Guid id, CategoryWrite request, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); _ = await catalogue.CategoryAsync(actor, id, ct);
            var result = await catalogue.UpdateCategoryAsync(actor, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), request.Name, request.SortOrder, ct);
            return Write(context, result, id, result.Revision);
        }).WithName("UpdateIngredientCategory").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<CategoryRead>();
        api.MapDelete("/ingredient-categories/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, CatalogueService catalogue, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); _ = await catalogue.CategoryAsync(actor, id, ct);
            await catalogue.DeleteCategoryAsync(actor, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), ct);
            return TypedResults.NoContent();
        }).WithName("DeleteIngredientCategory").WithMetadata(new AggregateContract(RequiresIfMatch: true));
    }
    private static async Task<RequestActor> Actor(CurrentMembership membership, CancellationToken ct)
        => await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
    private static async Task<long> Expected(HttpContext context, RequestActor actor, Guid id, CatalogueService catalogue, CancellationToken ct)
    {
        try { return TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id); }
        catch (ApiContractException) { _ = await catalogue.IngredientAsync(actor, id, ct); throw; }
    }
    private static Guid? RetryKey(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var value)) return null;
        if (!Guid.TryParseExact(value, "D", out var key) || key == Guid.Empty) throw new ApiContractException(422, "invalid_idempotency_key");
        return key;
    }
    private static IResult Read<T>(HttpContext context, T body, Guid id, long revision)
    {
        var etag = TransportConventions.ETag(id, revision); context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "private, no-cache";
        return context.Request.Headers.IfNoneMatch == etag ? Results.StatusCode(304) : TypedResults.Ok(body);
    }
    private static IResult Write<T>(HttpContext context, T body, Guid id, long revision)
    {
        context.Response.Headers.ETag = TransportConventions.ETag(id, revision);
        return TypedResults.Ok(body);
    }
}
