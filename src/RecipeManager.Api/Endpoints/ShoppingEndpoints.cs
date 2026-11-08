using System.Globalization;
using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Endpoints;

public sealed record ShoppingListPageRead(IReadOnlyList<ShoppingListBrief> Items, string? NextCursor);

public static class ShoppingEndpoints
{
    public static void MapShoppingEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/shopping-lists").WithTags("Shopping Lists").RequireAuthorization(IdentityConfiguration.MemberPolicy);
        api.MapGet("", async (int? limit, string? cursor, HttpContext context, CurrentMembership membership, IShoppingStore store, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var size = TransportConventions.PageSize(limit); var after = TransportConventions.ReadCursor(cursor);
            var result = await store.ListAsync(actor, after, size, ct);
            var tag = $"\"shopping:{actor.HouseholdId:N}:{result.HouseholdRevision.ToString(CultureInfo.InvariantCulture)}:{after?.ToString("N") ?? "start"}:{size.ToString(CultureInfo.InvariantCulture)}\"";
            return Conditional(context, tag, new ShoppingListPageRead(result.Items, result.NextId is { } id ? TransportConventions.Cursor(id) : null));
        }).WithName("ListShoppingLists").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<ShoppingListPageRead>().Produces(304);
        api.MapGet("/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, IShoppingStore store, CancellationToken ct) =>
        {
            var result = await store.ReadAsync(await Actor(membership, ct), id, ct);
            return Conditional(context, TransportConventions.ETag(id, result.Revision), result);
        }).WithName("GetShoppingList").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<ShoppingListRead>().Produces(304);
        api.MapPost("", async (ShoppingListWrite request, HttpContext context, CurrentMembership membership, IShoppingStore store, CancellationToken ct)
            => Created(context, await store.CreateAsync(await Actor(membership, ct), request, RetryKey(context), ct)))
            .WithName("CreateShoppingList").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<ShoppingListRead>(201);
        api.MapPost("/generate", async (ShoppingGenerateWrite request, HttpContext context, CurrentMembership membership, GenerateShoppingList generate, CancellationToken ct)
            => Created(context, await generate.GenerateAsync(await Actor(membership, ct), request, RetryKey(context), ct)))
            .WithName("GenerateShoppingList").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<ShoppingListRead>(201);
        api.MapPut("/{id:guid}", async (Guid id, ShoppingListWrite request, HttpContext context, CurrentMembership membership, IShoppingStore store, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await Expected(context, actor, id, store, ct);
            return Updated(context, await store.RenameAsync(actor, id, expected, request, ct));
        }).WithName("RenameShoppingList").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ShoppingListRead>();
        api.MapDelete("/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, IShoppingStore store, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await Expected(context, actor, id, store, ct);
            await store.DeleteAsync(actor, id, expected, ct); return Results.NoContent();
        }).WithName("DeleteShoppingList").WithMetadata(new AggregateContract(RequiresIfMatch: true)).Produces(204);
        api.MapPost("/{id:guid}/items", async (Guid id, ShoppingManualItemWrite request, HttpContext context, CurrentMembership membership, IShoppingStore store, EditShoppingList edit, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await Expected(context, actor, id, store, ct);
            return Created(context, await edit.AddAsync(actor, id, expected, request, RetryKey(context), ct));
        }).WithName("AddShoppingItem").WithMetadata(new RetryableCreationContract(), new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ShoppingListRead>(201);
        api.MapPut("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, ShoppingItemWrite request, HttpContext context, CurrentMembership membership, IShoppingStore store, EditShoppingList edit, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await Expected(context, actor, id, store, ct);
            return Updated(context, await edit.EditAsync(actor, id, itemId, expected, request, ct));
        }).WithName("EditShoppingItem").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ShoppingListRead>();
        api.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, HttpContext context, CurrentMembership membership, IShoppingStore store, EditShoppingList edit, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await Expected(context, actor, id, store, ct);
            return Updated(context, await edit.RemoveAsync(actor, id, itemId, expected, ct));
        }).WithName("RemoveShoppingItem").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ShoppingListRead>();
        api.MapPost("/{id:guid}/sources/preview", async (Guid id, ShoppingPreviewWrite request, HttpContext context, CurrentMembership membership, IShoppingStore store, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await Expected(context, actor, id, store, ct);
            var list = await store.ReadAsync(actor, id, ct);
            if (request.Selection is null) throw new DomainValidationException("selection", "required", "Choose recipes or meals to add.");
            var selection = GenerateShoppingList.AddSelection(list.Selection, request.Selection, list.Basis);
            var result = await store.PreviewAsync(actor, id, expected, new(selection, list.Basis), ct);
            context.Response.Headers.ETag = TransportConventions.ETag(id, result.ListRevision); return Results.Ok(result);
        }).WithName("PreviewShoppingSourceAddition").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ShoppingPreview>();
        api.MapPost("/{id:guid}/reconciliation/preview", async (Guid id, ShoppingPreviewWrite request, HttpContext context, CurrentMembership membership, IShoppingStore store, ReconcileShoppingList reconcile, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await Expected(context, actor, id, store, ct);
            var result = await reconcile.PreviewAsync(actor, id, expected, request, ct);
            context.Response.Headers.ETag = TransportConventions.ETag(id, result.ListRevision); return Results.Ok(result);
        }).WithName("PreviewShoppingReconciliation").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ShoppingPreview>();
        api.MapPost("/{id:guid}/reconciliation/apply", async (Guid id, ShoppingApplyWrite request, HttpContext context, CurrentMembership membership, IShoppingStore store, ReconcileShoppingList reconcile, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await Expected(context, actor, id, store, ct);
            return Updated(context, await reconcile.ApplyAsync(actor, id, expected, request, ct));
        }).WithName("ApplyShoppingReconciliation").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ShoppingListRead>();
    }
    private static async Task<RequestActor> Actor(CurrentMembership membership, CancellationToken ct) => await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
    private static async Task<long> Expected(HttpContext context, RequestActor actor, Guid id, IShoppingStore store, CancellationToken ct)
    {
        try { return TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id); }
        catch (ApiContractException) { _ = await store.ReadAsync(actor, id, ct); throw; }
    }
    private static Guid? RetryKey(HttpContext context)
    {
        var header = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrEmpty(header)) return null;
        return Guid.TryParse(header, out var value) && value != Guid.Empty ? value : throw new DomainValidationException("Idempotency-Key", "invalid", "Use a UUID retry key.");
    }
    private static IResult Created(HttpContext context, ShoppingListRead list)
    {
        context.Response.Headers.ETag = TransportConventions.ETag(list.Id, list.Revision);
        return Results.Created("/api/v1/shopping-lists/" + list.Id, list);
    }
    private static IResult Updated(HttpContext context, ShoppingListRead list)
    {
        context.Response.Headers.ETag = TransportConventions.ETag(list.Id, list.Revision); return Results.Ok(list);
    }
    private static IResult Conditional<T>(HttpContext context, string tag, T body)
    {
        context.Response.Headers.ETag = tag;
        return context.Request.Headers.IfNoneMatch == tag ? Results.StatusCode(304) : Results.Ok(body);
    }
}
