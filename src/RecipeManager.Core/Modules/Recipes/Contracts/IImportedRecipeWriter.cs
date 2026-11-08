using RecipeManager.Core.Modules.Households.Contracts;

namespace RecipeManager.Core.Modules.Recipes.Contracts;

public sealed record ImportedRecipeIdentity(Guid RecipeId, long Revision);
// The importing module owns the transaction; recipes retain validation and creation ownership.
public interface IImportedRecipeWriter
{
    RecipeWrite Validate(RecipeWrite recipe);
    Task<ImportedRecipeIdentity> CreateWithinTransactionAsync(RequestActor actor, RecipeWrite recipe, CancellationToken cancellationToken);
}
