using System.Text.Json.Serialization;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Leftovers.Contracts;

namespace RecipeManager.Core.Modules.MealPlanning.Contracts;

public sealed record MealSlotWrite(string Name, int Position);
public sealed record MealSlotRead(Guid Id, string Name, int Position, bool IsDefault, bool Archived, long Revision);
public sealed record MealSlotPage(IReadOnlyList<MealSlotRead> Items, long Revision);
public sealed record CookingEventRead(Guid Id, long Revision, Guid SourceRecipeId, long RecipeRevision, RecipeSnapshot RecipeSnapshot,
    decimal PlannedPrepared, decimal PlannedEaten, decimal? ActualPrepared, decimal? ActualEaten, DateTimeOffset? ConfirmedAt,
    string AvailabilityBasis, decimal AvailablePortions);
public sealed record ScheduledMealRead(Guid Id, long Revision, DateOnly Date, Guid SlotId, MealType Type, string? Label,
    CookingEventRead? CookingEvent, LeftoverRead? Leftovers);
public sealed record MealCalendarRead(DateOnly From, DateOnly To, IReadOnlyList<ScheduledMealRead> Items, IReadOnlyList<MealSlotRead> Slots, long Revision);
public sealed record MealVersion(long Revision, long? SourceRevision);
public sealed record CookingWrite(Guid RecipeId, DateOnly Date, Guid SlotId, decimal PlannedPrepared, decimal PlannedEaten, long? ExpectedRecipeRevision = null);
public sealed record OtherMealWrite(DateOnly Date, Guid SlotId, string Label);
public sealed record MealPositionWrite(DateOnly Date, Guid SlotId, long? SourceRevision = null);
public sealed record DuplicateMealWrite(DateOnly Date, Guid SlotId, long? SourceRevision = null);
public sealed record DeleteMealWrite(long? SourceRevision = null, bool ConfirmConsumedRemoval = false);
public sealed record CookingActuals(decimal ActualPrepared, decimal ActualEaten);
public sealed record ActualsWrite(decimal ActualPrepared, decimal ActualEaten, IReadOnlyList<DependentResolution>? DependentResolutions = null);
public enum RefreshMode { [JsonStringEnumMemberName("preview")] Preview, [JsonStringEnumMemberName("apply")] Apply }
public sealed record RecipeRefreshWrite(RefreshMode Mode, long ExpectedRecipeRevision, long? ExpectedEventRevision = null);
public sealed record RecipeRefreshResult(RefreshMode Mode, SnapshotRefreshPreview? Preview, ScheduledMealRead? Meal);
public sealed record ClearActualsWrite(IReadOnlyList<DependentResolution>? DependentResolutions = null);
public enum SourceResolutionKind { [JsonStringEnumMemberName("plan")] Plan, [JsonStringEnumMemberName("actuals")] Actuals, [JsonStringEnumMemberName("clearActuals")] ClearActuals, [JsonStringEnumMemberName("move")] Move, [JsonStringEnumMemberName("delete")] Delete }
public enum DependentAction { [JsonStringEnumMemberName("edit")] Edit, [JsonStringEnumMemberName("move")] Move, [JsonStringEnumMemberName("release")] Release, [JsonStringEnumMemberName("delete")] Delete, [JsonStringEnumMemberName("reassign")] Reassign }
public sealed record ExpectedSourceVersion(Guid SourceCookingEventId, long Revision);
public sealed record DependentResolution(Guid MealId, long MealRevision, DependentAction Action,
    decimal? PlannedPortions = null, decimal? ConsumedPortions = null, decimal? ReservedRemainder = null,
    DateOnly? Date = null, Guid? SlotId = null, Guid? SourceCookingEventId = null, bool ConfirmConsumedRemoval = false,
    bool ClearConsumption = false);
public sealed record SourceResolutionWrite(SourceResolutionKind Kind, IReadOnlyList<ExpectedSourceVersion> ExpectedSources,
    IReadOnlyList<DependentResolution> Dependents, decimal? PlannedPrepared = null, decimal? PlannedEaten = null,
    decimal? ActualPrepared = null, decimal? ActualEaten = null, DateOnly? Date = null, Guid? SlotId = null,
    Guid? RecipeId = null, long? ExpectedRecipeRevision = null);

public interface IPlannerStore
{
    Task<MealSlotRead> SlotAsync(RequestActor actor, Guid id, CancellationToken ct);
    Task<MealSlotPage> SlotsAsync(RequestActor actor, CancellationToken ct);
    Task<MealSlotRead> CreateSlotAsync(RequestActor actor, MealSlotWrite request, Guid? retryKey, CancellationToken ct);
    Task<MealSlotRead> UpdateSlotAsync(RequestActor actor, Guid id, long expected, MealSlotWrite request, CancellationToken ct);
    Task DeleteSlotAsync(RequestActor actor, Guid id, long expected, CancellationToken ct);
    Task<MealCalendarRead> CalendarAsync(RequestActor actor, DateOnly from, DateOnly to, CancellationToken ct);
    Task<ScheduledMealRead> ReadAsync(RequestActor actor, Guid id, CancellationToken ct);
    Task<ScheduledMealRead> CreateCookingAsync(RequestActor actor, CookingWrite request, Guid? retryKey, CancellationToken ct);
    Task<ScheduledMealRead> UpdateCookingAsync(RequestActor actor, Guid id, MealVersion expected, CookingWrite request, CancellationToken ct);
    Task<ScheduledMealRead> CreateOtherAsync(RequestActor actor, OtherMealWrite request, Guid? retryKey, CancellationToken ct);
    Task<ScheduledMealRead> UpdateOtherAsync(RequestActor actor, Guid id, MealVersion expected, OtherMealWrite request, CancellationToken ct);
    Task<ScheduledMealRead> CreateLeftoversAsync(RequestActor actor, LeftoverWrite request, Guid? retryKey, CancellationToken ct);
    Task<ScheduledMealRead> UpdateLeftoversAsync(RequestActor actor, Guid id, MealVersion expected, LeftoverWrite request, CancellationToken ct);
    Task<ScheduledMealRead> MoveAsync(RequestActor actor, Guid id, MealVersion expected, MealPositionWrite request, CancellationToken ct);
    Task<ScheduledMealRead> DuplicateAsync(RequestActor actor, Guid id, MealVersion expected, DuplicateMealWrite request, Guid? retryKey, CancellationToken ct);
    Task DeleteAsync(RequestActor actor, Guid id, MealVersion expected, DeleteMealWrite request, CancellationToken ct);
    Task<LeftoversAvailability> AvailabilityAsync(RequestActor actor, Guid id, CancellationToken ct);
    Task<ScheduledMealRead> ConfirmActualsAsync(RequestActor actor, Guid id, long expected, ActualsWrite request, CancellationToken ct);
    Task<ScheduledMealRead> ClearActualsAsync(RequestActor actor, Guid id, long expected, IReadOnlyList<DependentResolution>? resolutions, CancellationToken ct);
    Task<ScheduledMealRead> ConsumeAsync(RequestActor actor, Guid id, MealVersion expected, ConsumptionWrite request, CancellationToken ct);
    Task<ScheduledMealRead> ClearConsumptionAsync(RequestActor actor, Guid id, MealVersion expected, ClearConsumptionWrite request, CancellationToken ct);
    Task<SnapshotRefreshPreview> PreviewRefreshAsync(RequestActor actor, Guid id, MealVersion expected, RecipeRefreshWrite request, CancellationToken ct);
    Task<ScheduledMealRead> ApplyRefreshAsync(RequestActor actor, Guid id, MealVersion expected, RecipeRefreshWrite request, CancellationToken ct);
    Task<ScheduledMealRead?> ResolveAsync(RequestActor actor, Guid id, long expected, SourceResolutionWrite request, CancellationToken ct);
}
