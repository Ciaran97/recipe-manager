using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Leftovers;

/// <summary>Planned intention is retained for display. Only consumed plus still-reserved portions commit capacity.</summary>
public sealed class Allocation
{
    private Allocation() { }
    public Allocation(Guid id, Guid householdId, Guid scheduledMealId, Guid sourceCookingEventId, decimal plannedPortions)
    {
        var planned = PortionQuantity.Validate(plannedPortions);
        Id = DomainRules.NonEmptyId(id, "allocationId"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        ScheduledMealId = DomainRules.NonEmptyId(scheduledMealId, "scheduledMealId");
        SourceCookingEventId = DomainRules.NonEmptyId(sourceCookingEventId, "sourceCookingEventId");
        PlannedPortions = planned; ReservedRemainder = planned;
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid ScheduledMealId { get; private set; }
    public MealType MealType { get; private set; } = MealType.Leftovers;
    public Guid SourceCookingEventId { get; private set; }
    public decimal PlannedPortions { get; private set; }
    public decimal? ConsumedPortions { get; private set; }
    public decimal ReservedRemainder { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public decimal Commitment => (ConsumedPortions ?? 0m) + ReservedRemainder;

    public void SetConsumption(decimal? consumedPortions, decimal reservedRemainder, DateTimeOffset? confirmedAt)
    {
        if (consumedPortions is { } consumed) PortionQuantity.Validate(consumed, allowZero: true);
        PortionQuantity.Validate(reservedRemainder, allowZero: true);
        if (consumedPortions.HasValue != confirmedAt.HasValue)
            throw new DomainValidationException("consumedPortions", "inconsistent_confirmation", "Confirmed consumption requires a confirmation time; clearing consumption clears confirmation.");
        ConsumedPortions = consumedPortions; ReservedRemainder = reservedRemainder; ConfirmedAt = confirmedAt;
    }
    public void ReleaseReservation() => ReservedRemainder = 0m;
    public void ChangePlan(decimal plannedPortions, decimal reservedRemainder)
    {
        var planned = PortionQuantity.Validate(plannedPortions);
        var reserved = PortionQuantity.Validate(reservedRemainder, allowZero: true);
        PlannedPortions = planned; ReservedRemainder = reserved;
    }
    public decimal ReservationForPlan(decimal plannedPortions, decimal? requestedRemainder)
    {
        PortionQuantity.Validate(plannedPortions);
        if (requestedRemainder is { } explicitRemainder) return PortionQuantity.Validate(explicitRemainder, allowZero: true);
        if (plannedPortions == PlannedPortions) return ReservedRemainder;
        if (ConsumedPortions is null && ReservedRemainder == PlannedPortions) return plannedPortions;
        throw new DomainValidationException("reservedRemainder", "required", "Explicitly choose the remaining reservation when changing a partially consumed or released plan.");
    }
    public void ChangeSource(Guid sourceCookingEventId)
        => SourceCookingEventId = DomainRules.NonEmptyId(sourceCookingEventId, "sourceCookingEventId");

    public static Allocation Restore(Guid id, Guid householdId, Guid scheduledMealId, Guid sourceCookingEventId,
        decimal plannedPortions, decimal? consumedPortions, decimal reservedRemainder, DateTimeOffset? confirmedAt)
    {
        var restored = new Allocation(id, householdId, scheduledMealId, sourceCookingEventId, plannedPortions);
        // Restore the persisted remainder explicitly; never infer a new reservation from the planned quantity.
        restored.SetConsumption(consumedPortions, reservedRemainder, confirmedAt);
        return restored;
    }
}

public static class AllocationBalance
{
    public static decimal Available(decimal capacity, IEnumerable<Allocation> allocations)
    {
        PortionQuantity.Validate(capacity, allowZero: true);
        var committed = allocations.Sum(x => x.Commitment);
        if (committed > capacity) throw new AllocationConflictException("The proposed quantities exceed the source's available portions. Resolve dependent meals explicitly.");
        return capacity - committed;
    }
}
public sealed class AllocationConflictException(string message) : InvalidOperationException(message);
