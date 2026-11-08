using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;

namespace RecipeManager.Core.Modules.MealPlanning.Contracts;

public sealed record SnapshotIngredient(Guid Id, Guid? GroupId, int Position, Guid IngredientId, string IngredientName,
    Guid? CategoryId, string? CategoryName, string QuantityText, QuantityKind Kind, IngredientUnit Unit,
    string? Preparation, bool Uncertain, ExactQuantity? Exact, IngredientDimension Dimension, ExactQuantity CanonicalFactor);
public sealed record RecipeSnapshot(int SchemaVersion, Guid RecipeId, long RecipeRevision, string Title, string? Description,
    Guid? ImageAssetId, decimal DefaultServings, int? PrepMinutes, int? CookMinutes, string? Notes, string? SourceUrl,
    MeasurementStandard MeasurementStandard, int PolicyVersion, IReadOnlyList<RecipeGroupRead> Groups,
    IReadOnlyList<SnapshotIngredient> Ingredients, IReadOnlyList<RecipeInstructionRead> Instructions);
public sealed record SnapshotRefreshPreview(long EventRevision, long RecipeRevision, RecipeSnapshot Current, RecipeSnapshot Proposed);
