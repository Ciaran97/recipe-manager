using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Leftovers.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Leftovers;

public sealed class AllocationService(IPlannerStore store)
{
    public Task<LeftoversAvailability> AvailabilityAsync(RequestActor actor, Guid sourceId, CancellationToken ct)
        => store.AvailabilityAsync(actor, DomainRules.NonEmptyId(sourceId, "sourceCookingEventId"), ct);
    public Task<ScheduledMealRead> CreateAsync(RequestActor actor, LeftoverWrite request, Guid? retryKey, CancellationToken ct)
    {
        Validate(request); return store.CreateLeftoversAsync(actor, request, retryKey, ct);
    }
    public Task<ScheduledMealRead> UpdateAsync(RequestActor actor, Guid mealId, MealVersion expected, LeftoverWrite request, CancellationToken ct)
    {
        Validate(request); return store.UpdateLeftoversAsync(actor, DomainRules.NonEmptyId(mealId, "mealId"), expected, request, ct);
    }
    private static void Validate(LeftoverWrite request)
    {
        DomainRules.NonEmptyId(request.SourceCookingEventId, "sourceCookingEventId"); DomainRules.NonEmptyId(request.SlotId, "slotId");
        PortionQuantity.Validate(request.PlannedPortions);
        if (request.ReservedRemainder is { } remainder) PortionQuantity.Validate(remainder, allowZero: true);
        if (request.SourceRevision < 1 || request.PreviousSourceRevision is <= 0)
            throw new DomainValidationException("sourceRevision", "invalid_revision", "Source revisions must be positive.");
    }
}
