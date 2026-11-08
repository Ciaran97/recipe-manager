using System.Text.Json;
using System.Text.Json.Serialization;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.MealPlanning;

public static class RecipeSnapshotFactory
{
    private static readonly JsonSerializerOptions Json = Options();
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
    public static RecipeSnapshot Create(RecipeRead recipe)
    {
        if (recipe.PolicyVersion != IngredientCalculator.PolicyVersion)
            throw new DomainValidationException("policyVersion", "unsupported_policy", "The source recipe uses an unsupported calculation policy.");
        PortionQuantity.Validate(recipe.DefaultServings); IngredientCalculator.ValidateBatchSize(recipe.Ingredients.Count);
        var ingredients = recipe.Ingredients.Select(line =>
        {
            var parsed = line.Kind == QuantityKind.Unknown ? Quantity.Unknown(line.QuantityText) : Quantity.Parse(line.QuantityText);
            if (!Enum.IsDefined(line.Kind) || (parsed.Exact is null) != (line.Exact is null)
                || (line.Exact is { } exact && parsed.Exact != exact.ToAmount()))
                throw new DomainValidationException("recipeSnapshot", "inconsistent_quantity", "Snapshot quantities must match their preserved original values.");
            var canonical = IngredientCalculator.Canonical(new ExactAmount(1, 1), line.Unit, recipe.MeasurementStandard);
            return new SnapshotIngredient(line.Id, line.GroupId, line.Position, line.IngredientId, line.IngredientName,
                line.CategoryId, line.CategoryName, line.QuantityText, line.Kind, line.Unit, line.Preparation, line.Uncertain,
                line.Exact, canonical.Dimension, ExactQuantity.From(canonical.Amount));
        }).ToList().AsReadOnly();
        // Favourite and current organiser membership are personal/live library metadata, not a cooking basis.
        return new RecipeSnapshot(1, recipe.Id, recipe.Revision, recipe.Title, recipe.Description, recipe.ImageAssetId,
            recipe.DefaultServings, recipe.PrepMinutes, recipe.CookMinutes, recipe.Notes, recipe.SourceUrl,
            recipe.MeasurementStandard, recipe.PolicyVersion, recipe.Groups.ToList().AsReadOnly(), ingredients,
            recipe.Instructions.ToList().AsReadOnly());
    }
    public static string Serialize(RecipeSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);
    public static RecipeSnapshot Deserialize(string json)
    {
        var snapshot = JsonSerializer.Deserialize<RecipeSnapshot>(json, Json)
            ?? throw new DomainValidationException("recipeSnapshot", "invalid_snapshot", "The saved recipe snapshot is missing.");
        if (snapshot.SchemaVersion != 1 || snapshot.PolicyVersion != IngredientCalculator.PolicyVersion)
            throw new DomainValidationException("recipeSnapshot", "unsupported_policy", "The saved snapshot requires an unsupported calculation policy.");
        return snapshot;
    }
}
