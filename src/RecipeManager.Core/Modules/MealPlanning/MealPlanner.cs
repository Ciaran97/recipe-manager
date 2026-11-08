using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.MealPlanning;

public sealed class MealPlanner(IPlannerStore store)
{
    public Task<MealSlotRead> SlotAsync(RequestActor actor, Guid id, CancellationToken ct) => store.SlotAsync(actor, Id(id), ct);
    public Task<MealSlotPage> SlotsAsync(RequestActor actor, CancellationToken ct) => store.SlotsAsync(actor, ct);
    public Task<MealSlotRead> CreateSlotAsync(RequestActor actor, MealSlotWrite request, Guid? retryKey, CancellationToken ct)
    {
        _ = DomainRules.Name(request.Name); DomainRules.Position(request.Position);
        return store.CreateSlotAsync(actor, request, retryKey, ct);
    }
    public Task<MealSlotRead> UpdateSlotAsync(RequestActor actor, Guid id, long expected, MealSlotWrite request, CancellationToken ct)
    {
        _ = DomainRules.Name(request.Name); DomainRules.Position(request.Position);
        return store.UpdateSlotAsync(actor, Id(id), expected, request, ct);
    }
    public Task DeleteSlotAsync(RequestActor actor, Guid id, long expected, CancellationToken ct) => store.DeleteSlotAsync(actor, Id(id), expected, ct);
    public Task<MealCalendarRead> CalendarAsync(RequestActor actor, DateOnly from, DateOnly to, CancellationToken ct)
    {
        _ = new MealDateRange(from, to);
        return store.CalendarAsync(actor, from, to, ct);
    }
    public Task<ScheduledMealRead> ReadAsync(RequestActor actor, Guid id, CancellationToken ct) => store.ReadAsync(actor, Id(id), ct);
    public Task<ScheduledMealRead> CreateCookingAsync(RequestActor actor, CookingWrite request, Guid? retryKey, CancellationToken ct)
    {
        Validate(request); return store.CreateCookingAsync(actor, request, retryKey, ct);
    }
    public Task<ScheduledMealRead> UpdateCookingAsync(RequestActor actor, Guid id, MealVersion expected, CookingWrite request, CancellationToken ct)
    {
        Validate(request); return store.UpdateCookingAsync(actor, Id(id), expected, request, ct);
    }
    public Task<ScheduledMealRead> CreateOtherAsync(RequestActor actor, OtherMealWrite request, Guid? retryKey, CancellationToken ct)
    {
        DomainRules.Name(request.Label, "label"); Id(request.SlotId); return store.CreateOtherAsync(actor, request, retryKey, ct);
    }
    public Task<ScheduledMealRead> UpdateOtherAsync(RequestActor actor, Guid id, MealVersion expected, OtherMealWrite request, CancellationToken ct)
    {
        DomainRules.Name(request.Label, "label"); Id(request.SlotId); return store.UpdateOtherAsync(actor, Id(id), expected, request, ct);
    }
    public Task<ScheduledMealRead> MoveAsync(RequestActor actor, Guid id, MealVersion expected, MealPositionWrite request, CancellationToken ct)
    {
        Id(request.SlotId); return store.MoveAsync(actor, Id(id), expected, request, ct);
    }
    public Task<ScheduledMealRead> DuplicateAsync(RequestActor actor, Guid id, MealVersion expected, DuplicateMealWrite request, Guid? retryKey, CancellationToken ct)
    {
        Id(request.SlotId); return store.DuplicateAsync(actor, Id(id), expected, request, retryKey, ct);
    }
    public Task DeleteAsync(RequestActor actor, Guid id, MealVersion expected, DeleteMealWrite request, CancellationToken ct)
        => store.DeleteAsync(actor, Id(id), expected, request, ct);
    public Task<SnapshotRefreshPreview> PreviewRefreshAsync(RequestActor actor, Guid id, MealVersion expected, RecipeRefreshWrite request, CancellationToken ct)
        => store.PreviewRefreshAsync(actor, Id(id), expected, request, ct);
    public Task<ScheduledMealRead> ApplyRefreshAsync(RequestActor actor, Guid id, MealVersion expected, RecipeRefreshWrite request, CancellationToken ct)
        => store.ApplyRefreshAsync(actor, Id(id), expected, request, ct);
    private static void Validate(CookingWrite request)
    {
        Id(request.RecipeId); Id(request.SlotId); _ = new CookingPortions(request.PlannedPrepared, request.PlannedEaten);
        if (request.ExpectedRecipeRevision is <= 0) throw new DomainValidationException("expectedRecipeRevision", "invalid_revision", "Recipe revisions must be positive.");
    }
    private static Guid Id(Guid id) => DomainRules.NonEmptyId(id, "id");
}

public sealed class PlannerConflictException(string code, string message, IReadOnlyList<Guid>? mealIds = null) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public IReadOnlyList<Guid> MealIds { get; } = mealIds ?? [];
}
