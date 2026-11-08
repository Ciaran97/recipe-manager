using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.MealPlanning;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Recipes;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.MealPlanning;

public sealed class RecipeSnapshotPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Frozen_source_keeps_original_ingredient_and_image_references_until_explicit_refresh()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var household = new Household(Guid.NewGuid(), "Frozen recipe", "UTC");
        var ingredient = new Ingredient(Guid.NewGuid(), household.Id, "Rice");
        var image = new ImageAsset(Guid.NewGuid(), household.Id, Guid.NewGuid().ToString("N"), "image/jpeg", 100);
        var content = new RecipeWrite("Original rice", 3m, [], [new RecipeIngredientWrite(0, ingredient.Id, "1/3", QuantityKind.Numeric, IngredientUnit.Cup)], [], [], [], ImageAssetId: image.Id);
        var recipe = new Recipe(Guid.NewGuid(), household.Id, content);
        var row = new RecipeIngredient(Guid.NewGuid(), household.Id, recipe.Id, content.Ingredients[0]);
        var slot = new MealSlot(Guid.NewGuid(), household.Id, "Dinner", 2);
        var meal = new ScheduledMeal(Guid.NewGuid(), household.Id, new DateOnly(2026, 11, 2), slot.Id, MealType.Cook);
        db.AddRange(household, ingredient, image, recipe, row, slot, meal); await db.SaveChangesAsync(); db.BindHouseholdScope(household.Id);
        var reader = new RecipeSnapshotReader(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadWithinTransactionAsync(household.Id, recipe.Id, default));
        CookingEvent cooking;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var read = await reader.ReadWithinTransactionAsync(household.Id, recipe.Id, default);
            Assert.False(read.Favourite);
            cooking = new CookingEvent(Guid.NewGuid(), household.Id, meal.Id, recipe.Id, recipe.Revision,
                RecipeSnapshotFactory.Serialize(RecipeSnapshotFactory.Create(read)), 4m, 2m);
            db.Add(cooking); await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        var frozen = cooking.RecipeSnapshotJson; var sourceRevision = cooking.Revision;
        recipe.Replace(content with { Title = "Edited rice", ImageAssetId = null, Ingredients = [] });
        ingredient.Update("Renamed ingredient", null);
        await db.RecipeIngredients.Where(x => x.Id == row.Id).ExecuteDeleteAsync(); await db.SaveChangesAsync();
        var references = new PlannerReferences(db);
        await references.IngredientChangedAsync(household.Id, ingredient.Id, default);
        Assert.True(await references.UsesIngredientAsync(household.Id, ingredient.Id, default));
        Assert.True(await references.UsesImageAsync(household.Id, image.Id, default));
        Assert.True(await references.UsesRecipeAsync(household.Id, recipe.Id, default));
        Assert.False(await references.UsesImageAsync(Guid.NewGuid(), image.Id, default));
        Assert.Equal(frozen, cooking.RecipeSnapshotJson); Assert.Equal(sourceRevision, cooking.Revision);
        var saved = RecipeSnapshotFactory.Deserialize(frozen);
        Assert.Equal("Original rice", saved.Title); Assert.Equal("Rice", saved.Ingredients[0].IngredientName);
        Assert.Equal(image.Id, saved.ImageAssetId); Assert.Equal("1/3", saved.Ingredients[0].QuantityText);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var latest = await reader.ReadWithinTransactionAsync(household.Id, recipe.Id, default);
            var preview = RefreshRecipeSnapshot.Preview(cooking, latest);
            Assert.Equal(frozen, cooking.RecipeSnapshotJson);
            RefreshRecipeSnapshot.Apply(cooking, latest, preview.EventRevision, preview.RecipeRevision);
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        Assert.False(await references.UsesIngredientAsync(household.Id, ingredient.Id, default));
        Assert.False(await references.UsesImageAsync(household.Id, image.Id, default));
        Assert.True(await references.UsesRecipeAsync(household.Id, recipe.Id, default));
        Assert.Equal("Edited rice", RecipeSnapshotFactory.Deserialize(cooking.RecipeSnapshotJson).Title);
        Assert.Equal(4m, cooking.PlannedPrepared); Assert.Equal(2m, cooking.PlannedEaten);
    }
}
