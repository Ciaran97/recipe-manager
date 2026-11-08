using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Leftovers.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Leftovers;

public sealed class ConfirmConsumption(IPlannerStore store)
{
    public Task<ScheduledMealRead> ConfirmSourceAsync(RequestActor actor, Guid sourceId, long expected, ActualsWrite request, CancellationToken ct)
    {
        ValidateActuals(request.ActualPrepared, request.ActualEaten);
        return store.ConfirmActualsAsync(actor, Id(sourceId), expected, request, ct);
    }
    public Task<ScheduledMealRead> ClearSourceAsync(RequestActor actor, Guid sourceId, long expected, IReadOnlyList<DependentResolution>? resolutions, CancellationToken ct)
        => store.ClearActualsAsync(actor, Id(sourceId), expected, resolutions, ct);
    public Task<ScheduledMealRead> ConfirmAsync(RequestActor actor, Guid mealId, MealVersion expected, ConsumptionWrite request, CancellationToken ct)
    {
        PortionQuantity.Validate(request.ConsumedPortions, allowZero: true); PortionQuantity.Validate(request.ReservedRemainder, allowZero: true);
        if (request.SourceActuals is { } actuals) ValidateActuals(actuals.ActualPrepared, actuals.ActualEaten);
        return store.ConsumeAsync(actor, Id(mealId), expected, request, ct);
    }
    public Task<ScheduledMealRead> ClearAsync(RequestActor actor, Guid mealId, MealVersion expected, ClearConsumptionWrite request, CancellationToken ct)
    {
        PortionQuantity.Validate(request.ReservedRemainder, allowZero: true);
        return store.ClearConsumptionAsync(actor, Id(mealId), expected, request, ct);
    }
    public Task<ScheduledMealRead?> ResolveAsync(RequestActor actor, Guid sourceId, long expected, SourceResolutionWrite request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Kind) || request.Dependents is null || request.ExpectedSources is null)
            throw new DomainValidationException("resolution", "invalid_resolution", "Choose a supported source action and provide explicit dependent changes and source revisions.");
        if (request.Dependents.Count > 1000 || request.ExpectedSources.Count > 1000
            || request.Dependents.Any(x => x is null) || request.ExpectedSources.Any(x => x is null)
            || request.Dependents.Select(x => x.MealId).Distinct().Count() != request.Dependents.Count
            || request.ExpectedSources.Select(x => x.SourceCookingEventId).Distinct().Count() != request.ExpectedSources.Count)
            throw new DomainValidationException("resolution", "invalid_resolution", "Use at most 1000 unique dependent changes and source revisions.");
        if (request.RecipeId is { } recipeId) DomainRules.NonEmptyId(recipeId, "recipeId");
        if (request.ExpectedRecipeRevision is <= 0) throw new DomainValidationException("expectedRecipeRevision", "invalid_revision", "Recipe revisions must be positive.");
        return store.ResolveAsync(actor, Id(sourceId), expected, request, ct);
    }
    private static void ValidateActuals(decimal prepared, decimal eaten) => _ = new CookingPortions(prepared, eaten);
    private static Guid Id(Guid id) => DomainRules.NonEmptyId(id, "id");
}
