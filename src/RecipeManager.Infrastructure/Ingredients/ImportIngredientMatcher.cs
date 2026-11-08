using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Ingredients;

public sealed class ImportIngredientMatcher(RecipeManagerDbContext database) : IImportIngredientMatcher
{
    public async Task<IReadOnlyDictionary<string, ImportIngredientMatch>> MatchWithinTransactionAsync(Guid householdId, IReadOnlyList<string> names, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || database.CurrentHouseholdId != householdId)
            throw new InvalidOperationException("Import matching requires the household transaction.");
        IngredientCalculator.ValidateBatchSize(names.Count);
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            try { normalized.Add(name, CatalogueNames.Normalize(name)); }
            catch (DomainValidationException) { /* Retain invalid/unmatched names for user correction. */ }
        }
        var keys = normalized.Values.Distinct().ToArray();
        var matches = await database.IngredientNames.AsNoTracking().Where(x => keys.Contains(x.NormalizedName))
            .Join(database.Ingredients.AsNoTracking(), name => name.IngredientId, ingredient => ingredient.Id,
                (name, ingredient) => new { name.NormalizedName, ingredient.Id, ingredient.CanonicalName })
            .ToDictionaryAsync(x => x.NormalizedName, x => new ImportIngredientMatch(x.Id, x.CanonicalName), ct);
        return normalized.Where(x => matches.ContainsKey(x.Value)).ToDictionary(x => x.Key, x => matches[x.Value], StringComparer.Ordinal);
    }
}
