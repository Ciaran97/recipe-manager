using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.MealPlanning;

public sealed class MealModelTests
{
    private static CookingEvent Cook() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "{\"schemaVersion\":1,\"title\":\"Frozen rice\"}", 4m, 2m);
    [Fact]
    public void Plan_and_actual_changes_leave_the_recipe_snapshot_unchanged()
    {
        var cook = Cook(); var snapshot = cook.RecipeSnapshotJson;
        cook.ChangePlan(6m, 1.5m); cook.SetActuals(5m, 2m, DateTimeOffset.UtcNow);
        Assert.Equal(snapshot, cook.RecipeSnapshotJson); Assert.Equal(1, cook.RecipeRevision);
        Assert.Equal(3m, cook.Portions.Capacity); Assert.True(cook.Portions.IsActual);
        cook.SetActuals(null, null, null);
        Assert.Equal(4.5m, cook.Portions.Capacity); Assert.False(cook.Portions.IsActual);
        Assert.Equal(snapshot, cook.RecipeSnapshotJson);
    }
    [Fact]
    public void Rejected_portion_or_snapshot_replacements_preserve_the_original_state_and_revision()
    {
        var cook = Cook(); var revision = cook.Revision; var recipe = cook.RecipeId; var snapshot = cook.RecipeSnapshotJson;
        Assert.Throws<DomainValidationException>(() => cook.ChangePlan(1m, 2m));
        Assert.Throws<DomainValidationException>(() => cook.SetActuals(3m, null, DateTimeOffset.UtcNow));
        Assert.Throws<DomainValidationException>(() => cook.SetActuals(3m, 1m, null));
        Assert.Throws<DomainValidationException>(() => cook.ReplaceRecipe(Guid.NewGuid(), 0, "{}"));
        Assert.Equal(4m, cook.PlannedPrepared); Assert.Null(cook.ActualPrepared);
        Assert.Equal(recipe, cook.RecipeId); Assert.Equal(snapshot, cook.RecipeSnapshotJson); Assert.Equal(revision, cook.Revision);
    }
    [Fact]
    public void Only_an_explicit_replace_changes_the_source_recipe_basis()
    {
        var cook = Cook(); var revision = cook.Revision; var recipeId = Guid.NewGuid();
        cook.ReplaceRecipe(recipeId, 2, "{\"schemaVersion\":1,\"title\":\"Explicitly refreshed\"}");
        Assert.Equal(recipeId, cook.RecipeId); Assert.Equal(2, cook.RecipeRevision); Assert.Equal(revision + 1, cook.Revision);
        Assert.Equal(4m, cook.PlannedPrepared); Assert.Equal(2m, cook.PlannedEaten);
    }
    [Fact]
    public void Meal_position_and_slot_edits_validate_before_mutating_and_other_meals_require_labels()
    {
        var slot = new MealSlot(Guid.NewGuid(), Guid.NewGuid(), "Dinner", 2, true);
        Assert.Throws<DomainValidationException>(() => slot.Rename("Lunch", -1));
        Assert.Equal("Dinner", slot.Name); Assert.Equal(2, slot.Position); Assert.Equal(1, slot.Revision);
        var date = new DateOnly(2026, 11, 2);
        var meal = new ScheduledMeal(Guid.NewGuid(), slot.HouseholdId, date, slot.Id, MealType.Other, "Takeaway");
        Assert.Throws<DomainValidationException>(() => meal.Move(date.AddDays(1), Guid.Empty));
        Assert.Equal(date, meal.Date); Assert.Equal(1, meal.Revision);
        Assert.Throws<DomainValidationException>(() => new ScheduledMeal(Guid.NewGuid(), slot.HouseholdId, date, slot.Id, MealType.Other));
        slot.Archive(); Assert.True(slot.Archived); Assert.True(slot.IsDefault);
    }
}
