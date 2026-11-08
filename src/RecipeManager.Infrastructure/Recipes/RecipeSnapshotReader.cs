using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Recipes;

public sealed class RecipeSnapshotReader(RecipeManagerDbContext database) : IRecipeSnapshotSource
{
    public async Task<RecipeRead> ReadWithinTransactionAsync(Guid householdId, Guid recipeId, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null || database.CurrentHouseholdId != householdId)
            throw new InvalidOperationException("Recipe snapshot reads must join the caller's household transaction.");
        var recipe = await database.Recipes.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == recipeId, cancellationToken)
            ?? throw new ResourceMissingException();
        return (await RecipeProjection.ReadAsync(database, householdId, [recipe], cancellationToken)).Single();
    }
}
