using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Endpoints;

public sealed record OrganiserWrite(string Name);
public sealed record OrganiserListPage(IReadOnlyList<OrganiserRead> Items, string? NextCursor);
public static class RecipeOrganiserEndpoints
{
    public static void MapRecipeOrganiserEndpoints(this WebApplication app)
    {
        foreach (var kind in Enum.GetValues<OrganiserKind>())
        {
            var route = kind == OrganiserKind.Collection ? "collections" : "tags";
            var api = app.MapGroup("/api/v1/" + route).WithTags("Recipes").RequireAuthorization(IdentityConfiguration.MemberPolicy);
            api.MapGet("/", async (string? q, string? cursor, int? limit, CurrentMembership membership, RecipeOrganiserService service, CancellationToken ct) =>
            {
                var result = await service.ListAsync(await Actor(membership, ct), kind, q ?? "", TransportConventions.ReadCursor(cursor), TransportConventions.PageSize(limit), ct);
                return TypedResults.Ok(new OrganiserListPage(result.Items, result.NextId is { } id ? TransportConventions.Cursor(id) : null));
            }).WithName("List" + kind + "s");
            api.MapGet("/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, RecipeOrganiserService service, CancellationToken ct) =>
            {
                var result = await service.ReadAsync(await Actor(membership, ct), kind, id, ct);
                var etag = TransportConventions.ETag(id, result.Revision); context.Response.Headers.ETag = etag;
                context.Response.Headers.CacheControl = "private, no-cache";
                return context.Request.Headers.IfNoneMatch == etag ? Results.StatusCode(304) : Results.Ok(result);
            }).WithName("Get" + kind).WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<OrganiserRead>().Produces(304);
            api.MapPost("/", async (OrganiserWrite request, HttpContext context, CurrentMembership membership, RecipeOrganiserService service, CancellationToken ct) =>
            {
                Guid? key = null;
                if (context.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    if (!Guid.TryParseExact(header, "D", out var value) || value == Guid.Empty) throw new ApiContractException(422, "invalid_idempotency_key");
                    key = value;
                }
                var result = await service.CreateAsync(await Actor(membership, ct), kind, request.Name, key, ct);
                context.Response.Headers.ETag = TransportConventions.ETag(result.Id, result.Revision);
                return TypedResults.Created("/api/v1/" + route + "/" + result.Id, result);
            }).WithName("Create" + kind).WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true));
            api.MapPut("/{id:guid}", async (Guid id, OrganiserWrite request, HttpContext context, CurrentMembership membership, RecipeOrganiserService service, CancellationToken ct) =>
            {
                var actor = await Actor(membership, ct); _ = await service.ReadAsync(actor, kind, id, ct);
                var result = await service.UpdateAsync(actor, kind, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), request.Name, ct);
                context.Response.Headers.ETag = TransportConventions.ETag(id, result.Revision); return TypedResults.Ok(result);
            }).WithName("Update" + kind).WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true));
            api.MapDelete("/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, RecipeOrganiserService service, CancellationToken ct) =>
            {
                var actor = await Actor(membership, ct); _ = await service.ReadAsync(actor, kind, id, ct);
                await service.DeleteAsync(actor, kind, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), ct);
                return TypedResults.NoContent();
            }).WithName("Delete" + kind).WithMetadata(new AggregateContract(RequiresIfMatch: true));
        }
    }
    private static async Task<RequestActor> Actor(CurrentMembership membership, CancellationToken ct) => await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
}
