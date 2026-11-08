using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.Leftovers.Contracts;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.MealPlanning;

public sealed partial class PlannerStore(RecipeManagerDbContext database, TransactionBoundary transactions, RevisionCoordinator revisions,
    CreationRetryStore retries, IRecipeSnapshotSource recipes, TimeProvider clock) : IPlannerStore
{
    private sealed record LockedSource(CookingEvent Source, long OriginalRevision);
    private Task<T> Within<T>(RequestActor actor, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => transactions.ExecuteAsync(actor, false, [], action, ct);
    private Task<ScheduledMeal> Meal(Guid id, CancellationToken ct)
        => LocalOrRequired(database.ScheduledMeals.Local.FirstOrDefault(x => x.HouseholdId == database.CurrentHouseholdId && x.Id == id), () => database.ScheduledMeals.SingleOrDefaultAsync(x => x.Id == id, ct));
    private Task<CookingEvent> Source(Guid id, CancellationToken ct)
        => LocalOrRequired(database.CookingEvents.Local.FirstOrDefault(x => x.HouseholdId == database.CurrentHouseholdId && x.Id == id), () => database.CookingEvents.SingleOrDefaultAsync(x => x.Id == id, ct));
    private Task<CookingEvent> Cooking(Guid mealId, CancellationToken ct)
        => LocalOrRequired(database.CookingEvents.Local.FirstOrDefault(x => x.HouseholdId == database.CurrentHouseholdId && x.ScheduledMealId == mealId), () => database.CookingEvents.SingleOrDefaultAsync(x => x.ScheduledMealId == mealId, ct));
    private Task<Allocation> AllocationFor(Guid mealId, CancellationToken ct)
        => LocalOrRequired(database.Allocations.Local.FirstOrDefault(x => x.HouseholdId == database.CurrentHouseholdId && x.ScheduledMealId == mealId), () => database.Allocations.SingleOrDefaultAsync(x => x.ScheduledMealId == mealId, ct));
    private static Task<T> LocalOrRequired<T>(T? local, Func<Task<T?>> query) where T : class => local is null ? Required(query()) : Task.FromResult(local);
    private static async Task<T> Required<T>(Task<T?> query) where T : class
        => await query ?? throw new ResourceMissingException();
    private static void Match(long actual, long expected) { if (actual != expected) throw new RevisionConflictException(); }
    private async Task<CookingEvent?> CheckMeal(ScheduledMeal meal, MealVersion expected, CancellationToken ct)
    {
        Match(meal.Revision, expected.Revision);
        CookingEvent? source = meal.Type switch
        {
            MealType.Cook => await Cooking(meal.Id, ct),
            MealType.Leftovers => await Source((await AllocationFor(meal.Id, ct)).SourceCookingEventId, ct),
            _ => null
        };
        if (source?.Revision != expected.SourceRevision) throw new RevisionConflictException();
        return source;
    }
    private async Task<MealSlot> Slot(Guid id, CancellationToken ct, Guid? existingSlot = null)
    {
        var slot = await Required(database.MealSlots.SingleOrDefaultAsync(x => x.Id == id, ct));
        if (slot.Archived && existingSlot != id) throw new PlannerConflictException("slot_archived", "Choose an active meal slot.");
        return slot;
    }
    private async Task<Dictionary<Guid, LockedSource>> LockSources(RequestActor actor, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var locked = new Dictionary<Guid, LockedSource>();
        foreach (var id in ids.Distinct().Order())
        {
            var rows = await database.CookingEvents.FromSqlInterpolated(
                $"SELECT * FROM planning.\"CookingEvents\" WHERE \"HouseholdId\" = {actor.HouseholdId} AND \"Id\" = {id} FOR UPDATE")
                .AsNoTracking().ToArrayAsync(ct);
            if (rows.Length != 1) throw new ResourceMissingException();
            var source = await Source(id, ct); locked.Add(id, new(source, source.Revision));
        }
        return locked;
    }
    private async Task<IReadOnlyList<Allocation>> EffectiveAllocations(Guid sourceId, CancellationToken ct)
    {
        var persisted = await database.Allocations.Where(x => x.SourceCookingEventId == sourceId).ToListAsync(ct);
        return persisted.Concat(database.ChangeTracker.Entries<Allocation>().Select(x => x.Entity))
            .Where(x => x.HouseholdId == database.CurrentHouseholdId && x.SourceCookingEventId == sourceId
                && database.Entry(x).State != EntityState.Deleted).DistinctBy(x => x.Id).ToArray();
    }
    private async Task ValidateSource(CookingEvent source, CancellationToken ct)
    {
        var allocations = await EffectiveAllocations(source.Id, ct);
        try { _ = AllocationBalance.Available(source.Portions.Capacity, allocations); }
        catch (AllocationConflictException error) { throw new PlannerConflictException("leftovers_overallocated", error.Message, allocations.Select(x => x.ScheduledMealId).ToArray()); }
        if (!source.Portions.IsActual && allocations.Any(x => x.ConsumedPortions > 0))
            throw new PlannerConflictException("source_not_confirmed", "Confirm source production before recording leftover consumption, or explicitly clear dependent consumption.", allocations.Where(x => x.ConsumedPortions > 0).Select(x => x.ScheduledMealId).ToArray());
        var sourceMeal = await Meal(source.ScheduledMealId, ct);
        var sourceSlot = await Required(database.MealSlots.SingleOrDefaultAsync(x => x.Id == sourceMeal.SlotId, ct));
        foreach (var allocation in allocations)
        {
            var meal = await Meal(allocation.ScheduledMealId, ct);
            var slot = await Required(database.MealSlots.SingleOrDefaultAsync(x => x.Id == meal.SlotId, ct));
            if (!MealChronology.IsLater(sourceMeal.Date, sourceSlot.Position, meal.Date, slot.Position))
                throw new PlannerConflictException("invalid_leftover_chronology", "Leftovers must be scheduled after their original cooking meal.", [meal.Id]);
        }
    }
    private async Task Finish(RequestActor actor, IReadOnlyDictionary<Guid, LockedSource> sources, CancellationToken ct)
    {
        foreach (var entry in sources.Values)
        {
            if (database.Entry(entry.Source).State == EntityState.Deleted) continue;
            await ValidateSource(entry.Source, ct);
            if (entry.Source.Revision == entry.OriginalRevision) entry.Source.Advance();
        }
        await revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Planner, true, ct);
        await database.SaveChangesAsync(ct);
    }
    private static LeftoverRead AllocationRead(Allocation allocation, CookingEvent source, string title, DateOnly date)
        => new(allocation.Id, source.Id, source.Revision, title, date, allocation.PlannedPortions, allocation.ConsumedPortions,
            allocation.ReservedRemainder, allocation.Commitment, allocation.ConfirmedAt);
    private async Task<IReadOnlyList<ScheduledMealRead>> ReadMeals(IReadOnlyList<ScheduledMeal> meals, CancellationToken ct)
    {
        if (meals.Count == 0) return [];
        var ids = meals.Select(x => x.Id).ToArray();
        var allocations = await database.Allocations.AsNoTracking().Where(x => ids.Contains(x.ScheduledMealId)).ToArrayAsync(ct);
        var referenced = allocations.Select(x => x.SourceCookingEventId).Distinct().ToArray();
        var sources = await database.CookingEvents.AsNoTracking().Where(x => ids.Contains(x.ScheduledMealId) || referenced.Contains(x.Id)).ToArrayAsync(ct);
        var sourceIds = sources.Select(x => x.Id).ToArray();
        var allAllocations = await database.Allocations.AsNoTracking().Where(x => sourceIds.Contains(x.SourceCookingEventId)).ToArrayAsync(ct);
        var snapshots = sources.ToDictionary(x => x.Id, x => RecipeSnapshotFactory.Deserialize(x.RecipeSnapshotJson));
        var sourceMealIds = sources.Select(x => x.ScheduledMealId).ToArray();
        var sourceDates = await database.ScheduledMeals.AsNoTracking().Where(x => sourceMealIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Date, ct);
        var byId = sources.ToDictionary(x => x.Id);
        var byMeal = sources.ToDictionary(x => x.ScheduledMealId);
        var allocationByMeal = allocations.ToDictionary(x => x.ScheduledMealId);
        return meals.Select(meal =>
        {
            CookingEventRead? cooking = null; LeftoverRead? leftovers = null;
            if (meal.Type == MealType.Cook)
            {
                var source = byMeal[meal.Id];
                cooking = new(source.Id, source.Revision, source.RecipeId, source.RecipeRevision, snapshots[source.Id],
                    source.PlannedPrepared, source.PlannedEaten, source.ActualPrepared, source.ActualEaten, source.ConfirmedAt,
                    source.Portions.IsActual ? "actual" : "planned", AllocationBalance.Available(source.Portions.Capacity, allAllocations.Where(x => x.SourceCookingEventId == source.Id)));
            }
            else if (meal.Type == MealType.Leftovers)
            {
                var allocation = allocationByMeal[meal.Id]; var source = byId[allocation.SourceCookingEventId];
                leftovers = AllocationRead(allocation, source, snapshots[source.Id].Title, sourceDates[source.ScheduledMealId]);
            }
            return new ScheduledMealRead(meal.Id, meal.Revision, meal.Date, meal.SlotId, meal.Type, meal.Label, cooking, leftovers);
        }).ToArray();
    }
    private async Task<ScheduledMealRead> ReadMeal(Guid id, CancellationToken ct) => (await ReadMeals([await Meal(id, ct)], ct)).Single();
    private static MealSlotRead SlotRead(MealSlot slot) => new(slot.Id, slot.Name, slot.Position, slot.IsDefault, slot.Archived, slot.Revision);
    private async Task<T> Retry<T>(RequestActor actor, string route, Guid? key, object payload, Func<CancellationToken, Task<T>> create, CancellationToken ct)
    {
        if (key is null) return await create(ct);
        var outcome = await retries.ExecuteAsync(actor.HouseholdId, actor.UserId, route, key.Value, JsonSerializer.SerializeToUtf8Bytes(payload), async cancellation =>
            new CreationOutcome(201, JsonSerializer.Serialize(await create(cancellation)), null, null), ct);
        return JsonSerializer.Deserialize<T>(outcome.Body) ?? throw new InvalidOperationException("Planner creation retry result is missing.");
    }
    public Task<MealSlotPage> SlotsAsync(RequestActor actor, CancellationToken ct) => Within(actor, async cancellation =>
        new MealSlotPage((await database.MealSlots.AsNoTracking().OrderBy(x => x.Position).ThenBy(x => x.Id).ToArrayAsync(cancellation)).Select(SlotRead).ToArray(),
            (await database.Households.SingleAsync(x => x.Id == actor.HouseholdId, cancellation)).PlannerRevision), ct);
    public Task<MealCalendarRead> CalendarAsync(RequestActor actor, DateOnly from, DateOnly to, CancellationToken ct) => Within(actor, async cancellation =>
    {
        var slots = await database.MealSlots.AsNoTracking().OrderBy(x => x.Position).ThenBy(x => x.Id).ToArrayAsync(cancellation);
        var positions = slots.ToDictionary(x => x.Id, x => x.Position);
        var meals = await database.ScheduledMeals.AsNoTracking().Where(x => x.Date >= from && x.Date <= to).ToArrayAsync(cancellation);
        return new MealCalendarRead(from, to, await ReadMeals(meals.OrderBy(x => x.Date).ThenBy(x => positions[x.SlotId]).ThenBy(x => x.Id).ToArray(), cancellation),
            slots.Select(SlotRead).ToArray(), (await database.Households.SingleAsync(x => x.Id == actor.HouseholdId, cancellation)).PlannerRevision);
    }, ct);
    public Task<ScheduledMealRead> ReadAsync(RequestActor actor, Guid id, CancellationToken ct) => Within(actor, cancellation => ReadMeal(id, cancellation), ct);
    public Task<LeftoversAvailability> AvailabilityAsync(RequestActor actor, Guid id, CancellationToken ct) => Within(actor, async cancellation =>
    {
        var source = await Source(id, cancellation); var meal = await Meal(source.ScheduledMealId, cancellation);
        var allocations = await database.Allocations.AsNoTracking().Where(x => x.SourceCookingEventId == id).ToArrayAsync(cancellation);
        var mealIds = allocations.Select(x => x.ScheduledMealId).ToArray();
        var dependents = await database.ScheduledMeals.AsNoTracking().Where(x => mealIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellation);
        return new LeftoversAvailability(id, source.Revision, meal.Id, meal.Date, meal.SlotId, RecipeSnapshotFactory.Deserialize(source.RecipeSnapshotJson).Title,
            source.RecipeId, source.RecipeRevision, source.PlannedPrepared, source.PlannedEaten, source.ActualPrepared, source.ActualEaten, source.ConfirmedAt,
            source.Portions.IsActual ? "actual" : "planned", source.Portions.Capacity, AllocationBalance.Available(source.Portions.Capacity, allocations),
            allocations.Select(x => new AllocationReservationRead(x.ScheduledMealId, dependents[x.ScheduledMealId].Revision, dependents[x.ScheduledMealId].Date,
                dependents[x.ScheduledMealId].SlotId, AllocationRead(x, source, RecipeSnapshotFactory.Deserialize(source.RecipeSnapshotJson).Title, meal.Date))).OrderBy(x => x.Date).ThenBy(x => x.MealId).ToArray());
    }, ct);
}
