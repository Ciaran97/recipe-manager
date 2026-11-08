using System.Text;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Ingredients;

public static class CatalogueNames
{
    public static string Normalize(string value)
    {
        var normalized = string.Join(' ', DomainRules.Name(value).Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        return DomainRules.Name(normalized);
    }
}

public sealed class Ingredient
{
    private Ingredient() { }
    public Ingredient(Guid id, Guid householdId, string canonicalName, Guid? categoryId = null)
    {
        Id = DomainRules.NonEmptyId(id, "id"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        CanonicalName = DomainRules.Name(canonicalName, "canonicalName"); NormalizedName = CatalogueNames.Normalize(CanonicalName);
        CategoryId = categoryId is { } category ? DomainRules.NonEmptyId(category, "categoryId") : null;
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string CanonicalName { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public Guid? CategoryId { get; private set; }
    public long Revision { get; private set; } = 1;
    public void Update(string canonicalName, Guid? categoryId)
    {
        var name = DomainRules.Name(canonicalName, "canonicalName");
        var normalized = CatalogueNames.Normalize(name);
        var category = categoryId is { } value ? DomainRules.NonEmptyId(value, "categoryId") : (Guid?)null;
        CanonicalName = name; NormalizedName = normalized; CategoryId = category;
        Advance();
    }
    public void Advance() => Revision = checked(Revision + 1);
}

public sealed class IngredientAlias
{
    private IngredientAlias() { }
    public IngredientAlias(Guid id, Guid householdId, Guid ingredientId, string name)
    {
        Id = DomainRules.NonEmptyId(id, "aliasId"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        IngredientId = DomainRules.NonEmptyId(ingredientId, "ingredientId");
        Name = DomainRules.Name(name, "name"); NormalizedName = CatalogueNames.Normalize(Name);
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid IngredientId { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public void Rename(string name)
    {
        var display = DomainRules.Name(name); var normalized = CatalogueNames.Normalize(display);
        Name = display; NormalizedName = normalized;
    }
}

/// <summary>Single namespace for canonical and alias lookup; changes join the ingredient transaction.</summary>
public sealed class IngredientName
{
    private IngredientName() { }
    public IngredientName(Guid householdId, Guid ingredientId, string name, bool isCanonical)
    {
        HouseholdId = DomainRules.NonEmptyId(householdId, "householdId"); IngredientId = DomainRules.NonEmptyId(ingredientId, "ingredientId");
        NormalizedName = CatalogueNames.Normalize(name); IsCanonical = isCanonical;
    }
    public Guid HouseholdId { get; private set; }
    public Guid IngredientId { get; private set; }
    public string NormalizedName { get; private set; } = null!;
    public bool IsCanonical { get; private set; }
}

public sealed class IngredientCategory
{
    private IngredientCategory() { }
    public IngredientCategory(Guid id, Guid householdId, string name, int sortOrder)
    {
        Id = DomainRules.NonEmptyId(id, "id"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        Set(name, sortOrder);
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public long Revision { get; private set; } = 1;
    public void Update(string name, int sortOrder) { Set(name, sortOrder); Revision = checked(Revision + 1); }
    private void Set(string name, int sortOrder)
    {
        if (sortOrder < 0) throw new DomainValidationException("sortOrder", "range", "Sort order must be nonnegative.");
        var display = DomainRules.Name(name); var normalized = CatalogueNames.Normalize(display);
        Name = display; NormalizedName = normalized; SortOrder = sortOrder;
    }
}

public sealed class CatalogueConflictException(string code) : Exception
{
    public string Code { get; } = code;
}
