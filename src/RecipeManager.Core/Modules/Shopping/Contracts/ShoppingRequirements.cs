using System.Text.Json.Serialization;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;

namespace RecipeManager.Core.Modules.Shopping.Contracts;

public enum ShoppingBasis
{
    [JsonStringEnumMemberName("planned")] Planned,
    [JsonStringEnumMemberName("actual")] Actual
}

// Source keys identify a selected cooking event or recipe, never a mutable title or alias.
public sealed record ShoppingCalculationSource(string Key, string Label, MealType Type, RecipeSnapshot? Recipe,
    decimal PlannedPrepared, decimal? ActualPrepared = null);
public sealed record ShoppingContribution(string SourceKey, string SourceLabel, Guid SourceRecipeId, long RecipeRevision, decimal Servings, Guid IngredientRowId,
    string QuantityText, IngredientUnit Unit, string? Preparation, bool Uncertain, ExactQuantity? CanonicalExact);
public sealed record ShoppingRequirement(string Key, Guid IngredientId, string Label, Guid? CategoryId,
    string? CategoryName, IngredientDimension? Dimension, ExactQuantity? Exact, string DisplayText,
    bool IsApproximate, bool Unresolved, bool AdditionalAmountUnresolved, IReadOnlyList<ShoppingContribution> Contributions);
public sealed record ShoppingCalculation(IReadOnlyList<ShoppingRequirement> Items, int UnresolvedCount);
