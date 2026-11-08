using System.Text.Json.Serialization;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients.Contracts;

namespace RecipeManager.Core.Modules.Shopping.Contracts;

public sealed record ShoppingGenerateWrite(string Name, ShoppingSelection Selection, ShoppingBasis Basis = ShoppingBasis.Planned);
public sealed record ShoppingListWrite(string Name);
public sealed record ShoppingManualItemWrite(string Label, string QuantityText = "", QuantityKind Kind = QuantityKind.Unknown,
    IngredientUnit Unit = IngredientUnit.Item, MeasurementStandard MeasurementStandard = MeasurementStandard.AU, Guid? CategoryId = null);
public sealed record ShoppingItemWrite(ShoppingQuantityInput? QuantityOverride = null, bool ClearOverride = false,
    bool? Purchased = null, bool? Removed = null, string? Label = null);
public sealed record ShoppingKnownTotal(Guid ItemId, Guid? IngredientId, string Label, IngredientDimension Dimension, ExactQuantity Exact, bool Purchased);
public sealed record ShoppingSummary(int KnownItemCount, int UnresolvedCount, int PurchasedCount, int RemovedCount, IReadOnlyList<ShoppingKnownTotal> KnownTotals);
public sealed record ShoppingItemRead(Guid Id, string? Key, Guid? IngredientId, string Label, Guid? CategoryId, string? CategoryName,
    IngredientDimension? Dimension, ExactQuantity? BaselineExact, ExactQuantity? EffectiveExact, ShoppingQuantityValue? QuantityOverride,
    bool HasOverride, string DisplayText, bool IsApproximate, bool Unresolved, bool AdditionalAmountUnresolved,
    bool Purchased, bool Manual, bool Removed, int Position, IReadOnlyList<ShoppingContribution> Contributions);
public sealed record ShoppingListRead(Guid Id, long Revision, string Name, ShoppingSelection? Selection, ShoppingBasis Basis,
    int PolicyVersion, long HouseholdSourceRevision, IReadOnlyList<ShoppingSourceRevision> SourceRevisions,
    IReadOnlyList<ShoppingItemRead> Items, ShoppingSummary Summary);
public sealed record ShoppingListBrief(Guid Id, long Revision, string Name, ShoppingBasis Basis, ShoppingSummary Summary);
public sealed record ShoppingListPage(IReadOnlyList<ShoppingListBrief> Items, Guid? NextId, long HouseholdRevision);
public enum ReconciliationChoice
{
    [JsonStringEnumMemberName("acceptNew")] AcceptNew,
    [JsonStringEnumMemberName("keepUser")] KeepUser,
    [JsonStringEnumMemberName("delete")] Delete
}
public sealed record ShoppingPreviewWrite(ShoppingSelection? Selection = null, ShoppingBasis? Basis = null);
public sealed record ShoppingReconciliationChoice(string Key, ReconciliationChoice Choice, bool? Purchased = null);
public sealed record ShoppingChange(string Key, ShoppingRequirement? OldBaseline, ShoppingRequirement? NewBaseline,
    ShoppingItemRead? UserState, bool Conflict, bool PurchaseDecisionRequired, ReconciliationChoice DefaultChoice, IReadOnlyList<ReconciliationChoice> AllowedChoices);
public sealed record ShoppingPreview(long ListRevision, long HouseholdSourceRevision, IReadOnlyList<ShoppingSourceRevision> SourceRevisions,
    int PolicyVersion, ShoppingSelection? Selection, ShoppingBasis Basis, IReadOnlyList<ShoppingChange> Changes,
    IReadOnlyList<ShoppingItemRead> ManualItems);
public sealed record ShoppingApplyWrite(long ListRevision, long HouseholdSourceRevision, IReadOnlyList<ShoppingSourceRevision> SourceRevisions,
    int PolicyVersion, ShoppingSelection? Selection, ShoppingBasis Basis, IReadOnlyList<ShoppingReconciliationChoice> Choices);
public interface IShoppingStore
{
    Task<ShoppingListPage> ListAsync(RequestActor actor, Guid? cursor, int limit, CancellationToken ct);
    Task<ShoppingListRead> ReadAsync(RequestActor actor, Guid id, CancellationToken ct);
    Task<ShoppingListRead> CreateAsync(RequestActor actor, ShoppingListWrite request, Guid? retryKey, CancellationToken ct);
    Task<ShoppingListRead> GenerateAsync(RequestActor actor, ShoppingGenerateWrite request, Guid? retryKey, CancellationToken ct);
    Task<ShoppingListRead> RenameAsync(RequestActor actor, Guid id, long expected, ShoppingListWrite request, CancellationToken ct);
    Task DeleteAsync(RequestActor actor, Guid id, long expected, CancellationToken ct);
    Task<ShoppingListRead> AddItemAsync(RequestActor actor, Guid id, long expected, ShoppingManualItemWrite request, Guid? retryKey, CancellationToken ct);
    Task<ShoppingListRead> EditItemAsync(RequestActor actor, Guid id, Guid itemId, long expected, ShoppingItemWrite request, CancellationToken ct);
    Task<ShoppingListRead> RemoveItemAsync(RequestActor actor, Guid id, Guid itemId, long expected, CancellationToken ct);
    Task<ShoppingPreview> PreviewAsync(RequestActor actor, Guid id, long expected, ShoppingPreviewWrite request, CancellationToken ct);
    Task<ShoppingListRead> ApplyAsync(RequestActor actor, Guid id, long expected, ShoppingApplyWrite request, CancellationToken ct);
}
