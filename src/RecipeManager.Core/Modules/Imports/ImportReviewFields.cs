using RecipeManager.Core.Modules.Ingredients.Contracts;

namespace RecipeManager.Core.Modules.Imports;

public static class ImportReviewFields
{
    public static IReadOnlyList<string> For(string? title, decimal? servings, MeasurementStandard? standard,
        IReadOnlyList<ImportedIngredient> ingredients, IReadOnlyList<ImportedInstruction> instructions)
    {
        var flags = new List<string>();
        if (string.IsNullOrWhiteSpace(title)) flags.Add("title");
        if (servings is null) flags.Add("defaultServings");
        if (standard is null) flags.Add("measurementStandard");
        if (ingredients.Count == 0) flags.Add("ingredients");
        if (instructions.Count == 0) flags.Add("instructions");
        for (var index = 0; index < ingredients.Count; index++)
        {
            var row = ingredients[index];
            if (row.Kind == QuantityKind.Unknown) flags.Add($"ingredients[{index}].quantity");
            else if (row.Uncertain) flags.Add($"ingredients[{index}].review");
            if (row.Unit is null) flags.Add($"ingredients[{index}].unit");
        }
        return flags;
    }
}
