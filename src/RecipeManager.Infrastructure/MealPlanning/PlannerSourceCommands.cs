using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Infrastructure.MealPlanning;

public sealed partial class PlannerStore
{
    public Task<ScheduledMealRead> ConfirmActualsAsync(RequestActor actor, Guid id, long expected, ActualsWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var sources = await LockSources(actor, [id], cancellation); var source = sources[id].Source; Match(source.Revision, expected);
            source.SetActuals(request.ActualPrepared, request.ActualEaten, clock.GetUtcNow());
            await ApplyDependents(source, request.DependentResolutions ?? [], sources, cancellation);
            var meal = await Meal(source.ScheduledMealId, cancellation); meal.Advance();
            await Finish(actor, sources, cancellation); return await ReadMeal(meal.Id, cancellation);
        }, ct);
    public Task<ScheduledMealRead> ClearActualsAsync(RequestActor actor, Guid id, long expected, IReadOnlyList<DependentResolution>? resolutions, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var sources = await LockSources(actor, [id], cancellation); var source = sources[id].Source; Match(source.Revision, expected);
            source.SetActuals(null, null, null); await ApplyDependents(source, resolutions ?? [], sources, cancellation);
            var meal = await Meal(source.ScheduledMealId, cancellation); meal.Advance();
            await Finish(actor, sources, cancellation); return await ReadMeal(meal.Id, cancellation);
        }, ct);
    public Task<SnapshotRefreshPreview> PreviewRefreshAsync(RequestActor actor, Guid id, MealVersion expected, RecipeRefreshWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); RequireType(meal, MealType.Cook);
            var cooking = await Cooking(id, cancellation); _ = await LockSources(actor, [cooking.Id], cancellation);
            await CheckMeal(meal, expected, cancellation);
            if (request.ExpectedEventRevision is { } sourceRevision) Match(cooking.Revision, sourceRevision);
            var latest = await recipes.ReadWithinTransactionAsync(actor.HouseholdId, cooking.RecipeId, cancellation);
            Match(latest.Revision, request.ExpectedRecipeRevision); return RefreshRecipeSnapshot.Preview(cooking, latest);
        }, ct);
    public Task<ScheduledMealRead> ApplyRefreshAsync(RequestActor actor, Guid id, MealVersion expected, RecipeRefreshWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var meal = await Meal(id, cancellation); RequireType(meal, MealType.Cook);
            var cooking = await Cooking(id, cancellation); var sources = await LockSources(actor, [cooking.Id], cancellation);
            await CheckMeal(meal, expected, cancellation);
            if (request.ExpectedEventRevision is not { } eventRevision)
                throw new DomainValidationException("expectedEventRevision", "required", "Apply requires the event revision returned by the refresh preview.");
            var latest = await recipes.ReadWithinTransactionAsync(actor.HouseholdId, cooking.RecipeId, cancellation);
            RefreshRecipeSnapshot.Apply(cooking, latest, eventRevision, request.ExpectedRecipeRevision); meal.Advance();
            await Finish(actor, sources, cancellation); return await ReadMeal(id, cancellation);
        }, ct);
    public Task<ScheduledMealRead?> ResolveAsync(RequestActor actor, Guid id, long expected, SourceResolutionWrite request, CancellationToken ct)
        => Within<ScheduledMealRead?>(actor, async cancellation =>
        {
            var targetIds = request.Dependents.Where(x => x.SourceCookingEventId != null).Select(x => x.SourceCookingEventId!.Value).Append(id).Distinct().ToArray();
            var sources = await LockSources(actor, targetIds, cancellation); var source = sources[id].Source; Match(source.Revision, expected);
            var versions = request.ExpectedSources.ToDictionary(x => x.SourceCookingEventId, x => x.Revision);
            foreach (var entry in sources)
            {
                if (!versions.TryGetValue(entry.Key, out var revision))
                    throw new DomainValidationException("expectedSources", "required", "Provide the current revision of every affected cooking source.");
                Match(entry.Value.Source.Revision, revision);
            }
            if (versions.Count != sources.Count) throw new DomainValidationException("expectedSources", "unrelated_source", "Only provide revisions for affected cooking sources.");
            var meal = await Meal(source.ScheduledMealId, cancellation);
            switch (request.Kind)
            {
                case SourceResolutionKind.Plan:
                    RequireOnlyFields(request, plan: true, position: true, recipe: true);
                    if (request.ExpectedRecipeRevision is not null && request.RecipeId is null)
                        throw new DomainValidationException("recipeId", "required", "A recipe revision requires an explicit recipe selection.");
                    if (request.RecipeId is { } selectedRecipe && selectedRecipe != source.RecipeId)
                    {
                        var latest = await recipes.ReadWithinTransactionAsync(actor.HouseholdId, selectedRecipe, cancellation);
                        if (request.ExpectedRecipeRevision is { } recipeVersion) Match(latest.Revision, recipeVersion);
                        source.ReplaceRecipe(latest.Id, latest.Revision, RecipeSnapshotFactory.Serialize(RecipeSnapshotFactory.Create(latest)));
                    }
                    source.ChangePlan(Need(request.PlannedPrepared, "plannedPrepared"), Need(request.PlannedEaten, "plannedEaten"));
                    if (request.Date is not null || request.SlotId is not null)
                    {
                        var plannedSlot = Need(request.SlotId, "slotId"); _ = await Slot(plannedSlot, cancellation, meal.SlotId);
                        meal.Move(Need(request.Date, "date"), plannedSlot);
                    }
                    else meal.Advance();
                    break;
                case SourceResolutionKind.Actuals:
                    RequireOnlyFields(request, actuals: true);
                    source.SetActuals(Need(request.ActualPrepared, "actualPrepared"), Need(request.ActualEaten, "actualEaten"), clock.GetUtcNow()); meal.Advance(); break;
                case SourceResolutionKind.ClearActuals:
                    RequireOnlyFields(request); source.SetActuals(null, null, null); meal.Advance(); break;
                case SourceResolutionKind.Move:
                    RequireOnlyFields(request, position: true);
                    var slotId = Need(request.SlotId, "slotId"); _ = await Slot(slotId, cancellation, meal.SlotId);
                    meal.Move(Need(request.Date, "date"), slotId); break;
                case SourceResolutionKind.Delete: RequireOnlyFields(request); break;
                default: throw new DomainValidationException("kind", "unsupported", "Choose a supported source resolution action.");
            }
            await ApplyDependents(source, request.Dependents, sources, cancellation);
            if (request.Kind == SourceResolutionKind.Delete) await DeleteWithin(meal, false, cancellation);
            await Finish(actor, sources, cancellation);
            return request.Kind == SourceResolutionKind.Delete ? null : await ReadMeal(meal.Id, cancellation);
        }, ct);
    private async Task ApplyDependents(CookingEvent source, IReadOnlyList<DependentResolution> changes, IReadOnlyDictionary<Guid, LockedSource> sources, CancellationToken ct)
    {
        if (changes.Count > 1000 || changes.Any(x => x is null) || changes.Select(x => x.MealId).Distinct().Count() != changes.Count)
            throw new DomainValidationException("dependentResolutions", "invalid_resolution", "Provide at most 1000 unique explicit dependent changes.");
        foreach (var change in changes)
        {
            ValidateDependentFields(change);
            var meal = await Meal(change.MealId, ct); RequireType(meal, MealType.Leftovers); Match(meal.Revision, change.MealRevision);
            var allocation = await AllocationFor(meal.Id, ct);
            if (allocation.SourceCookingEventId != source.Id)
                throw new DomainValidationException("dependentResolutions", "not_dependent", "Each affected meal must reference this cooking source.");
            switch (change.Action)
            {
                case DependentAction.Delete: await DeleteWithin(meal, change.ConfirmConsumedRemoval, ct); continue;
                case DependentAction.Release:
                    var released = change.ReservedRemainder ?? 0m;
                    if (released > allocation.ReservedRemainder) throw new DomainValidationException("reservedRemainder", "not_a_release", "Releasing a reservation cannot increase it.");
                    allocation.ChangePlan(allocation.PlannedPortions, released); break;
                case DependentAction.Edit: AmendAllocation(allocation, change); break;
                case DependentAction.Move:
                    var slot = Need(change.SlotId, "slotId"); _ = await Slot(slot, ct, meal.SlotId);
                    meal.Move(Need(change.Date, "date"), slot); break;
                case DependentAction.Reassign:
                    var target = Need(change.SourceCookingEventId, "sourceCookingEventId");
                    if (!sources.ContainsKey(target)) throw new DomainValidationException("expectedSources", "required", "Use the atomic resolution endpoint with revisions for both cooking sources when reassigning a meal.");
                    AmendAllocation(allocation, change); allocation.ChangeSource(target);
                    if (change.Date is not null || change.SlotId is not null)
                    {
                        var targetSlot = Need(change.SlotId, "slotId"); _ = await Slot(targetSlot, ct, meal.SlotId);
                        meal.Move(Need(change.Date, "date"), targetSlot);
                    }
                    break;
                default: throw new DomainValidationException("action", "unsupported", "Choose a supported dependent resolution action.");
            }
            if (change.ConsumedPortions is not null && !sources[allocation.SourceCookingEventId].Source.Portions.IsActual)
                throw new PlannerConflictException("source_not_confirmed", "Confirm the selected cooking source before recording consumption.", [meal.Id]);
            meal.Advance();
        }
    }
    private static void ValidateDependentFields(DependentResolution change)
    {
        var quantities = change.PlannedPortions is not null || change.ConsumedPortions is not null || change.ClearConsumption;
        var position = change.Date is not null || change.SlotId is not null;
        var source = change.SourceCookingEventId is not null;
        var invalid = change.Action switch
        {
            DependentAction.Delete => quantities || position || source || change.ReservedRemainder is not null,
            DependentAction.Release => quantities || position || source || change.ConfirmConsumedRemoval,
            DependentAction.Move => quantities || source || change.ReservedRemainder is not null || change.ConfirmConsumedRemoval,
            DependentAction.Edit => position || source || change.ConfirmConsumedRemoval,
            DependentAction.Reassign => change.ConfirmConsumedRemoval,
            _ => true
        };
        if (invalid) throw new DomainValidationException("dependentResolutions", "conflicting_values", "Only supply fields for the chosen dependent action.");
    }
    private void AmendAllocation(Allocation allocation, DependentResolution change)
    {
        var planned = change.PlannedPortions ?? allocation.PlannedPortions;
        if (change.ClearConsumption && change.ConsumedPortions is not null)
            throw new DomainValidationException("consumedPortions", "conflicting_values", "Choose either replacement consumption or explicit clearing.");
        if (change.ClearConsumption && change.ReservedRemainder is null)
            throw new DomainValidationException("reservedRemainder", "required", "Clearing consumption requires an explicit remaining reservation.");
        var reserved = allocation.ReservationForPlan(planned, change.ReservedRemainder);
        allocation.ChangePlan(planned, reserved);
        if (change.ClearConsumption) allocation.SetConsumption(null, reserved, null);
        else if (change.ConsumedPortions is { } consumed) allocation.SetConsumption(consumed, reserved, clock.GetUtcNow());
    }
    private static T Need<T>(T? value, string field) where T : struct
        => value ?? throw new DomainValidationException(field, "required", "Provide an explicit value for this action.");
    private static void RequireOnlyFields(SourceResolutionWrite request, bool plan = false, bool actuals = false, bool position = false, bool recipe = false)
    {
        if ((!plan && (request.PlannedPrepared is not null || request.PlannedEaten is not null))
            || (!actuals && (request.ActualPrepared is not null || request.ActualEaten is not null))
            || (!position && (request.Date is not null || request.SlotId is not null))
            || (!recipe && (request.RecipeId is not null || request.ExpectedRecipeRevision is not null)))
            throw new DomainValidationException("resolution", "conflicting_values", "Only supply source fields for the chosen action.");
    }
}
