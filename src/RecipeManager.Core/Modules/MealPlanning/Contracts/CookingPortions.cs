using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.MealPlanning.Contracts;

/// <summary>One capacity basis: planned until an actual prepared/eaten pair is confirmed.</summary>
public sealed record CookingPortions
{
    public CookingPortions(decimal plannedPrepared, decimal plannedEaten, decimal? actualPrepared = null, decimal? actualEaten = null)
    {
        ValidatePair(plannedPrepared, plannedEaten);
        if (actualPrepared.HasValue != actualEaten.HasValue)
            throw new DomainValidationException("actuals", "incomplete_actuals", "Confirm both actual prepared and eaten portions, or clear both.");
        if (actualPrepared is { } prepared && actualEaten is { } eaten) ValidatePair(prepared, eaten);
        PlannedPrepared = plannedPrepared; PlannedEaten = plannedEaten;
        ActualPrepared = actualPrepared; ActualEaten = actualEaten;
    }
    public decimal PlannedPrepared { get; }
    public decimal PlannedEaten { get; }
    public decimal? ActualPrepared { get; }
    public decimal? ActualEaten { get; }
    public bool IsActual => ActualPrepared.HasValue;
    public decimal Capacity => (ActualPrepared ?? PlannedPrepared) - (ActualEaten ?? PlannedEaten);
    private static void ValidatePair(decimal prepared, decimal eaten)
    {
        PortionQuantity.Validate(prepared); PortionQuantity.Validate(eaten, allowZero: true);
        if (eaten > prepared) throw new DomainValidationException("eaten", "exceeds_prepared", "Portions eaten cannot exceed portions prepared.");
    }
}
