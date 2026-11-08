using RecipeManager.Core.Modules.MealPlanning.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.MealPlanning;

public sealed class MealSlot
{
    private MealSlot() { }
    public MealSlot(Guid id, Guid householdId, string name, int position, bool isDefault = false)
    {
        Id = DomainRules.NonEmptyId(id, "slotId"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        Rename(name, position); Revision = 1; IsDefault = isDefault;
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Position { get; private set; }
    public bool IsDefault { get; private set; }
    public bool Archived { get; private set; }
    public long Revision { get; private set; }
    public void Rename(string name, int position)
    {
        var validatedName = DomainRules.Name(name); var validatedPosition = DomainRules.Position(position);
        Name = validatedName; Position = validatedPosition; Advance();
    }
    public void Archive() { Archived = true; Advance(); }
    private void Advance() => Revision = checked(Revision + 1);
}

public sealed class ScheduledMeal
{
    private ScheduledMeal() { }
    public ScheduledMeal(Guid id, Guid householdId, DateOnly date, Guid slotId, MealType type, string? label = null)
    {
        if (!Enum.IsDefined(type)) throw new DomainValidationException("type", "unsupported", "Choose cook, leftovers or other.");
        var validatedLabel = type == MealType.Other ? DomainRules.Name(label, "label") : null;
        Id = DomainRules.NonEmptyId(id, "mealId"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        SlotId = DomainRules.NonEmptyId(slotId, "slotId"); Date = date; Type = type; Label = validatedLabel;
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid SlotId { get; private set; }
    public MealType Type { get; private set; }
    public string? Label { get; private set; }
    public long Revision { get; private set; } = 1;
    public void Move(DateOnly date, Guid slotId)
    {
        var validated = DomainRules.NonEmptyId(slotId, "slotId"); Date = date; SlotId = validated; Advance();
    }
    public void Relabel(string label)
    {
        if (Type != MealType.Other) throw new DomainValidationException("type", "invalid_meal_type", "Only other meals have a free-text label.");
        Label = DomainRules.Name(label, "label"); Advance();
    }
    public void Advance() => Revision = checked(Revision + 1);
}

public sealed class CookingEvent
{
    private CookingEvent() { }
    public CookingEvent(Guid id, Guid householdId, Guid scheduledMealId, Guid recipeId, long recipeRevision, string recipeSnapshotJson,
        decimal plannedPrepared, decimal plannedEaten)
    {
        var portions = new CookingPortions(plannedPrepared, plannedEaten);
        Id = DomainRules.NonEmptyId(id, "cookingEventId"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId");
        ScheduledMealId = DomainRules.NonEmptyId(scheduledMealId, "scheduledMealId");
        ReplaceRecipe(recipeId, recipeRevision, recipeSnapshotJson); Revision = 1;
        PlannedPrepared = portions.PlannedPrepared; PlannedEaten = portions.PlannedEaten;
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid ScheduledMealId { get; private set; }
    public MealType MealType { get; private set; } = MealType.Cook;
    public Guid RecipeId { get; private set; }
    public long RecipeRevision { get; private set; }
    public string RecipeSnapshotJson { get; private set; } = null!;
    public decimal PlannedPrepared { get; private set; }
    public decimal PlannedEaten { get; private set; }
    public decimal? ActualPrepared { get; private set; }
    public decimal? ActualEaten { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public long Revision { get; private set; } = 1;
    public CookingPortions Portions => new(PlannedPrepared, PlannedEaten, ActualPrepared, ActualEaten);
    public void ChangePlan(decimal prepared, decimal eaten)
    {
        _ = new CookingPortions(prepared, eaten, ActualPrepared, ActualEaten);
        PlannedPrepared = prepared; PlannedEaten = eaten; Advance();
    }
    public void SetActuals(decimal? prepared, decimal? eaten, DateTimeOffset? confirmedAt)
    {
        _ = new CookingPortions(PlannedPrepared, PlannedEaten, prepared, eaten);
        if (prepared.HasValue != confirmedAt.HasValue)
            throw new DomainValidationException("confirmedAt", "inconsistent_confirmation", "Actual portions require a confirmation time; clearing actuals clears confirmation.");
        ActualPrepared = prepared; ActualEaten = eaten; ConfirmedAt = confirmedAt; Advance();
    }
    /// <summary>Only explicit recipe selection or refresh commands may replace this immutable basis.</summary>
    public void ReplaceRecipe(Guid recipeId, long recipeRevision, string recipeSnapshotJson)
    {
        var validatedId = DomainRules.NonEmptyId(recipeId, "recipeId");
        if (recipeRevision < 1 || string.IsNullOrWhiteSpace(recipeSnapshotJson))
            throw new DomainValidationException("recipeSnapshot", "invalid_snapshot", "A complete recipe snapshot and positive source revision are required.");
        RecipeId = validatedId; RecipeRevision = recipeRevision; RecipeSnapshotJson = recipeSnapshotJson; Advance();
    }
    public void Advance() => Revision = checked(Revision + 1);
}
