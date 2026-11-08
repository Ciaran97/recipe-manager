using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.MealPlanning;

public sealed class RecipeSnapshotTests
{
    private static RecipeRead Recipe() => new(Guid.NewGuid(), 1, "Original rice", "Description", Guid.NewGuid(), 3m, 10, 20, "Notes", "https://example.test/rice", MeasurementStandard.US, 1,
        false, true, [new RecipeGroupRead(Guid.NewGuid(), "Rice", 0)],
        [new RecipeIngredientRead(Guid.NewGuid(), null, 0, Guid.NewGuid(), "Rice", null, "Pantry", "1/3", QuantityKind.Numeric, IngredientUnit.Cup, "rinsed", false, new ExactQuantity("1", "3")),
         new RecipeIngredientRead(Guid.NewGuid(), null, 1, Guid.NewGuid(), "Salt", null, "Pantry", "to taste", QuantityKind.Unknown, IngredientUnit.Gram, null, false, null)],
        [new RecipeInstructionRead(Guid.NewGuid(), 0, "Cook rice.")], [], []);
    [Fact]
    public void Snapshot_retains_complete_original_recipe_and_explicit_conversion_factors()
    {
        var recipe = Recipe(); var snapshot = RecipeSnapshotFactory.Create(recipe);
        Assert.Equal(recipe.Id, snapshot.RecipeId); Assert.Equal("Original rice", snapshot.Title);
        Assert.Equal(recipe.ImageAssetId, snapshot.ImageAssetId); Assert.Equal(3m, snapshot.DefaultServings);
        Assert.Equal("Notes", snapshot.Notes); Assert.Equal("Cook rice.", snapshot.Instructions[0].Text);
        Assert.Equal("1/3", snapshot.Ingredients[0].QuantityText);
        Assert.Equal(new ExactQuantity("1", "3"), snapshot.Ingredients[0].Exact);
        Assert.Equal(new ExactQuantity("59147", "250"), snapshot.Ingredients[0].CanonicalFactor);
        Assert.Equal(IngredientDimension.Volume, snapshot.Ingredients[0].Dimension);
        Assert.Null(snapshot.Ingredients[1].Exact); Assert.Equal("to taste", snapshot.Ingredients[1].QuantityText);
        var edited = recipe with { Title = "Edited live recipe", Ingredients = [] };
        Assert.Equal("Original rice", snapshot.Title); Assert.Equal(2, snapshot.Ingredients.Count);
        Assert.Equal("Edited live recipe", RecipeSnapshotFactory.Create(edited).Title);
    }
    [Fact]
    public void Refresh_preview_does_not_mutate_and_stale_apply_rejects_before_changes()
    {
        var recipe = Recipe(); var snapshot = RecipeSnapshotFactory.Create(recipe);
        var json = RecipeSnapshotFactory.Serialize(snapshot);
        var cooking = new CookingEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), recipe.Id, recipe.Revision, json, 4m, 2m);
        cooking.SetActuals(5m, 1.5m, DateTimeOffset.UtcNow);
        var edited = recipe with { Revision = 2, Title = "Explicit refresh" };
        var preview = RefreshRecipeSnapshot.Preview(cooking, edited);
        Assert.Equal(json, cooking.RecipeSnapshotJson);
        Assert.Throws<RecipeSnapshotConflictException>(() => RefreshRecipeSnapshot.Apply(cooking, edited, preview.EventRevision - 1, preview.RecipeRevision));
        Assert.Throws<RecipeSnapshotConflictException>(() => RefreshRecipeSnapshot.Apply(cooking, edited with { Revision = 3 }, preview.EventRevision, preview.RecipeRevision));
        Assert.Equal(json, cooking.RecipeSnapshotJson);
        RefreshRecipeSnapshot.Apply(cooking, edited, preview.EventRevision, preview.RecipeRevision);
        Assert.Equal("Explicit refresh", RecipeSnapshotFactory.Deserialize(cooking.RecipeSnapshotJson).Title);
        Assert.Equal(4m, cooking.PlannedPrepared); Assert.Equal(2m, cooking.PlannedEaten);
        Assert.Equal(5m, cooking.ActualPrepared); Assert.Equal(1.5m, cooking.ActualEaten);
    }
    [Fact]
    public void Snapshot_rejects_inconsistent_known_and_unknown_quantity_representations()
    {
        var recipe = Recipe(); var line = recipe.Ingredients[0];
        Assert.Throws<DomainValidationException>(() => RecipeSnapshotFactory.Create(recipe with { Ingredients = [line with { Exact = null }] }));
        Assert.Throws<DomainValidationException>(() => RecipeSnapshotFactory.Create(recipe with { Ingredients = [line with { Kind = QuantityKind.Unknown }] }));
    }
}
