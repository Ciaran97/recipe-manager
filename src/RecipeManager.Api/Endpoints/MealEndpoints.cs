using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.Leftovers.Contracts;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Endpoints;

public static class MealEndpoints
{
    public static void MapMealEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Meal Planning").RequireAuthorization(IdentityConfiguration.MemberPolicy);
        api.MapGet("/meal-slots", async (HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var result = await planner.SlotsAsync(actor, ct);
            return Conditional(context, $"\"slots:{actor.HouseholdId:N}:{result.Revision.ToString(CultureInfo.InvariantCulture)}\"", result);
        }).WithName("ListMealSlots").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<MealSlotPage>().Produces(304);
        api.MapGet("/meal-slots/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var result = await planner.SlotAsync(await Actor(membership, ct), id, ct);
            return Conditional(context, TransportConventions.ETag(id, result.Revision), result);
        }).WithName("GetMealSlot").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<MealSlotRead>().Produces(304);
        api.MapPost("/meal-slots", async (MealSlotWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var result = await planner.CreateSlotAsync(await Actor(membership, ct), request, RetryKey(context), ct);
            context.Response.Headers.ETag = TransportConventions.ETag(result.Id, result.Revision);
            return Results.Created("/api/v1/meal-slots/" + result.Id, result);
        }).WithName("CreateMealSlot").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<MealSlotRead>(201);
        api.MapPut("/meal-slots/{id:guid}", async (Guid id, MealSlotWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            var expected = await ExpectedSimple(context, id, async () => { _ = await planner.SlotAsync(actor, id, ct); });
            var result = await planner.UpdateSlotAsync(actor, id, expected, request, ct);
            context.Response.Headers.ETag = TransportConventions.ETag(id, result.Revision); return Results.Ok(result);
        }).WithName("UpdateMealSlot").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<MealSlotRead>();
        api.MapDelete("/meal-slots/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            var expected = await ExpectedSimple(context, id, async () => { _ = await planner.SlotAsync(actor, id, ct); });
            await planner.DeleteSlotAsync(actor, id, expected, ct); return Results.NoContent();
        }).WithName("DeleteMealSlot").WithMetadata(new AggregateContract(RequiresIfMatch: true)).Produces(204);
        api.MapGet("/meals", async (DateOnly from, DateOnly to, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var result = await planner.CalendarAsync(actor, from, to, ct);
            return Conditional(context, $"\"calendar:{actor.HouseholdId:N}:{result.Revision.ToString(CultureInfo.InvariantCulture)}:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}\"", result);
        }).WithName("GetMealCalendar").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<MealCalendarRead>().Produces(304);
        api.MapGet("/meals/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var result = await planner.ReadAsync(await Actor(membership, ct), id, ct); return Conditional(context, MealTag(result), result);
        }).WithName("GetScheduledMeal").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<ScheduledMealRead>().Produces(304);
        api.MapPost("/meals/cooking", async (CookingWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct)
            => Created(context, await planner.CreateCookingAsync(await Actor(membership, ct), request, RetryKey(context), ct)))
            .WithName("ScheduleCooking").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<ScheduledMealRead>(201);
        api.MapPut("/meals/{id:guid}/cooking", async (Guid id, CookingWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            return Updated(context, await planner.UpdateCookingAsync(actor, id, expected, request, ct));
        }).WithName("UpdateCookingMeal").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>();
        api.MapPost("/meals/other", async (OtherMealWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct)
            => Created(context, await planner.CreateOtherAsync(await Actor(membership, ct), request, RetryKey(context), ct)))
            .WithName("ScheduleOtherMeal").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<ScheduledMealRead>(201);
        api.MapPut("/meals/{id:guid}/other", async (Guid id, OtherMealWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            return Updated(context, await planner.UpdateOtherAsync(actor, id, expected, request, ct));
        }).WithName("UpdateOtherMeal").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>();
        api.MapPost("/meals/leftovers", async (LeftoverWrite request, HttpContext context, CurrentMembership membership, AllocationService allocations, CancellationToken ct)
            => Created(context, await allocations.CreateAsync(await Actor(membership, ct), request, RetryKey(context), ct)))
            .WithName("ScheduleLeftovers").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true)).Produces<ScheduledMealRead>(201);
        api.MapPut("/meals/{id:guid}/leftovers", async (Guid id, LeftoverWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, AllocationService allocations, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            return Updated(context, await allocations.UpdateAsync(actor, id, expected, request, ct));
        }).WithName("UpdateLeftoverMeal").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>();
        api.MapPut("/meals/{id:guid}/position", async (Guid id, MealPositionWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            return Updated(context, await planner.MoveAsync(actor, id, expected, request, ct));
        }).WithName("MoveScheduledMeal").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>();
        api.MapPost("/meals/{id:guid}/duplicate", async (Guid id, DuplicateMealWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            return Created(context, await planner.DuplicateAsync(actor, id, expected, request, RetryKey(context), ct));
        }).WithName("DuplicateScheduledMeal").WithMetadata(new RetryableCreationContract(), new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>(201);
        api.MapDelete("/meals/{id:guid}", async (Guid id, [FromBody] DeleteMealWrite? request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            await planner.DeleteAsync(actor, id, expected, request ?? new DeleteMealWrite(), ct); return Results.NoContent();
        }).WithName("DeleteScheduledMeal").WithMetadata(new AggregateContract(RequiresIfMatch: true)).Produces(204);
        api.MapGet("/cooking-events/{id:guid}/leftovers", async (Guid id, HttpContext context, CurrentMembership membership, AllocationService allocations, CancellationToken ct) =>
        {
            var result = await allocations.AvailabilityAsync(await Actor(membership, ct), id, ct);
            return Conditional(context, TransportConventions.ETag(id, result.SourceRevision), result);
        }).WithName("GetLeftoverAvailability").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<LeftoversAvailability>().Produces(304);
        api.MapPut("/cooking-events/{id:guid}/actuals", async (Guid id, ActualsWrite request, HttpContext context, CurrentMembership membership, ConfirmConsumption confirmations, AllocationService allocations, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedSource(context, actor, id, allocations, ct);
            return Updated(context, await confirmations.ConfirmSourceAsync(actor, id, expected, request, ct));
        }).WithName("ConfirmCookingActuals").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>();
        api.MapDelete("/cooking-events/{id:guid}/actuals", async (Guid id, [FromBody] ClearActualsWrite? request, HttpContext context, CurrentMembership membership, ConfirmConsumption confirmations, AllocationService allocations, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedSource(context, actor, id, allocations, ct);
            return Updated(context, await confirmations.ClearSourceAsync(actor, id, expected, request?.DependentResolutions, ct));
        }).WithName("ClearCookingActuals").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>();
        api.MapPut("/meals/{id:guid}/consumption", async (Guid id, ConsumptionWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, ConfirmConsumption confirmations, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            return Updated(context, await confirmations.ConfirmAsync(actor, id, expected, request, ct));
        }).WithName("ConfirmLeftoverConsumption").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>();
        api.MapDelete("/meals/{id:guid}/consumption", async (Guid id, [FromBody] ClearConsumptionWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, ConfirmConsumption confirmations, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            return Updated(context, await confirmations.ClearAsync(actor, id, expected, request, ct));
        }).WithName("ClearLeftoverConsumption").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>();
        api.MapPost("/meals/{id:guid}/recipe-refresh", async (Guid id, RecipeRefreshWrite request, HttpContext context, CurrentMembership membership, MealPlanner planner, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedMeal(context, actor, id, planner, ct);
            if (request.Mode == RefreshMode.Preview)
            {
                var preview = await planner.PreviewRefreshAsync(actor, id, expected, request, ct);
                context.Response.Headers.ETag = context.Request.Headers.IfMatch;
                return Results.Ok(new RecipeRefreshResult(request.Mode, preview, null));
            }
            var meal = await planner.ApplyRefreshAsync(actor, id, expected, request, ct);
            context.Response.Headers.ETag = MealTag(meal); return Results.Ok(new RecipeRefreshResult(request.Mode, null, meal));
        }).WithName("RefreshCookingRecipe").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<RecipeRefreshResult>();
        api.MapPost("/cooking-events/{id:guid}/resolve", async (Guid id, SourceResolutionWrite request, HttpContext context, CurrentMembership membership, AllocationService allocations, ConfirmConsumption confirmations, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct); var expected = await ExpectedSource(context, actor, id, allocations, ct);
            var result = await confirmations.ResolveAsync(actor, id, expected, request, ct);
            return result is null ? Results.NoContent() : Updated(context, result);
        }).WithName("ResolveCookingConflicts").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ScheduledMealRead>().Produces(204);
    }
    private static async Task<RequestActor> Actor(CurrentMembership membership, CancellationToken ct)
        => await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
    private static IResult Conditional<T>(HttpContext context, string tag, T result)
    {
        context.Response.Headers.ETag = tag; context.Response.Headers.CacheControl = "private, no-cache";
        return context.Request.Headers.IfNoneMatch == tag ? Results.StatusCode(304) : Results.Ok(result);
    }
    private static string MealTag(ScheduledMealRead meal)
        => MealTag(meal.Id, new MealVersion(meal.Revision, meal.CookingEvent?.Revision ?? meal.Leftovers?.SourceRevision));
    private static string MealTag(Guid id, MealVersion version)
        => $"\"{id:N}:{version.Revision.ToString(CultureInfo.InvariantCulture)}:{version.SourceRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"}\"";
    private static IResult Created(HttpContext context, ScheduledMealRead meal)
    {
        context.Response.Headers.ETag = MealTag(meal); return Results.Created("/api/v1/meals/" + meal.Id, meal);
    }
    private static IResult Updated(HttpContext context, ScheduledMealRead meal)
    {
        context.Response.Headers.ETag = MealTag(meal); return Results.Ok(meal);
    }
    private static async Task<MealVersion> ExpectedMeal(HttpContext context, RequestActor actor, Guid id, MealPlanner planner, CancellationToken ct)
    {
        var supplied = context.Request.Headers.IfMatch.ToString(); var parts = supplied.Trim('"').Split(':');
        if (parts.Length == 3 && Guid.TryParseExact(parts[0], "N", out var resource) && resource == id
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            && (parts[2] == "-" || long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            var version = new MealVersion(revision, parts[2] == "-" ? null : long.Parse(parts[2], CultureInfo.InvariantCulture));
            if (supplied == MealTag(id, version)) return version;
        }
        _ = await planner.ReadAsync(actor, id, ct);
        throw new ApiContractException(string.IsNullOrWhiteSpace(supplied) ? 428 : 412, string.IsNullOrWhiteSpace(supplied) ? "precondition_required" : "stale_revision");
    }
    private static async Task<long> ExpectedSource(HttpContext context, RequestActor actor, Guid id, AllocationService allocations, CancellationToken ct)
        => await ExpectedSimple(context, id, async () => { _ = await allocations.AvailabilityAsync(actor, id, ct); });
    private static async Task<long> ExpectedSimple(HttpContext context, Guid id, Func<Task> scopedRead)
    {
        try { return TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id); }
        catch (ApiContractException) { await scopedRead(); throw; }
    }
    private static Guid? RetryKey(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var value)) return null;
        if (!Guid.TryParseExact(value, "D", out var key) || key == Guid.Empty) throw new ApiContractException(422, "invalid_idempotency_key");
        return key;
    }
}
