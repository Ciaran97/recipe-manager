using RecipeManager.Core.Modules.Ingredients.Contracts;

namespace RecipeManager.Core.Modules.Recipes.Contracts;

public sealed record RecipeGroupWrite(string Title, int Position, Guid? Id = null);
public sealed record RecipeIngredientWrite(int Position, Guid IngredientId, string QuantityText, QuantityKind Kind,
    IngredientUnit Unit, Guid? Id = null, Guid? GroupId = null, string? Preparation = null, bool Uncertain = false);
public sealed record RecipeInstructionWrite(int Position, string Text, Guid? Id = null);
public sealed record RecipeWrite(string Title, decimal DefaultServings, IReadOnlyList<RecipeGroupWrite> Groups,
    IReadOnlyList<RecipeIngredientWrite> Ingredients, IReadOnlyList<RecipeInstructionWrite> Instructions,
    IReadOnlyList<Guid> CollectionIds, IReadOnlyList<Guid> TagIds, string? Description = null, Guid? ImageAssetId = null,
    int? PrepMinutes = null, int? CookMinutes = null, string? Notes = null, string? SourceUrl = null,
    MeasurementStandard MeasurementStandard = MeasurementStandard.AU, bool AcknowledgeUncertainIngredients = false);
public sealed record RecipeGroupRead(Guid Id, string Title, int Position);
public sealed record RecipeIngredientRead(Guid Id, Guid? GroupId, int Position, Guid IngredientId, string IngredientName, Guid? CategoryId,
    string? CategoryName, string QuantityText, QuantityKind Kind, IngredientUnit Unit, string? Preparation, bool Uncertain, ExactQuantity? Exact);
public sealed record RecipeInstructionRead(Guid Id, int Position, string Text);
public sealed record RecipeRead(Guid Id, long Revision, string Title, string? Description, Guid? ImageAssetId, decimal DefaultServings,
    int? PrepMinutes, int? CookMinutes, string? Notes, string? SourceUrl, MeasurementStandard MeasurementStandard, int PolicyVersion,
    bool Archived, bool Favourite, IReadOnlyList<RecipeGroupRead> Groups, IReadOnlyList<RecipeIngredientRead> Ingredients,
    IReadOnlyList<RecipeInstructionRead> Instructions, IReadOnlyList<Guid> CollectionIds, IReadOnlyList<Guid> TagIds);
public sealed record RecipeIngredientPreview(Guid Id, Guid IngredientId, string IngredientName, string QuantityText, IngredientUnit Unit,
    ExactQuantity? Exact, string DisplayText, bool IsApproximate, bool Uncertain);
public sealed record RecipeServingPreview(Guid RecipeId, long RecipeRevision, decimal DefaultServings, decimal TargetServings,
    IReadOnlyList<RecipeIngredientPreview> Ingredients);
public sealed record RecipePage(IReadOnlyList<RecipeRead> Items, Guid? NextId);

public interface IRecipeReferences
{
    Task<bool> UsesRecipeAsync(Guid householdId, Guid recipeId, CancellationToken cancellationToken);
}
public interface IImageReferences
{
    Task<bool> UsesImageAsync(Guid householdId, Guid imageAssetId, CancellationToken cancellationToken);
}
