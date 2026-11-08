using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;

namespace RecipeManager.Infrastructure.MealPlanning;

public sealed partial class PlannerStore
{
    public Task<ScheduledMealRead> CreateCookingAsync(RequestActor actor, CookingWrite request, Guid? retryKey, CancellationToken ct)
        => Within(actor, cancellation => Retry(actor, "/api/v1/meals/cooking", retryKey, request, async token =>
        {
            _ = await Slot(request.SlotId, token);
            var recipe = await recipes.ReadWithinTransactionAsync(actor.HouseholdId, request.RecipeId, token);
            if (request.ExpectedRecipeRevision is { } expected) Match(recipe.Revision, expected);
            var meal = new ScheduledMeal(Guid.NewGuid(), actor.HouseholdId, request.Date, request.SlotId, MealType.Cook);
            var cooking = new CookingEvent(Guid.NewGuid(), actor.HouseholdId, meal.Id, recipe.Id, recipe.Revision,
                RecipeSnapshotFactory.Serialize(RecipeSnapshotFactory.Create(recipe)), request.PlannedPrepared, request.PlannedEaten);
            database.AddRange(meal, cooking); await Finish(actor, new Dictionary<Guid, LockedSource>(), token);
            return await ReadMeal(meal.Id, token);
        }, cancellation), ct);
    public Task<ScheduledMealRead> UpdateCookingAsync(RequestActor actor, Guid id, MealVersion expected, CookingWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); RequireType(meal, MealType.Cook);
            var source = await Cooking(id, cancellation); var sources = await LockSources(actor, [source.Id], cancellation);
            await CheckMeal(meal, expected, cancellation); _ = await Slot(request.SlotId, cancellation, meal.SlotId);
            if (request.RecipeId != source.RecipeId)
            {
                var recipe = await recipes.ReadWithinTransactionAsync(actor.HouseholdId, request.RecipeId, cancellation);
                if (request.ExpectedRecipeRevision is { } revision) Match(recipe.Revision, revision);
                source.ReplaceRecipe(recipe.Id, recipe.Revision, RecipeSnapshotFactory.Serialize(RecipeSnapshotFactory.Create(recipe)));
            }
            // Editing portions or position with the same recipe never refreshes its existing snapshot.
            source.ChangePlan(request.PlannedPrepared, request.PlannedEaten); meal.Move(request.Date, request.SlotId);
            await Finish(actor, sources, cancellation); return await ReadMeal(id, cancellation);
        }, ct);
    public Task<ScheduledMealRead> CreateOtherAsync(RequestActor actor, OtherMealWrite request, Guid? retryKey, CancellationToken ct)
        => Within(actor, cancellation => Retry(actor, "/api/v1/meals/other", retryKey, request, async token =>
        {
            _ = await Slot(request.SlotId, token);
            var meal = new ScheduledMeal(Guid.NewGuid(), actor.HouseholdId, request.Date, request.SlotId, MealType.Other, request.Label);
            database.Add(meal); await Finish(actor, new Dictionary<Guid, LockedSource>(), token); return await ReadMeal(meal.Id, token);
        }, cancellation), ct);
    public Task<ScheduledMealRead> UpdateOtherAsync(RequestActor actor, Guid id, MealVersion expected, OtherMealWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); RequireType(meal, MealType.Other); await CheckMeal(meal, expected, cancellation);
            _ = await Slot(request.SlotId, cancellation, meal.SlotId); meal.Relabel(request.Label); meal.Move(request.Date, request.SlotId);
            await Finish(actor, new Dictionary<Guid, LockedSource>(), cancellation); return await ReadMeal(id, cancellation);
        }, ct);
    public Task<ScheduledMealRead> MoveAsync(RequestActor actor, Guid id, MealVersion expected, MealPositionWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); var sourceIds = await SourceIds(meal, cancellation);
            var sources = await LockSources(actor, sourceIds, cancellation); var source = await CheckMeal(meal, expected, cancellation);
            if (source is not null && request.SourceRevision is { } revision) Match(source.Revision, revision);
            _ = await Slot(request.SlotId, cancellation, meal.SlotId); meal.Move(request.Date, request.SlotId);
            await Finish(actor, sources, cancellation); return await ReadMeal(id, cancellation);
        }, ct);
    public Task<ScheduledMealRead> DuplicateAsync(RequestActor actor, Guid id, MealVersion expected, DuplicateMealWrite request, Guid? retryKey, CancellationToken ct)
        => Within(actor, cancellation => Retry(actor, $"/api/v1/meals/{id}/duplicate", retryKey, new { expected, request }, async token =>
        {
            var original = await Meal(id, token); var sourceIds = await SourceIds(original, token);
            var sources = await LockSources(actor, sourceIds, token); var source = await CheckMeal(original, expected, token);
            if (source is not null && request.SourceRevision is { } revision) Match(source.Revision, revision);
            _ = await Slot(request.SlotId, token);
            var copy = new ScheduledMeal(Guid.NewGuid(), actor.HouseholdId, request.Date, request.SlotId, original.Type, original.Label);
            database.Add(copy);
            if (original.Type == MealType.Cook)
            {
                var basis = source!;
                database.Add(new CookingEvent(Guid.NewGuid(), actor.HouseholdId, copy.Id, basis.RecipeId, basis.RecipeRevision, basis.RecipeSnapshotJson, basis.PlannedPrepared, basis.PlannedEaten));
                sources.Clear(); // The original cooking event was read, not modified.
            }
            else if (original.Type == MealType.Leftovers)
            {
                var allocation = await AllocationFor(id, token);
                database.Add(new Allocation(Guid.NewGuid(), actor.HouseholdId, copy.Id, allocation.SourceCookingEventId, allocation.PlannedPortions));
            }
            await Finish(actor, sources, token); return await ReadMeal(copy.Id, token);
        }, cancellation), ct);
    public Task DeleteAsync(RequestActor actor, Guid id, MealVersion expected, DeleteMealWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); var sources = await LockSources(actor, await SourceIds(meal, cancellation), cancellation);
            var source = await CheckMeal(meal, expected, cancellation);
            if (source is not null && request.SourceRevision is { } revision) Match(source.Revision, revision);
            await DeleteWithin(meal, request.ConfirmConsumedRemoval, cancellation);
            await Finish(actor, sources, cancellation); return true;
        }, ct);
    private async Task DeleteWithin(ScheduledMeal meal, bool confirmConsumedRemoval, CancellationToken ct)
    {
        if (meal.Type == MealType.Cook)
        {
            var source = await Cooking(meal.Id, ct); var allocations = await EffectiveAllocations(source.Id, ct);
            if (allocations.Count != 0) throw new PlannerConflictException("dependent_leftovers", "Resolve dependent leftover meals before deleting their cooking event.", allocations.Select(x => x.ScheduledMealId).ToArray());
            database.CookingEvents.Remove(source);
        }
        else if (meal.Type == MealType.Leftovers)
        {
            var allocation = await AllocationFor(meal.Id, ct);
            if (allocation.ConsumedPortions is not null && !confirmConsumedRemoval)
                throw new PlannerConflictException("consumption_removal_confirmation", "Explicitly confirm removing this meal's recorded consumption.", [meal.Id]);
            database.Allocations.Remove(allocation);
        }
        database.ScheduledMeals.Remove(meal);
    }
    private async Task<Guid[]> SourceIds(ScheduledMeal meal, CancellationToken ct) => meal.Type switch
    {
        MealType.Cook => [(await Cooking(meal.Id, ct)).Id],
        MealType.Leftovers => [(await AllocationFor(meal.Id, ct)).SourceCookingEventId],
        _ => []
    };
    private static void RequireType(ScheduledMeal meal, MealType type)
    {
        if (meal.Type != type) throw new PlannerConflictException("different_meal_type", "Choose the editor for this meal's existing type.", [meal.Id]);
    }
}
