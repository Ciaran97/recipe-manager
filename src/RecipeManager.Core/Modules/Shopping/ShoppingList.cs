using System.Text.Json;
using System.Text.Json.Serialization;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Shopping;

public static class ShoppingJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidOperationException("Shopping snapshot is missing.");
}

public sealed class ShoppingList
{
    private ShoppingList() { }
    public ShoppingList(Guid id, Guid householdId, string name, ShoppingSelection? selection, ShoppingBasis basis,
        ShoppingCalculation baseline, IReadOnlyList<ShoppingSourceRevision> sourceRevisions, long householdSourceRevision)
    {
        Id = DomainRules.NonEmptyId(id, "listId"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        Name = DomainRules.Name(name, "name"); SetBaseline(selection, basis, baseline, sourceRevisions, householdSourceRevision);
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = null!;
    public long Revision { get; private set; } = 1;
    public ShoppingBasis Basis { get; private set; }
    public int PolicyVersion { get; private set; } = IngredientCalculator.PolicyVersion;
    public string? SelectionJson { get; private set; }
    public string BaselineJson { get; private set; } = null!;
    public string SourceRevisionsJson { get; private set; } = null!;
    public long HouseholdSourceRevision { get; private set; }
    public ShoppingSelection? Selection => SelectionJson is null ? null : ShoppingJson.Decode<ShoppingSelection>(SelectionJson);
    public ShoppingCalculation Baseline => ShoppingJson.Decode<ShoppingCalculation>(BaselineJson);
    public IReadOnlyList<ShoppingSourceRevision> SourceRevisions => ShoppingJson.Decode<ShoppingSourceRevision[]>(SourceRevisionsJson);
    public void Rename(string name) { Name = DomainRules.Name(name, "name"); Advance(); }
    public void Advance() => Revision = checked(Revision + 1);
    public void Rebase(ShoppingSelection? selection, ShoppingBasis basis, ShoppingCalculation baseline,
        IReadOnlyList<ShoppingSourceRevision> sourceRevisions, long householdSourceRevision)
    {
        SetBaseline(selection, basis, baseline, sourceRevisions, householdSourceRevision); Advance();
    }
    private void SetBaseline(ShoppingSelection? selection, ShoppingBasis basis, ShoppingCalculation baseline,
        IReadOnlyList<ShoppingSourceRevision> sourceRevisions, long householdSourceRevision)
    {
        if (!Enum.IsDefined(basis) || householdSourceRevision < 0 || (basis == ShoppingBasis.Actual && selection is null))
            throw new DomainValidationException("basis", "unsupported", "Use a valid shopping source basis and revision; a manual list has no actual cooking basis.");
        var selected = selection is null ? null : ShoppingJson.Encode(selection);
        var amounts = ShoppingJson.Encode(baseline); var versions = ShoppingJson.Encode(sourceRevisions);
        SelectionJson = selected; BaselineJson = amounts; SourceRevisionsJson = versions;
        Basis = basis; HouseholdSourceRevision = householdSourceRevision; PolicyVersion = IngredientCalculator.PolicyVersion;
    }
}

public sealed class ShoppingItem
{
    private ShoppingItem() { }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid ListId { get; private set; }
    public string? RequirementKey { get; private set; }
    public Guid? IngredientId { get; private set; }
    public string Label { get; private set; } = null!;
    public Guid? CategoryId { get; private set; }
    public string? CategoryName { get; private set; }
    public bool Manual { get; private set; }
    public bool Purchased { get; private set; }
    public bool Removed { get; private set; }
    public int Position { get; private set; }
    public string? GeneratedJson { get; private set; }
    public string? OverrideJson { get; private set; }
    public ShoppingRequirement? Baseline => GeneratedJson is null ? null : ShoppingJson.Decode<ShoppingRequirement>(GeneratedJson);
    public ShoppingQuantityValue? OverrideValue => OverrideJson is null ? null : ShoppingJson.Decode<ShoppingQuantityValue>(OverrideJson);
    public bool HasOverride => !Manual && OverrideJson is not null;
    public ExactQuantity? EffectiveExact => OverrideJson is not null ? OverrideValue!.Exact : Baseline?.Exact;
    public IngredientDimension? Dimension => OverrideJson is not null ? OverrideValue!.Dimension : Baseline?.Dimension;
    public bool Unresolved => OverrideJson is not null ? OverrideValue!.Kind == QuantityKind.Unknown : Baseline!.Unresolved;
    public string DisplayText => OverrideJson is not null ? OverrideValue!.DisplayText : Baseline!.DisplayText;

    public static ShoppingItem Generated(Guid id, Guid householdId, Guid listId, ShoppingRequirement requirement, int position)
    {
        var item = Create(id, householdId, listId, position); item.SetGenerated(requirement); return item;
    }
    public static ShoppingItem ManualItem(Guid id, Guid householdId, Guid listId, string label, ShoppingQuantityInput quantity, int position,
        Guid? categoryId = null, string? categoryName = null)
    {
        var value = Value(quantity);
        var item = Create(id, householdId, listId, position);
        item.Manual = true; item.Label = DomainRules.Name(label, "label"); item.OverrideJson = ShoppingJson.Encode(value);
        item.CategoryId = categoryId; item.CategoryName = categoryName; return item;
    }
    private static ShoppingItem Create(Guid id, Guid householdId, Guid listId, int position) => new()
    {
        Id = DomainRules.NonEmptyId(id, "itemId"),
        HouseholdId = DomainRules.NonEmptyId(householdId, "householdId"),
        ListId = DomainRules.NonEmptyId(listId, "listId"),
        Position = DomainRules.Position(position)
    };
    public void Override(ShoppingQuantityInput quantity)
    {
        var value = Value(quantity);
        if (!Manual && Baseline?.Dimension is { } dimension && value.Dimension is { } replacement && dimension != replacement)
            throw new DomainValidationException("quantityOverride.unit", "incompatible_unit", "Choose a compatible unit for this requirement; mass and volume cannot be guessed.");
        OverrideJson = ShoppingJson.Encode(value);
    }
    public void ClearOverride()
    {
        if (Manual) throw new DomainValidationException("quantityOverride", "manual_quantity", "Enter a quantity or unresolved amount for a manual item.");
        OverrideJson = null;
    }
    public void PreserveCurrentQuantity()
    {
        if (Manual || OverrideJson is not null) return;
        var baseline = Baseline ?? throw new InvalidOperationException("A generated item needs its baseline.");
        if (baseline.Exact is not { } exact)
        {
            OverrideJson = ShoppingJson.Encode(new ShoppingQuantityValue(baseline.DisplayText, QuantityKind.Unknown,
                baseline.Contributions.FirstOrDefault()?.Unit ?? IngredientUnit.Item, MeasurementStandard.AU, null, null, baseline.DisplayText, false));
            return;
        }
        var unit = baseline.Dimension switch
        {
            IngredientDimension.Mass => IngredientUnit.Gram,
            IngredientDimension.Volume => IngredientUnit.Millilitre,
            IngredientDimension.Count => IngredientUnit.Item,
            _ => throw new InvalidOperationException("An exact shopping baseline needs a dimension.")
        };
        OverrideJson = ShoppingJson.Encode(new ShoppingQuantityValue(exact.Numerator + "/" + exact.Denominator, QuantityKind.Numeric,
            unit, MeasurementStandard.AU, exact, baseline.Dimension, baseline.DisplayText, baseline.IsApproximate));
    }
    public void RenameManual(string label)
    {
        if (!Manual) throw new DomainValidationException("label", "generated_name", "Correct the ingredient catalogue name instead of renaming a generated requirement.");
        Label = DomainRules.Name(label, "label");
    }
    public void SetPurchased(bool purchased) => Purchased = purchased;
    public void Remove() => Removed = true;
    public void Restore() => Removed = false;
    public void SetGenerated(ShoppingRequirement requirement)
    {
        if (Manual || (RequirementKey is not null && RequirementKey != requirement.Key)) throw new InvalidOperationException("A baseline cannot change an item's identity.");
        var json = ShoppingJson.Encode(requirement);
        RequirementKey = requirement.Key; IngredientId = requirement.IngredientId; Label = requirement.Label;
        CategoryId = requirement.CategoryId; CategoryName = requirement.CategoryName; GeneratedJson = json;
    }
    public static ShoppingQuantityValue Value(ShoppingQuantityInput input)
    {
        if (!Enum.IsDefined(input.Kind) || !Enum.IsDefined(input.Unit) || !Enum.IsDefined(input.MeasurementStandard))
            throw new DomainValidationException("quantity", "unsupported", "Choose supported units, quantity kinds and measurement standards.");
        var parsed = input.Kind == QuantityKind.Numeric ? Quantity.Parse(input.QuantityText) : Quantity.Unknown(input.QuantityText);
        if (parsed.Exact is not { } exact) return new(input.QuantityText, input.Kind, input.Unit, input.MeasurementStandard, null, null,
            string.IsNullOrWhiteSpace(input.QuantityText) ? "Quantity not specified" : input.QuantityText, false);
        var canonical = IngredientCalculator.Canonical(exact, input.Unit, input.MeasurementStandard);
        var display = IngredientCalculator.Display(canonical.Amount, canonical.Dimension);
        return new(input.QuantityText, input.Kind, input.Unit, input.MeasurementStandard, ExactQuantity.From(canonical.Amount), canonical.Dimension, display.Text, display.IsApproximate);
    }
}
