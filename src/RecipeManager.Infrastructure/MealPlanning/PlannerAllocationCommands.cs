using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.Leftovers.Contracts;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Infrastructure.MealPlanning;

public sealed partial class PlannerStore
{
    public Task<ScheduledMealRead> CreateLeftoversAsync(RequestActor actor, LeftoverWrite request, Guid? retryKey, CancellationToken ct)
        => Within(actor, cancellation => Retry(actor, "/api/v1/meals/leftovers", retryKey, request, async token =>
        {
            var sources = await LockSources(actor, [request.SourceCookingEventId], token);
            Match(sources[request.SourceCookingEventId].Source.Revision, request.SourceRevision);
            if (request.ReservedRemainder is { } reserved && reserved != request.PlannedPortions)
                throw new DomainValidationException("reservedRemainder", "initial_reservation", "A new leftover meal initially reserves its planned portions. Release portions explicitly afterward.");
            _ = await Slot(request.SlotId, token);
            var meal = new ScheduledMeal(Guid.NewGuid(), actor.HouseholdId, request.Date, request.SlotId, MealType.Leftovers);
            database.AddRange(meal, new Allocation(Guid.NewGuid(), actor.HouseholdId, meal.Id, request.SourceCookingEventId, request.PlannedPortions));
            await Finish(actor, sources, token); return await ReadMeal(meal.Id, token);
        }, cancellation), ct);
    public Task<ScheduledMealRead> UpdateLeftoversAsync(RequestActor actor, Guid id, MealVersion expected, LeftoverWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); RequireType(meal, MealType.Leftovers);
            var allocation = await AllocationFor(id, cancellation); var previous = allocation.SourceCookingEventId;
            var sources = await LockSources(actor, [previous, request.SourceCookingEventId], cancellation);
            await CheckMeal(meal, expected, cancellation);
            Match(sources[request.SourceCookingEventId].Source.Revision, request.SourceRevision);
            if (previous != request.SourceCookingEventId)
            {
                if (request.PreviousSourceRevision is not { } oldRevision)
                    throw new DomainValidationException("previousSourceRevision", "required", "Reassignment requires the current revision of both cooking sources.");
                Match(sources[previous].Source.Revision, oldRevision);
            }
            _ = await Slot(request.SlotId, cancellation, meal.SlotId);
            var remainder = allocation.ReservationForPlan(request.PlannedPortions, request.ReservedRemainder);
            allocation.ChangePlan(request.PlannedPortions, remainder); allocation.ChangeSource(request.SourceCookingEventId);
            meal.Move(request.Date, request.SlotId);
            await Finish(actor, sources, cancellation); return await ReadMeal(id, cancellation);
        }, ct);
    public Task<ScheduledMealRead> ConsumeAsync(RequestActor actor, Guid id, MealVersion expected, ConsumptionWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); RequireType(meal, MealType.Leftovers);
            var allocation = await AllocationFor(id, cancellation); var sources = await LockSources(actor, [allocation.SourceCookingEventId], cancellation);
            var source = sources[allocation.SourceCookingEventId].Source;
            await CheckMeal(meal, expected, cancellation); Match(source.Revision, request.SourceRevision);
            if (request.SourceActuals is { } actuals) source.SetActuals(actuals.ActualPrepared, actuals.ActualEaten, clock.GetUtcNow());
            if (!source.Portions.IsActual) throw new PlannerConflictException("source_not_confirmed", "Confirm actual portions prepared and eaten before recording leftover consumption, or confirm both explicitly in this request.", [meal.Id]);
            allocation.SetConsumption(request.ConsumedPortions, request.ReservedRemainder, clock.GetUtcNow()); meal.Advance();
            await Finish(actor, sources, cancellation); return await ReadMeal(id, cancellation);
        }, ct);
    public Task<ScheduledMealRead> ClearConsumptionAsync(RequestActor actor, Guid id, MealVersion expected, ClearConsumptionWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); RequireType(meal, MealType.Leftovers);
            var allocation = await AllocationFor(id, cancellation); var sources = await LockSources(actor, [allocation.SourceCookingEventId], cancellation);
            await CheckMeal(meal, expected, cancellation); Match(sources[allocation.SourceCookingEventId].Source.Revision, request.SourceRevision);
            allocation.SetConsumption(null, request.ReservedRemainder, null); meal.Advance();
            await Finish(actor, sources, cancellation); return await ReadMeal(id, cancellation);
        }, ct);
}
