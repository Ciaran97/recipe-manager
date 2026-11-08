namespace RecipeManager.Core.Modules.Ingredients.Contracts;

public sealed record ImportIngredientMatch(Guid Id, string CanonicalName);
public interface IImportIngredientMatcher
{
    Task<IReadOnlyDictionary<string, ImportIngredientMatch>> MatchWithinTransactionAsync(Guid householdId, IReadOnlyList<string> originalNames, CancellationToken cancellationToken);
}
