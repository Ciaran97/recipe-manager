using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Shopping;

public sealed partial class ShoppingStore
{
    private sealed record SourceResult(ShoppingCalculation Calculation, IReadOnlyList<ShoppingSourceRevision> Revisions, long HouseholdRevision);
    private async Task<SourceResult> Sources(RequestActor actor, ShoppingSelection? selected, ShoppingBasis basis, CancellationToken ct)
    {
        var inputs = new List<ShoppingCalculationSource>(); var versions = new List<ShoppingSourceRevision>();
        if (selected is not null)
        {
            var selection = GenerateShoppingList.Selection(selected, basis);
            if (selection.Kind == ShoppingSourceKind.Recipes)
            {
                foreach (var entry in selection.Entries!)
                {
                    var recipe = await recipes.ReadWithinTransactionAsync(actor.HouseholdId, entry.RecipeId, ct);
                    var key = $"recipe:{recipe.Id:N}";
                    inputs.Add(new(key, recipe.Title, MealType.Cook, RecipeSnapshotFactory.Create(recipe), entry.Servings));
                    versions.Add(new(key, recipe.Revision, recipe.Id, recipe.Revision));
                }
            }
            else
            {
                var events = await (from source in database.CookingEvents.AsNoTracking()
                                    join meal in database.ScheduledMeals.AsNoTracking() on new { source.HouseholdId, Id = source.ScheduledMealId } equals new { meal.HouseholdId, meal.Id }
                                    where source.HouseholdId == actor.HouseholdId && meal.HouseholdId == actor.HouseholdId
                                        && meal.Type == MealType.Cook && meal.Date >= selection.From!.Value && meal.Date <= selection.To!.Value
                                    orderby source.Id
                                    select new { Source = source, meal.Date }).ToArrayAsync(ct);
                foreach (var entry in events)
                {
                    var source = entry.Source; var snapshot = RecipeSnapshotFactory.Deserialize(source.RecipeSnapshotJson);
                    var key = $"cook:{source.Id:N}";
                    inputs.Add(new(key, snapshot.Title + " (" + entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")", MealType.Cook,
                        snapshot, source.PlannedPrepared, source.ConfirmedAt is null ? null : source.ActualPrepared));
                    versions.Add(new(key, source.Revision, source.RecipeId, source.RecipeRevision));
                }
            }
        }
        else if (basis == ShoppingBasis.Actual) throw new Core.Shared.DomainValidationException("basis", "actuals_required", "Choose a meal range for actual preparation quantities.");
        // All requirement-affecting writers hold this same household lock; no nested transaction
        // or external extraction is needed for a consistent source snapshot.
        var household = await database.Households.SingleAsync(x => x.Id == actor.HouseholdId, ct);
        return new(ShoppingRequirements.Calculate(inputs, basis), versions.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(), household.ShoppingSourcesRevision);
    }
    public Task<ShoppingPreview> PreviewAsync(RequestActor actor, Guid id, long expected, ShoppingPreviewWrite request, CancellationToken ct)
        => Within(actor, id, async token =>
        {
            var list = await List(actor, id, token); Match(list.Revision, expected);
            var selection = request.Selection ?? list.Selection; var basis = request.Basis ?? list.Basis;
            if (selection is not null) selection = GenerateShoppingList.Selection(selection, basis);
            var source = await Sources(actor, selection, basis, token); var items = await Items(actor, id, token);
            return new ShoppingPreview(list.Revision, source.HouseholdRevision, source.Revisions, Core.Modules.Ingredients.Contracts.IngredientCalculator.PolicyVersion,
                selection, basis, ReconcileShoppingList.Changes(list, items, source.Calculation), items.Where(x => x.Manual).Select(x => ShoppingViews.Item(x)).ToArray());
        }, ct);
    public Task<ShoppingListRead> ApplyAsync(RequestActor actor, Guid id, long expected, ShoppingApplyWrite request, CancellationToken ct)
        => Within(actor, id, async token =>
        {
            var list = await List(actor, id, token); Match(list.Revision, expected); Match(list.Revision, request.ListRevision);
            var household = await database.Households.SingleAsync(x => x.Id == actor.HouseholdId, token);
            if (request.HouseholdSourceRevision != household.ShoppingSourcesRevision || request.PolicyVersion != Core.Modules.Ingredients.Contracts.IngredientCalculator.PolicyVersion)
                throw new ShoppingSourceConflictException();
            var selection = request.Selection is null ? null : GenerateShoppingList.Selection(request.Selection, request.Basis);
            var source = await Sources(actor, selection, request.Basis, token);
            if (request.SourceRevisions is null || request.SourceRevisions.Any(x => x is null)
                || !request.SourceRevisions.OrderBy(x => x.Key, StringComparer.Ordinal).SequenceEqual(source.Revisions)) throw new ShoppingSourceConflictException();
            var items = await Items(actor, id, token);
            var edits = ReconcileShoppingList.ApplyChoices(list, items, source.Calculation, request.Choices);
            database.AddRange(edits.Added); database.RemoveRange(edits.Deleted);
            list.Rebase(selection, request.Basis, source.Calculation, source.Revisions, source.HouseholdRevision);
            await Finish(actor, token, list); return await Read(actor, id, token);
        }, ct);
}
