using RecipeManager.Core.Modules.MealPlanning.Contracts;

namespace RecipeManager.Core.Modules.Leftovers.Contracts;

public sealed record LeftoverRead(Guid Id, Guid SourceCookingEventId, long SourceRevision, string RecipeTitle, DateOnly SourceDate, decimal PlannedPortions,
    decimal? ConsumedPortions, decimal ReservedRemainder, decimal Commitment, DateTimeOffset? ConfirmedAt);
public sealed record AllocationReservationRead(Guid MealId, long MealRevision, DateOnly Date, Guid SlotId, LeftoverRead Allocation);
public sealed record LeftoversAvailability(Guid SourceCookingEventId, long SourceRevision, Guid SourceMealId, DateOnly Date, Guid SlotId,
    string RecipeTitle, Guid SourceRecipeId, long RecipeRevision, decimal PlannedPrepared, decimal PlannedEaten,
    decimal? ActualPrepared, decimal? ActualEaten, DateTimeOffset? ConfirmedAt,
    string Basis, decimal Capacity, decimal AvailablePortions, IReadOnlyList<AllocationReservationRead> Allocations);
public sealed record LeftoverWrite(Guid SourceCookingEventId, long SourceRevision, DateOnly Date, Guid SlotId, decimal PlannedPortions,
    decimal? ReservedRemainder = null, long? PreviousSourceRevision = null);
public sealed record ConsumptionWrite(decimal ConsumedPortions, decimal ReservedRemainder, long SourceRevision, CookingActuals? SourceActuals = null);
public sealed record ClearConsumptionWrite(decimal ReservedRemainder, long SourceRevision);
