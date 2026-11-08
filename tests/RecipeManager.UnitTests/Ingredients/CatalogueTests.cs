using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Ingredients;

public sealed class CatalogueTests
{
    [Theory]
    [InlineData("  Red   Pepper ", "RED PEPPER")]
    [InlineData("red\tpepper", "RED PEPPER")]
    [InlineData("Ｒｅｄ pepper", "RED PEPPER")]
    public void Catalogue_lookup_normalizes_case_unicode_and_whitespace(string name, string normalized)
        => Assert.Equal(normalized, CatalogueNames.Normalize(name));

    [Fact]
    public void Canonical_correction_and_category_assignment_preserve_ingredient_identity()
    {
        var id = Guid.NewGuid(); var household = Guid.NewGuid(); var category = Guid.NewGuid();
        var ingredient = new Ingredient(id, household, "Red peper");
        ingredient.Update("Red pepper", category);
        Assert.Equal(id, ingredient.Id); Assert.Equal(household, ingredient.HouseholdId);
        Assert.Equal("RED PEPPER", ingredient.NormalizedName); Assert.Equal(category, ingredient.CategoryId);
        Assert.Equal(2, ingredient.Revision);
        Assert.NotEqual(CatalogueNames.Normalize("Beef mince"), CatalogueNames.Normalize("Beef strips"));
    }

    [Fact]
    public void Required_catalogue_names_and_nonnegative_category_order_are_enforced()
    {
        Assert.Throws<DomainValidationException>(() => new Ingredient(Guid.NewGuid(), Guid.NewGuid(), " "));
        Assert.Throws<DomainValidationException>(() => new IngredientAlias(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 201)));
        Assert.Throws<DomainValidationException>(() => new IngredientCategory(Guid.NewGuid(), Guid.NewGuid(), "Produce", -1));
    }
}
