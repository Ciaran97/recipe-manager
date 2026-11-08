using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Leftovers;

public sealed class AllocationInvariantTests
{
    private static Allocation Reserve(decimal quantity) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), quantity);

    [Fact]
    public void Planning_and_actual_confirmation_use_one_capacity_basis_at_a_time()
    {
        var planned = new CookingPortions(4m, 2m);
        Assert.Equal(2m, planned.Capacity); Assert.False(planned.IsActual);
        var actual = new CookingPortions(4m, 2m, 5m, 1.5m);
        Assert.Equal(3.5m, actual.Capacity); Assert.True(actual.IsActual);
        Assert.Equal(4m, actual.PlannedPrepared); Assert.Equal(2m, actual.PlannedEaten);
    }

    [Fact]
    public void Partial_consumption_deducts_consumed_plus_remaining_reservation_only()
    {
        var allocation = Reserve(2m);
        allocation.SetConsumption(1.5m, 0.5m, DateTimeOffset.UtcNow);
        Assert.Equal(2m, allocation.PlannedPortions); Assert.Equal(2m, allocation.Commitment);
        Assert.Equal(0m, AllocationBalance.Available(2m, [allocation]));
    }

    [Fact]
    public void Released_reservation_becomes_available_without_changing_consumed_or_planned_portions()
    {
        var first = Reserve(2m); first.SetConsumption(1.5m, 0.5m, DateTimeOffset.UtcNow);
        first.ReleaseReservation(); first.ReleaseReservation();
        Assert.Equal(2m, first.PlannedPortions); Assert.Equal(1.5m, first.ConsumedPortions);
        Assert.Equal(0m, first.ReservedRemainder); Assert.Equal(0.5m, AllocationBalance.Available(2m, [first]));
        var second = Reserve(0.5m);
        Assert.Equal(0m, AllocationBalance.Available(2m, [first, second]));
        var restored = Allocation.Restore(first.Id, first.HouseholdId, first.ScheduledMealId, first.SourceCookingEventId,
            first.PlannedPortions, first.ConsumedPortions, first.ReservedRemainder, first.ConfirmedAt);
        Assert.Equal(0m, AllocationBalance.Available(2m, [restored, second]));
        Assert.Equal(1.5m, restored.ConsumedPortions);
    }

    [Fact]
    public void Corrections_replace_absolute_consumption_and_require_explicit_remaining_reservation()
    {
        var allocation = Reserve(2m); var confirmed = DateTimeOffset.UtcNow;
        allocation.SetConsumption(1.5m, 0.5m, confirmed);
        allocation.SetConsumption(1m, 0.5m, confirmed);
        Assert.Equal(1.5m, allocation.Commitment);
        allocation.ChangePlan(3m, 0m);
        Assert.Equal(3m, allocation.PlannedPortions); Assert.Equal(1m, allocation.Commitment);
        allocation.SetConsumption(null, 0.5m, null);
        Assert.Null(allocation.ConsumedPortions); Assert.Null(allocation.ConfirmedAt);
        Assert.Equal(0.5m, allocation.Commitment);
    }

    [Fact]
    public void Capacity_correction_and_extra_allocations_cannot_over_allocate()
    {
        var first = Reserve(2m); var second = Reserve(0.5m);
        Assert.Throws<AllocationConflictException>(() => AllocationBalance.Available(2m, [first, second]));
        first.SetConsumption(1.5m, 0m, DateTimeOffset.UtcNow);
        Assert.Throws<AllocationConflictException>(() => AllocationBalance.Available(1m, [first]));
        Assert.Equal(0m, AllocationBalance.Available(1.5m, [first]));
    }

    [Fact]
    public void Actual_consumption_above_planned_intention_is_explicit_and_still_capacity_checked()
    {
        var allocation = Reserve(0.5m); allocation.SetConsumption(1.5m, 0m, DateTimeOffset.UtcNow);
        Assert.Equal(0.5m, allocation.PlannedPortions); Assert.Equal(1.5m, allocation.Commitment);
        Assert.Equal(0.5m, AllocationBalance.Available(2m, [allocation]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(1000001)]
    [InlineData(0.0000001)]
    public void Planned_portions_reject_nonpositive_excessive_and_overprecise_values(double value)
        => Assert.Throws<DomainValidationException>(() => Reserve((decimal)value));

    [Fact]
    public void Invalid_consumption_is_rejected_without_mutating_the_allocation()
    {
        var allocation = Reserve(1.5m);
        Assert.Throws<DomainValidationException>(() => allocation.SetConsumption(-1m, 0m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainValidationException>(() => allocation.SetConsumption(0m, -1m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainValidationException>(() => allocation.SetConsumption(1m, 0m, null));
        Assert.Throws<DomainValidationException>(() => allocation.SetConsumption(null, 0m, DateTimeOffset.UtcNow));
        Assert.Equal(1.5m, allocation.ReservedRemainder); Assert.Null(allocation.ConsumedPortions);
    }

    [Fact]
    public void Source_quantities_require_valid_planned_and_actual_pairs()
    {
        Assert.Throws<DomainValidationException>(() => new CookingPortions(0m, 0m));
        Assert.Throws<DomainValidationException>(() => new CookingPortions(4m, 5m));
        Assert.Throws<DomainValidationException>(() => new CookingPortions(4m, 2m, 4m, null));
        Assert.Throws<DomainValidationException>(() => new CookingPortions(4m, 2m, null, 1m));
        Assert.Throws<DomainValidationException>(() => new CookingPortions(4m, 2m, 2m, 3m));
        Assert.Equal(2.5m, new CookingPortions(2.5m, 0m).Capacity);
    }
    [Fact]
    public void Released_or_consumed_plans_need_explicit_reservations_when_resized()
    {
        var allocation = Reserve(2m);
        Assert.Equal(3m, allocation.ReservationForPlan(3m, null));
        allocation.ReleaseReservation();
        Assert.Equal(0m, allocation.ReservationForPlan(2m, null));
        Assert.Throws<DomainValidationException>(() => allocation.ReservationForPlan(3m, null));
        Assert.Equal(0.5m, allocation.ReservationForPlan(3m, 0.5m));
        allocation.SetConsumption(1m, 0.5m, DateTimeOffset.UtcNow);
        Assert.Throws<DomainValidationException>(() => allocation.ReservationForPlan(3m, null));
        Assert.Equal(0.5m, allocation.ReservationForPlan(2m, null));
    }
}
