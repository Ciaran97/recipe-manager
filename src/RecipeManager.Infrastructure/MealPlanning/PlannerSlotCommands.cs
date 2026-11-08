using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;

namespace RecipeManager.Infrastructure.MealPlanning;

public sealed partial class PlannerStore
{
    public Task<MealSlotRead> SlotAsync(RequestActor actor, Guid id, CancellationToken ct) => Within(actor, async cancellation =>
        SlotRead(await Slot(id, cancellation, id)), ct);
    public Task<MealSlotRead> CreateSlotAsync(RequestActor actor, MealSlotWrite request, Guid? retryKey, CancellationToken ct)
        => Within(actor, cancellation => Retry(actor, "/api/v1/meal-slots", retryKey, request, async token =>
        {
            var slots = await database.MealSlots.OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync(token);
            var created = new MealSlot(Guid.NewGuid(), actor.HouseholdId, request.Name, request.Position);
            slots.Insert(Math.Min(request.Position, slots.Count), created); database.Add(created);
            ReindexSlots(slots); await Finish(actor, new Dictionary<Guid, LockedSource>(), token); return SlotRead(created);
        }, cancellation), ct);
    public Task<MealSlotRead> UpdateSlotAsync(RequestActor actor, Guid id, long expected, MealSlotWrite request, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var slot = await Slot(id, cancellation, id); Match(slot.Revision, expected);
            var slots = await database.MealSlots.OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync(cancellation);
            var sourceIds = await database.CookingEvents.Select(x => x.Id).ToArrayAsync(cancellation);
            var sources = await LockSources(actor, sourceIds, cancellation);
            slots.Remove(slot); slots.Insert(Math.Min(request.Position, slots.Count), slot);
            slot.Rename(request.Name, slots.IndexOf(slot)); ReindexSlots(slots);
            // Position edits cannot invalidate existing same-day source/allocation chronology.
            foreach (var source in sources.Values) await ValidateSource(source.Source, cancellation);
            await Finish(actor, new Dictionary<Guid, LockedSource>(), cancellation); return SlotRead(slot);
        }, ct);
    public Task DeleteSlotAsync(RequestActor actor, Guid id, long expected, CancellationToken ct)
        => Within(actor, async cancellation =>
        {
            var slot = await Slot(id, cancellation, id); Match(slot.Revision, expected);
            if (await database.ScheduledMeals.AnyAsync(x => x.SlotId == id, cancellation)) slot.Archive();
            else
            {
                database.MealSlots.Remove(slot);
                var remaining = await database.MealSlots.Where(x => x.Id != id).OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync(cancellation);
                ReindexSlots(remaining);
            }
            await Finish(actor, new Dictionary<Guid, LockedSource>(), cancellation); return true;
        }, ct);
    private static void ReindexSlots(IReadOnlyList<MealSlot> slots)
    {
        for (var index = 0; index < slots.Count; index++)
            if (slots[index].Position != index) slots[index].Rename(slots[index].Name, index);
    }
}
