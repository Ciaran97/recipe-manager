using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Shopping;

public static class ShoppingRequirements
{
    public static ShoppingCalculation Calculate(IEnumerable<ShoppingCalculationSource> sources, ShoppingBasis basis)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!Enum.IsDefined(basis)) throw new DomainValidationException("basis", "unsupported", "Choose planned or actual cooking quantities.");
        var input = sources.Take(IngredientCalculator.MaximumBatchLines + 1).ToArray();
        if (input.Length > IngredientCalculator.MaximumBatchLines || input.Any(x => x is null || !Enum.IsDefined(x.Type)))
            throw new DomainValidationException("selection", "calculation_limit", "Select at most 1000 valid sources in one calculation.");
        var ordered = input.Where(x => x.Type == MealType.Cook).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        if (ordered.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 200) || ordered.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new DomainValidationException("selection", "duplicate_source", "Each selected recipe or cooking event needs one stable source identity.");
        IngredientCalculator.ValidateBatchSize(ordered.Sum(x => x.Recipe?.Ingredients.Count ?? 0));
        var rows = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var source in ordered)
        {
            var recipe = source.Recipe ?? throw new DomainValidationException("selection", "recipe_required", "Cooking requirements need their saved recipe snapshot.");
            if (recipe.SchemaVersion != 1 || recipe.PolicyVersion != IngredientCalculator.PolicyVersion)
                throw new DomainValidationException("selection", "unsupported_policy", "This recipe snapshot requires a supported calculation policy.");
            var servings = basis == ShoppingBasis.Planned ? source.PlannedPrepared
                : source.ActualPrepared ?? throw new DomainValidationException("basis", "actuals_required", "Confirm actual preparation for every selected cooking event before choosing actual quantities.");
            PortionQuantity.Validate(servings); PortionQuantity.Validate(recipe.DefaultServings);
            var scale = ExactAmount.FromDecimal(servings) / ExactAmount.FromDecimal(recipe.DefaultServings);
            foreach (var ingredient in recipe.Ingredients.OrderBy(x => x.Position).ThenBy(x => x.Id))
            {
                DomainRules.NonEmptyId(ingredient.IngredientId, "ingredientId");
                var unresolved = ingredient.Kind == QuantityKind.Unknown;
                ExactAmount? amount = null;
                if (!unresolved)
                {
                    if (ingredient.Kind != QuantityKind.Numeric || ingredient.Exact is null || !Enum.IsDefined(ingredient.Dimension))
                        throw new DomainValidationException("selection", "invalid_snapshot", "Numeric snapshot ingredients need their exact quantity and dimension.");
                    var factor = ingredient.CanonicalFactor.ToAmount();
                    if (factor.Numerator.IsZero) throw new DomainValidationException("selection", "invalid_snapshot", "Snapshot unit factors must be positive.");
                    amount = ingredient.Exact.ToAmount() * scale * factor;
                }
                var key = unresolved ? $"unknown:{source.Key}:{ingredient.Id:N}"
                    : $"known:{ingredient.IngredientId:N}:{ingredient.Dimension}";
                var contribution = new ShoppingContribution(source.Key, source.Label, recipe.RecipeId, recipe.RecipeRevision, servings, ingredient.Id, ingredient.QuantityText,
                    ingredient.Unit, ingredient.Preparation, ingredient.Uncertain, amount is null ? null : ExactQuantity.From(amount));
                if (!rows.TryGetValue(key, out var row)) rows.Add(key, row = new Row(ingredient, unresolved));
                row.Add(amount, contribution);
            }
        }
        var unknownIdentities = rows.Values.Where(x => x.Unresolved).Select(x => x.Ingredient.IngredientId).ToHashSet();
        var result = rows.OrderBy(x => x.Key, StringComparer.Ordinal).Select(pair => pair.Value.Read(pair.Key, unknownIdentities)).ToArray();
        return new ShoppingCalculation(Array.AsReadOnly(result), result.Count(x => x.Unresolved));
    }

    private sealed class Row(SnapshotIngredient ingredient, bool unresolved)
    {
        public SnapshotIngredient Ingredient { get; } = ingredient;
        public bool Unresolved { get; } = unresolved;
        private ExactAmount _total = new(0, 1);
        private readonly List<ShoppingContribution> _contributions = [];
        public void Add(ExactAmount? amount, ShoppingContribution contribution)
        {
            if (amount is not null) _total += amount;
            _contributions.Add(contribution);
        }
        public ShoppingRequirement Read(string key, HashSet<Guid> unknownIdentities)
        {
            var display = Unresolved ? null : IngredientCalculator.Display(_total, Ingredient.Dimension);
            return new(key, Ingredient.IngredientId, Ingredient.IngredientName, Ingredient.CategoryId, Ingredient.CategoryName,
                Unresolved ? null : Ingredient.Dimension, Unresolved ? null : ExactQuantity.From(_total),
                Unresolved ? (string.IsNullOrWhiteSpace(Ingredient.QuantityText) ? "Quantity not specified" : Ingredient.QuantityText) : display!.Text,
                display?.IsApproximate ?? false, Unresolved, !Unresolved && unknownIdentities.Contains(Ingredient.IngredientId),
                Array.AsReadOnly(_contributions.ToArray()));
        }
    }
}
