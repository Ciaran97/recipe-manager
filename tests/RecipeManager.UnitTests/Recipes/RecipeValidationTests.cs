using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Recipes;

public sealed class RecipeValidationTests
{
    internal static RecipeWrite Content(Guid ingredientId) => new("Rice bowls", 3m, [],
        [new RecipeIngredientWrite(0, ingredientId, "1/3", QuantityKind.Numeric, IngredientUnit.Gram)],
        [new RecipeInstructionWrite(0, "Cook the rice.")], [], []);

    [Fact]
    public void Recipe_requires_review_of_unknowns_and_uncertainty_without_inventing_quantities()
    {
        var request = Content(Guid.NewGuid());
        var unresolved = request with { Ingredients = [request.Ingredients[0] with { Kind = QuantityKind.Unknown, QuantityText = "to taste" }] };
        Assert.Throws<DomainValidationException>(() => RecipeRules.Validate(unresolved));
        var accepted = RecipeRules.Validate(unresolved with { AcknowledgeUncertainIngredients = true });
        Assert.Equal("to taste", accepted.Ingredients[0].QuantityText);
        Assert.Throws<DomainValidationException>(() => RecipeRules.Validate(request with { Ingredients = [request.Ingredients[0] with { Uncertain = true }] }));
    }

    [Fact]
    public void Invalid_order_foreign_groups_ids_standards_and_bounds_are_rejected()
    {
        var request = Content(Guid.NewGuid()); var line = request.Ingredients[0];
        foreach (var invalid in new[]
        {
            request with { Title = " " }, request with { DefaultServings = 0 }, request with { DefaultServings = 0.0000001m },
            request with { PrepMinutes = -1 }, request with { MeasurementStandard = (MeasurementStandard)99 },
            request with { SourceUrl = "file:///secret" }, request with { ImageAssetId = Guid.Empty },
            request with { Ingredients = [null!] }, request with { Ingredients = [line, line] }, request with { Ingredients = [line with { GroupId = Guid.NewGuid() }] },
            request with { Instructions = [new RecipeInstructionWrite(0, " ")] }
        }) Assert.Throws<DomainValidationException>(() => RecipeRules.Validate(invalid));
    }

    [Fact]
    public void Rejected_replacement_preserves_original_recipe_and_archive_has_its_own_revision()
    {
        var request = Content(Guid.NewGuid()); var recipe = new Recipe(Guid.NewGuid(), Guid.NewGuid(), request);
        Assert.Throws<DomainValidationException>(() => recipe.Replace(request with { Title = "New title", DefaultServings = 0 }));
        Assert.Equal("Rice bowls", recipe.Title); Assert.Equal(1, recipe.Revision);
        recipe.SetArchived(true); Assert.True(recipe.Archived); Assert.Equal(2, recipe.Revision);
        recipe.SetArchived(false); Assert.False(recipe.Archived); Assert.Equal(3, recipe.Revision);
    }
}
