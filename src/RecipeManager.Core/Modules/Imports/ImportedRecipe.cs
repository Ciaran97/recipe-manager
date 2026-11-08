using RecipeManager.Core.Modules.Ingredients.Contracts;

namespace RecipeManager.Core.Modules.Imports;

public sealed record ImportedIngredient(string OriginalText, string IngredientName, string QuantityText, QuantityKind Kind,
    IngredientUnit? Unit, string? OriginalUnit, string? Preparation, string? Group, bool Uncertain, Guid? MatchedIngredientId = null, string? MatchedIngredientName = null);
public sealed record ImportedInstruction(string Text, string? Group = null);
public sealed record ImportedRecipe(string? Title, string? Description, decimal? DefaultServings, int? PrepMinutes, int? CookMinutes,
    IReadOnlyList<ImportedIngredient> Ingredients, IReadOnlyList<ImportedInstruction> Instructions, string? Notes,
    string? SourceUrl, string? ImageUrl, MeasurementStandard? MeasurementStandard, IReadOnlyList<string> SuggestedTags,
    IReadOnlyList<string> UncertaintyFields);
public sealed record ImportExtraction(IReadOnlyList<ImportedRecipe> Recipes);
public sealed class ImportFailureException(string code) : Exception
{
    public string Code { get; } = code;
}
