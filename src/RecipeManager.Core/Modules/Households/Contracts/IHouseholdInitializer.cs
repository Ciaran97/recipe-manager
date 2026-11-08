namespace RecipeManager.Core.Modules.Households.Contracts;

// Known module defaults (ingredient categories and meal slots) join the setup transaction.
public interface IHouseholdInitializer
{
    Task InitializeAsync(Guid householdId, CancellationToken cancellationToken);
}
