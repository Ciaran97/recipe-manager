using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Recipes;

public static class RecipeRules
{
    public static RecipeWrite Validate(RecipeWrite request)
    {
        var title = DomainRules.Name(request.Title, "title");
        try { PortionQuantity.Validate(request.DefaultServings); }
        catch (DomainValidationException error) { throw new DomainValidationException("defaultServings", error.Code, error.UserMessage); }
        if (request.Groups is null || request.Ingredients is null || request.Instructions is null || request.CollectionIds is null || request.TagIds is null)
            throw new DomainValidationException("recipe", "required", "Recipe arrays must be provided, including empty arrays.");
        if (request.Groups.Any(x => x is null) || request.Ingredients.Any(x => x is null) || request.Instructions.Any(x => x is null))
            throw new DomainValidationException("recipe", "invalid_rows", "Recipe sections must not contain null rows.");
        IngredientCalculator.ValidateBatchSize(request.Ingredients.Count);
        if (request.Groups.Count > 1000 || request.Instructions.Count > 1000 || request.CollectionIds.Count > 1000 || request.TagIds.Count > 1000)
            throw new DomainValidationException("recipe", "limit", "Use at most 1000 entries in each recipe section.");
        if (request.PrepMinutes < 0 || request.CookMinutes < 0)
            throw new DomainValidationException("times", "range", "Preparation and cooking minutes must be nonnegative.");
        if (!Enum.IsDefined(request.MeasurementStandard)) throw new DomainValidationException("measurementStandard", "unsupported", "Choose AU or US measures.");
        if (request.ImageAssetId is { } image) DomainRules.NonEmptyId(image, "imageAssetId");
        UniquePositions(request.Groups.Select(x => x.Position), "groups");
        UniquePositions(request.Ingredients.Select(x => x.Position), "ingredients");
        UniquePositions(request.Instructions.Select(x => x.Position), "instructions");
        UniqueIds(request.Groups.Select(x => x.Id), "groups");
        UniqueIds(request.Ingredients.Select(x => x.Id), "ingredients");
        UniqueIds(request.Instructions.Select(x => x.Id), "instructions");
        var groups = request.Groups.Where(x => x.Id != null).Select(x => x.Id!.Value).ToHashSet();
        foreach (var group in request.Groups) _ = DomainRules.Name(group.Title, "groups.title");
        for (var index = 0; index < request.Ingredients.Count; index++)
        {
            var ingredient = request.Ingredients[index];
            DomainRules.NonEmptyId(ingredient.IngredientId, $"ingredients[{index}].ingredientId");
            if (ingredient.GroupId is { } group && !groups.Contains(group)) throw new DomainValidationException("ingredients.groupId", "invalid_group", "Ingredient groups must belong to this recipe write.");
            if (!Enum.IsDefined(ingredient.Kind) || !Enum.IsDefined(ingredient.Unit)) throw new DomainValidationException("ingredients", "unsupported", "Choose supported quantity kinds and units.");
            try { _ = ingredient.Kind == QuantityKind.Unknown ? Quantity.Unknown(ingredient.QuantityText) : Quantity.Parse(ingredient.QuantityText); }
            catch (DomainValidationException error) { throw new DomainValidationException($"ingredients[{index}].quantityText", error.Code, error.UserMessage); }
            DomainRules.Description(ingredient.Preparation, "ingredients.preparation");
            if ((ingredient.Kind == QuantityKind.Unknown || ingredient.Uncertain) && !request.AcknowledgeUncertainIngredients)
                throw new DomainValidationException("acknowledgeUncertainIngredients", "review_required", "Review and acknowledge every unresolved or uncertain ingredient before saving.");
        }
        foreach (var instruction in request.Instructions)
            if (string.IsNullOrWhiteSpace(instruction.Text)) throw new DomainValidationException("instructions.text", "required", "An instruction must contain text.");
            else _ = DomainRules.Description(instruction.Text, "instructions.text");
        foreach (var id in request.CollectionIds.Concat(request.TagIds)) DomainRules.NonEmptyId(id, "organiserId");
        return request with { Title = title, Description = DomainRules.Description(request.Description), Notes = DomainRules.Description(request.Notes, "notes"), SourceUrl = DomainRules.HttpUrl(request.SourceUrl) };
    }
    private static void UniquePositions(IEnumerable<int> positions, string field)
    {
        var values = positions.ToArray();
        if (values.Any(x => x < 0) || values.Distinct().Count() != values.Length)
            throw new DomainValidationException(field, "invalid_order", "Positions must be unique and nonnegative within each section.");
    }
    private static void UniqueIds(IEnumerable<Guid?> ids, string field)
    {
        var values = ids.Where(x => x != null).Select(x => DomainRules.NonEmptyId(x!.Value, field)).ToArray();
        if (values.Distinct().Count() != values.Length) throw new DomainValidationException(field, "duplicate_id", "Each recipe row must have a unique ID.");
    }
}

public sealed class Recipe
{
    private Recipe() { }
    public Recipe(Guid id, Guid householdId, RecipeWrite content)
    {
        Id = DomainRules.NonEmptyId(id, "id"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId"); Set(RecipeRules.Validate(content));
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public long Revision { get; private set; } = 1;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid? ImageAssetId { get; private set; }
    public decimal DefaultServings { get; private set; }
    public int? PrepMinutes { get; private set; }
    public int? CookMinutes { get; private set; }
    public string? Notes { get; private set; }
    public string? SourceUrl { get; private set; }
    public MeasurementStandard MeasurementStandard { get; private set; }
    public int PolicyVersion { get; private set; } = IngredientCalculator.PolicyVersion;
    public bool Archived { get; private set; }
    public void Replace(RecipeWrite content) { Set(RecipeRules.Validate(content)); Advance(); }
    public void SetArchived(bool archived) { Archived = archived; Advance(); }
    public void Advance() => Revision = checked(Revision + 1);
    private void Set(RecipeWrite content)
        => (Title, Description, ImageAssetId, DefaultServings, PrepMinutes, CookMinutes, Notes, SourceUrl, MeasurementStandard)
        = (content.Title, content.Description, content.ImageAssetId, content.DefaultServings, content.PrepMinutes, content.CookMinutes, content.Notes, content.SourceUrl, content.MeasurementStandard);
}

public sealed class IngredientGroup
{
    private IngredientGroup() { }
    public IngredientGroup(Guid id, Guid householdId, Guid recipeId, string title, int position)
        => (Id, HouseholdId, RecipeId, Title, Position) = (DomainRules.NonEmptyId(id, "groupId"), DomainRules.NonEmptyId(householdId, "householdId"), DomainRules.NonEmptyId(recipeId, "recipeId"), DomainRules.Name(title), DomainRules.Position(position));
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid RecipeId { get; private set; }
    public string Title { get; private set; } = null!;
    public int Position { get; private set; }
}
public sealed class RecipeIngredient
{
    private RecipeIngredient() { }
    public RecipeIngredient(Guid id, Guid householdId, Guid recipeId, RecipeIngredientWrite content)
    {
        if (!Enum.IsDefined(content.Kind) || !Enum.IsDefined(content.Unit)) throw new DomainValidationException("ingredients", "unsupported", "Choose supported quantity kinds and units.");
        Id = DomainRules.NonEmptyId(id, "rowId"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId"); RecipeId = DomainRules.NonEmptyId(recipeId, "recipeId");
        IngredientId = DomainRules.NonEmptyId(content.IngredientId, "ingredientId"); GroupId = content.GroupId is { } groupId ? DomainRules.NonEmptyId(groupId, "groupId") : null; Position = DomainRules.Position(content.Position);
        var quantity = content.Kind == QuantityKind.Unknown ? Quantity.Unknown(content.QuantityText) : Quantity.Parse(content.QuantityText);
        QuantityText = quantity.OriginalText; Kind = quantity.Kind; DecimalValue = quantity.DecimalValue;
        FractionNumerator = quantity.FractionNumerator; FractionDenominator = quantity.FractionDenominator;
        Unit = content.Unit; Preparation = DomainRules.Description(content.Preparation, "preparation"); Uncertain = content.Uncertain;
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid RecipeId { get; private set; }
    public Guid IngredientId { get; private set; }
    public Guid? GroupId { get; private set; }
    public int Position { get; private set; }
    public string QuantityText { get; private set; } = null!;
    public QuantityKind Kind { get; private set; }
    public decimal? DecimalValue { get; private set; }
    public long? FractionNumerator { get; private set; }
    public long? FractionDenominator { get; private set; }
    public IngredientUnit Unit { get; private set; }
    public string? Preparation { get; private set; }
    public bool Uncertain { get; private set; }
    public Quantity Quantity => Kind == QuantityKind.Unknown ? Quantity.Unknown(QuantityText) : Quantity.Parse(QuantityText);
}
public sealed class Instruction
{
    private Instruction() { }
    public Instruction(Guid id, Guid householdId, Guid recipeId, int position, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new DomainValidationException("instructions.text", "required", "An instruction must contain text.");
        (Id, HouseholdId, RecipeId, Position, Text) = (DomainRules.NonEmptyId(id, "instructionId"), DomainRules.NonEmptyId(householdId, "householdId"), DomainRules.NonEmptyId(recipeId, "recipeId"), DomainRules.Position(position), DomainRules.Description(text, "instructions.text")!);
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid RecipeId { get; private set; }
    public int Position { get; private set; }
    public string Text { get; private set; } = null!;
}
public sealed class Favourite
{
    private Favourite() { }
    public Favourite(Guid householdId, Guid userId, Guid recipeId) => (HouseholdId, UserId, RecipeId) = (DomainRules.NonEmptyId(householdId, "householdId"), DomainRules.NonEmptyId(userId, "userId"), DomainRules.NonEmptyId(recipeId, "recipeId"));
    public Guid HouseholdId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RecipeId { get; private set; }
}
public sealed class ImageAsset
{
    private ImageAsset() { }
    public ImageAsset(Guid id, Guid householdId, string storageKey, string mediaType, long size)
    {
        if (!Guid.TryParseExact(storageKey, "N", out _) || storageKey != storageKey.ToLowerInvariant())
            throw new DomainValidationException("storageKey", "invalid", "Image storage keys must be generated UUIDs.");
        if (mediaType is not ("image/jpeg" or "image/png" or "image/webp") || size <= 0)
            throw new DomainValidationException("image", "invalid", "A validated image and positive size are required.");
        (Id, HouseholdId, StorageKey, MediaType, Size) = (DomainRules.NonEmptyId(id, "imageAssetId"), DomainRules.NonEmptyId(householdId, "householdId"), storageKey, mediaType, size);
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public string MediaType { get; private set; } = null!;
    public long Size { get; private set; }
}
