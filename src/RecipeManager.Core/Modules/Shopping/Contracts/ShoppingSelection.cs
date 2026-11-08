using System.Text.Json.Serialization;
using RecipeManager.Core.Modules.Ingredients.Contracts;

namespace RecipeManager.Core.Modules.Shopping.Contracts;

public enum ShoppingSourceKind
{
    [JsonStringEnumMemberName("mealRange")] MealRange,
    [JsonStringEnumMemberName("recipes")] Recipes,
    [JsonStringEnumMemberName("combined")] Combined
}
public sealed record ShoppingMealRange(DateOnly From, DateOnly To);
public sealed record ShoppingRecipeSelection(Guid RecipeId, decimal Servings);
public sealed record ShoppingSelection(ShoppingSourceKind Kind, DateOnly? From = null, DateOnly? To = null,
    IReadOnlyList<ShoppingRecipeSelection>? Entries = null, IReadOnlyList<ShoppingMealRange>? Ranges = null);
public sealed record ShoppingSourceRevision(string Key, long Revision, Guid RecipeId, long RecipeRevision);
public sealed record ShoppingQuantityInput(string QuantityText, QuantityKind Kind, IngredientUnit Unit,
    MeasurementStandard MeasurementStandard = MeasurementStandard.AU);
public sealed record ShoppingQuantityValue(string QuantityText, QuantityKind Kind, IngredientUnit Unit,
    MeasurementStandard MeasurementStandard, ExactQuantity? Exact, IngredientDimension? Dimension,
    string DisplayText, bool IsApproximate);
