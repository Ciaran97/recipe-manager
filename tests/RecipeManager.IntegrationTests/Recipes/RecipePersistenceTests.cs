using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Recipes;

public sealed class RecipePersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private RecipeManagerDbContext Open() => new(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
    private static RecipeWrite Content(Guid ingredientId) => new("Rice", 3m, [],
        [new RecipeIngredientWrite(0, ingredientId, "1/3", QuantityKind.Numeric, IngredientUnit.Gram)], [], [], []);

    [Fact]
    public async Task Recipe_storage_preserves_fraction_fields_and_rejects_inconsistent_and_foreign_rows()
    {
        await using var db = Open(); await db.Database.MigrateAsync();
        var first = new Household(Guid.NewGuid(), "First", "UTC"); var second = new Household(Guid.NewGuid(), "Second", "UTC");
        var ingredient = new Ingredient(Guid.NewGuid(), first.Id, "Rice"); var foreign = new Ingredient(Guid.NewGuid(), second.Id, "Salt");
        var recipe = new Recipe(Guid.NewGuid(), first.Id, Content(ingredient.Id));
        var row = new RecipeIngredient(Guid.NewGuid(), first.Id, recipe.Id, Content(ingredient.Id).Ingredients[0]);
        db.AddRange(first, second, ingredient, foreign, recipe, row); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Empty(await db.Recipes.ToArrayAsync()); Assert.Empty(await db.RecipeIngredients.ToArrayAsync());
        db.BindHouseholdScope(first.Id);
        var saved = Assert.Single(await db.RecipeIngredients.ToArrayAsync());
        Assert.Equal("1/3", saved.QuantityText); Assert.Null(saved.DecimalValue); Assert.Equal(1, saved.FractionNumerator); Assert.Equal(3, saved.FractionDenominator);
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE recipes.\"RecipeIngredients\" SET \"DecimalValue\" = 1 WHERE \"HouseholdId\" = {first.Id} AND \"Id\" = {row.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
        db.ChangeTracker.Clear();
        db.RecipeIngredients.Add(new RecipeIngredient(Guid.NewGuid(), first.Id, recipe.Id,
            Content(foreign.Id).Ingredients[0] with { Position = 1 }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>((await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException).SqlState);
        db.ChangeTracker.Clear(); db.BindHouseholdScope(second.Id);
        Assert.Empty(await db.Recipes.ToListAsync()); Assert.Empty(await db.RecipeIngredients.ToListAsync());
    }

    [Fact]
    public async Task Root_deletion_cascades_only_owned_recipe_content_and_preserves_catalogue()
    {
        await using var db = Open(); await db.Database.MigrateAsync();
        var household = new Household(Guid.NewGuid(), "Owned recipe", "UTC");
        var ingredient = new Ingredient(Guid.NewGuid(), household.Id, "Rice");
        var group = new IngredientGroup(Guid.NewGuid(), household.Id, Guid.NewGuid(), "Base", 0);
        var content = Content(ingredient.Id) with
        {
            Groups = [new RecipeGroupWrite("Base", 0, group.Id)],
            Ingredients = [new RecipeIngredientWrite(0, ingredient.Id, "1", QuantityKind.Numeric, IngredientUnit.Gram, GroupId: group.Id)]
        };
        var recipe = new Recipe(group.RecipeId, household.Id, content);
        db.AddRange(household, ingredient, recipe, group, new RecipeIngredient(Guid.NewGuid(), household.Id, recipe.Id, content.Ingredients[0]),
            new Instruction(Guid.NewGuid(), household.Id, recipe.Id, 0, "Cook."));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); db.BindHouseholdScope(household.Id);
        await db.Recipes.Where(x => x.Id == recipe.Id).ExecuteDeleteAsync();
        Assert.Empty(await db.RecipeIngredients.ToListAsync()); Assert.Empty(await db.IngredientGroups.ToListAsync()); Assert.Empty(await db.Instructions.ToListAsync());
        Assert.Single(await db.Ingredients.ToListAsync());
    }
}
