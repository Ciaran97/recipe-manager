import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient, type ApiProblem } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';
import { addDays, dayLabel, mondayOf } from './planner-dates';
import {
  inputValue,
  portionPattern,
  type Availability,
  type Calendar,
  type Meal,
  type Recipe,
  type Slot,
  type SourceIntent,
} from './planner-types';
import { PortionInput } from './portion-input';
import { RecipeContentView } from '../recipes/recipe-content';

type Resolution = components['schemas']['DependentResolution'];
type Refresh = components['schemas']['RecipeRefreshResult'];
@Component({
  selector: 'app-leftovers-panel',
  imports: [ReactiveFormsModule, PortionInput, RecipeContentView],
  templateUrl: './leftovers-panel.html',
  styleUrl: './leftovers-panel.css',
})
export class LeftoversPanel implements OnInit {
  private readonly api = inject(ApiClient);
  private readonly fb = inject(FormBuilder);
  readonly mode = input.required<'allocation' | 'source'>();
  readonly mealId = input<string | null>(null);
  readonly sourceId = input<string | null>(null);
  readonly date = input.required<string>();
  readonly slotId = input.required<string>();
  readonly slots = input.required<Slot[]>();
  readonly initialIntent = input<SourceIntent | null>(null);
  readonly saved = output<void>();
  readonly closed = output<void>();
  readonly busy = signal(false);
  readonly error = signal('');
  readonly source = signal<Availability | null>(null);
  readonly meal = signal<Meal | null>(null);
  readonly sourceChoices = signal<Meal[]>([]);
  readonly sourceSlots = signal<Slot[]>([]);
  readonly sourceWeek = signal('');
  readonly operation = signal<SourceIntent | null>(null);
  readonly preview = signal<Refresh['preview']>(null);
  readonly proposedRecipe = signal<Recipe | null>(null);
  readonly recipeReviewed = signal(false);
  readonly needsReview = signal(false);
  readonly reviewed = signal(false);
  readonly confirmDelete = signal(false);
  readonly conflicts = signal<string[]>([]);
  readonly targets = signal(new Map<string, Availability>());
  readonly inputValue = inputValue;
  readonly dayLabel = dayLabel;
  readonly allocationForm = this.fb.nonNullable.group({
    sourceId: ['', Validators.required],
    date: ['', Validators.required],
    slotId: ['', Validators.required],
    plannedPortions: ['2', [Validators.required, Validators.pattern(portionPattern)]],
    reservedRemainder: ['0', [Validators.required, Validators.pattern(portionPattern)]],
  });
  readonly actualForm = this.fb.nonNullable.group({
    prepared: ['', [Validators.required, Validators.pattern(portionPattern)]],
    eaten: ['', [Validators.required, Validators.pattern(portionPattern)]],
  });
  readonly consumptionForm = this.fb.nonNullable.group({
    consumed: ['', [Validators.required, Validators.pattern(portionPattern)]],
    reserved: ['0', [Validators.required, Validators.pattern(portionPattern)]],
    confirmSource: [false],
    prepared: ['', [Validators.required, Validators.pattern(portionPattern)]],
    eaten: ['', [Validators.required, Validators.pattern(portionPattern)]],
  });
  readonly planForm = this.fb.nonNullable.group({
    prepared: ['', [Validators.required, Validators.pattern(portionPattern)]],
    eaten: ['', [Validators.required, Validators.pattern(portionPattern)]],
    date: ['', Validators.required],
    slotId: ['', Validators.required],
  });
  readonly resolutionForm = this.fb.nonNullable.group({
    rows: this.fb.array<ReturnType<LeftoversPanel['resolutionRow']>>([]),
  });
  get rows() {
    return this.resolutionForm.controls.rows;
  }
  private sourceTag = '';
  private mealTag = '';
  private previewTag = '';
  private retryKey?: string;
  private sourceGeneration = 0;
  private choicesGeneration = 0;
  ngOnInit(): void {
    void this.initialize();
  }
  hasUnsavedChanges(): boolean {
    return (
      this.allocationForm.dirty ||
      this.actualForm.dirty ||
      this.consumptionForm.dirty ||
      this.planForm.dirty ||
      this.resolutionForm.dirty
    );
  }
  private resolutionRow(row: Availability['allocations'][number]) {
    return this.fb.nonNullable.group({
      mealId: [row.mealId],
      mealRevision: [row.mealRevision],
      action: ['keep'],
      planned: [row.allocation.plannedPortions],
      consumed: [row.allocation.consumedPortions ?? row.allocation.plannedPortions],
      reserved: [row.allocation.reservedRemainder],
      consumption: ['keep'],
      date: [row.date],
      slotId: [row.slotId],
      targetId: [''],
      confirmDelete: [false],
    });
  }
  private async initialize(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.mode() === 'source') {
        const source = this.sourceId();
        if (!source) throw new Error('Choose a cooking event.');
        await this.loadSource(source, true);
        this.operation.set(this.initialIntent());
        if (this.initialIntent()) {
          this.fillIntent(this.initialIntent()!);
          await this.loadProposedRecipe();
        }
      } else {
        const id = this.mealId();
        if (id) {
          const response = await firstValueFrom(this.api.read<Meal>(`/api/v1/meals/${id}`));
          if (!response.body?.leftovers) throw new Error('This is not a leftover meal.');
          this.meal.set(response.body);
          this.mealTag = response.headers.get('ETag') ?? '';
          const allocation = response.body.leftovers;
          this.allocationForm.reset({
            sourceId: allocation.sourceCookingEventId,
            date: response.body.date,
            slotId: response.body.slotId,
            plannedPortions: allocation.plannedPortions,
            reservedRemainder: allocation.reservedRemainder,
          });
          this.sourceWeek.set(mondayOf(allocation.sourceDate));
          await this.loadSource(allocation.sourceCookingEventId, true);
          this.consumptionForm.reset({
            consumed: allocation.consumedPortions ?? allocation.plannedPortions,
            reserved: allocation.consumedPortions === null ? '0' : allocation.reservedRemainder,
            confirmSource: false,
            prepared: this.source()?.plannedPrepared ?? '',
            eaten: this.source()?.plannedEaten ?? '',
          });
        } else {
          this.allocationForm.reset({
            sourceId: '',
            date: this.date(),
            slotId: this.slotId(),
            plannedPortions: '2',
            reservedRemainder: '0',
          });
          this.sourceWeek.set(mondayOf(this.date()));
        }
        await this.loadChoices();
      }
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }
  private async loadSource(id: string, prefill: boolean): Promise<void> {
    const generation = ++this.sourceGeneration;
    const response = await firstValueFrom(
      this.api.read<Availability>(`/api/v1/cooking-events/${id}/leftovers`),
    );
    if (!response.body || generation !== this.sourceGeneration) return;
    const value = response.body;
    this.source.set(value);
    this.sourceTag = response.headers.get('ETag') ?? '';
    if (prefill) {
      this.actualForm.reset({
        prepared: value.actualPrepared ?? value.plannedPrepared,
        eaten: value.actualEaten ?? value.plannedEaten,
      });
      this.planForm.reset({
        prepared: value.plannedPrepared,
        eaten: value.plannedEaten,
        date: value.date,
        slotId: value.slotId,
      });
      this.buildResolutions(value, false);
      if (!this.sourceWeek()) this.sourceWeek.set(mondayOf(value.date));
    }
  }
  async loadChoices(): Promise<void> {
    const week = this.sourceWeek();
    if (!week) return;
    const generation = ++this.choicesGeneration;
    try {
      const response = await firstValueFrom(
        this.api.read<Calendar>(`/api/v1/meals?from=${week}&to=${addDays(week, 6)}`),
      );
      if (response.body && generation === this.choicesGeneration) {
        this.sourceChoices.set(response.body.items.filter((meal) => meal.type === 'cook'));
        this.sourceSlots.set(response.body.slots);
      }
    } catch (error) {
      this.fail(error);
    }
  }
  chooseWeek(value: string): void {
    try {
      this.sourceWeek.set(mondayOf(value));
      void this.loadChoices();
    } catch (error) {
      this.fail(error);
    }
  }
  choiceLabel(meal: Meal): string {
    return `${meal.cookingEvent?.recipeSnapshot.title ?? 'Recipe'} — ${dayLabel(meal.date)}, ${this.sourceSlots().find((slot) => slot.id === meal.slotId)?.name ?? 'meal'} — ${meal.cookingEvent?.availablePortions ?? '?'} ${meal.cookingEvent?.availabilityBasis ?? 'planned'} unreserved`;
  }
  async selectSource(id: string): Promise<void> {
    if (!id || this.busy()) return;
    await this.perform(async () => {
      await this.loadSource(id, false);
      if (
        !this.meal() &&
        !this.allocationForm.controls.plannedPortions.dirty &&
        this.source()?.availablePortions !== '0'
      )
        this.allocationForm.controls.plannedPortions.setValue(
          this.source()?.availablePortions ?? '2',
        );
    });
  }
  amount(
    form: 'allocation' | 'actual' | 'consumption' | 'plan',
    field: string,
    value: string,
  ): void {
    const controls =
      form === 'allocation'
        ? this.allocationForm.controls
        : form === 'actual'
          ? this.actualForm.controls
          : form === 'consumption'
            ? this.consumptionForm.controls
            : this.planForm.controls;
    const control = Object.entries(controls).find(([key]) => key === field)?.[1];
    if (!control) return;
    // Raw input strings are passed through; no balance or serving calculation runs in this client.
    if (typeof control.value === 'string') {
      (control as typeof this.actualForm.controls.prepared).setValue(value);
      control.markAsDirty();
    }
  }
  staleAllocation(): boolean {
    const current = this.meal()?.leftovers;
    return (
      !!current &&
      current.sourceCookingEventId === this.source()?.sourceCookingEventId &&
      current.sourceRevision !== this.source()?.sourceRevision
    );
  }
  async saveAllocation(): Promise<void> {
    if (this.allocationForm.invalid || this.busy() || !this.source()) {
      this.allocationForm.markAllAsTouched();
      return;
    }
    const value = this.allocationForm.getRawValue();
    const current = this.meal();
    const availability = this.source()!;
    if (value.sourceId !== availability.sourceCookingEventId) {
      this.error.set('Choose a cooking source and wait for its availability.');
      return;
    }
    await this.perform(async () => {
      const body: components['schemas']['LeftoverWrite'] = {
        sourceCookingEventId: value.sourceId,
        sourceRevision: availability.sourceRevision,
        date: value.date,
        slotId: value.slotId,
        plannedPortions: value.plannedPortions,
        ...(current
          ? {
              reservedRemainder: value.reservedRemainder,
              previousSourceRevision:
                current.leftovers?.sourceCookingEventId !== value.sourceId
                  ? current.leftovers?.sourceRevision
                  : null,
            }
          : {}),
      };
      await firstValueFrom(
        this.api.write<Meal>(
          current ? 'PUT' : 'POST',
          current ? `/api/v1/meals/${current.id}/leftovers` : '/api/v1/meals/leftovers',
          body,
          current ? this.mealTag : undefined,
          current ? undefined : (this.retryKey ??= crypto.randomUUID()),
        ),
      );
      this.markSaved();
    });
  }
  releaseReservation(): void {
    this.allocationForm.controls.reservedRemainder.setValue('0');
    this.allocationForm.controls.reservedRemainder.markAsDirty();
  }
  async confirmConsumption(): Promise<void> {
    const meal = this.meal();
    const source = this.source();
    if (!meal || !source || this.consumptionForm.invalid || this.busy()) return;
    if (
      this.allocationForm.dirty ||
      this.allocationForm.controls.sourceId.value !== meal.leftovers?.sourceCookingEventId
    ) {
      this.error.set('Save or cancel planned meal changes before confirming consumption.');
      return;
    }
    const value = this.consumptionForm.getRawValue();
    if (source.basis === 'planned' && !value.confirmSource) {
      this.error.set('Confirm the source cooking quantities below, or record cooking first.');
      return;
    }
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write<Meal>(
          'PUT',
          `/api/v1/meals/${meal.id}/consumption`,
          {
            consumedPortions: value.consumed,
            reservedRemainder: value.reserved,
            sourceRevision: source.sourceRevision,
            ...(source.basis === 'planned'
              ? { sourceActuals: { actualPrepared: value.prepared, actualEaten: value.eaten } }
              : {}),
          },
          this.mealTag,
        ),
      );
      this.markSaved();
    });
  }
  async clearConsumption(): Promise<void> {
    const meal = this.meal();
    const source = this.source();
    if (
      this.allocationForm.dirty ||
      this.allocationForm.controls.sourceId.value !== meal?.leftovers?.sourceCookingEventId
    ) {
      this.error.set('Save or cancel planned meal changes before clearing consumption.');
      return;
    }
    if (
      !meal ||
      !source ||
      this.busy() ||
      !window.confirm(
        'Clear this meal’s recorded consumption? The remaining reservation shown below will be kept.',
      )
    )
      return;
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write<Meal>(
          'DELETE',
          `/api/v1/meals/${meal.id}/consumption`,
          {
            sourceRevision: source.sourceRevision,
            reservedRemainder: this.consumptionForm.controls.reserved.value,
          },
          this.mealTag,
        ),
      );
      this.markSaved();
    });
  }
  async saveActuals(): Promise<void> {
    const source = this.source();
    if (!source || this.actualForm.invalid || this.busy()) {
      this.actualForm.markAllAsTouched();
      return;
    }
    if (this.needsReview() && !this.reviewed()) {
      this.error.set('Reload and review current quantities before applying your correction.');
      return;
    }
    const value = this.actualForm.getRawValue();
    await this.perform(async () => {
      try {
        await firstValueFrom(
          this.api.write<Meal>(
            'PUT',
            `/api/v1/cooking-events/${source.sourceCookingEventId}/actuals`,
            { actualPrepared: value.prepared, actualEaten: value.eaten },
            this.sourceTag,
          ),
        );
        this.markSaved();
      } catch (error) {
        if (error instanceof HttpErrorResponse && error.status === 409)
          this.operation.set({
            kind: 'actuals',
            actualPrepared: value.prepared,
            actualEaten: value.eaten,
          });
        throw error;
      }
    });
  }
  async clearActuals(): Promise<void> {
    const source = this.source();
    if (
      !source ||
      this.busy() ||
      !window.confirm(
        'Clear confirmed cooking quantities and return to the planned basis? Related leftover meals will be checked.',
      )
    )
      return;
    if (this.needsReview() && !this.reviewed()) {
      this.error.set('Reload and review current quantities before clearing confirmation.');
      return;
    }
    await this.perform(async () => {
      try {
        await firstValueFrom(
          this.api.write<Meal>(
            'DELETE',
            `/api/v1/cooking-events/${source.sourceCookingEventId}/actuals`,
            undefined,
            this.sourceTag,
          ),
        );
        this.markSaved();
      } catch (error) {
        if (error instanceof HttpErrorResponse && error.status === 409)
          this.operation.set({ kind: 'clearActuals' });
        throw error;
      }
    });
  }
  private fillIntent(intent: SourceIntent): void {
    const current = this.source();
    if (!current) return;
    this.planForm.reset({
      prepared: intent.plannedPrepared ?? current.plannedPrepared,
      eaten: intent.plannedEaten ?? current.plannedEaten,
      date: intent.date ?? current.date,
      slotId: intent.slotId ?? current.slotId,
    });
  }
  private buildResolutions(source: Availability, keep: boolean): void {
    const existing = new Map(
      this.rows.controls.map((row) => [row.controls.mealId.value, row.getRawValue()]),
    );
    this.rows.clear();
    for (const allocation of source.allocations) {
      const row = this.resolutionRow(allocation);
      const old = keep ? existing.get(allocation.mealId) : undefined;
      if (old) row.patchValue({ ...old, mealRevision: allocation.mealRevision });
      this.rows.push(row);
    }
    this.resolutionForm.markAsPristine();
  }
  async selectTarget(index: number, id: string): Promise<void> {
    if (!id || this.busy()) return;
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.read<Availability>(`/api/v1/cooking-events/${id}/leftovers`),
      );
      if (response.body && this.rows.at(index)?.controls.targetId.value === id) {
        const next = new Map(this.targets());
        next.set(id, response.body);
        this.targets.set(next);
      }
    });
  }
  private async loadProposedRecipe(): Promise<void> {
    const id = this.operation()?.recipeId;
    if (!id) return;
    const result = await firstValueFrom(this.api.read<Recipe>(`/api/v1/recipes/${id}`));
    if (result.body?.revision !== this.proposedRecipe()?.revision) this.recipeReviewed.set(false);
    this.proposedRecipe.set(result.body);
  }
  recipeReviewNeeded(): boolean {
    return (
      !!this.proposedRecipe() &&
      this.operation()?.expectedRecipeRevision !== this.proposedRecipe()?.revision
    );
  }
  async reloadForReview(): Promise<void> {
    const source = this.source();
    if (!source || this.busy()) return;
    await this.perform(async () => {
      await this.loadSource(source.sourceCookingEventId, false);
      if (this.source()) this.buildResolutions(this.source()!, true);
      this.preview.set(null);
      this.needsReview.set(true);
      this.reviewed.set(false);
      await this.loadProposedRecipe();
    });
  }
  async reloadAllocation(): Promise<void> {
    if (
      this.busy() ||
      (this.hasUnsavedChanges() && !window.confirm('Reload this meal and discard unsaved changes?'))
    )
      return;
    await this.initialize();
  }
  async applyResolution(): Promise<void> {
    const source = this.source();
    const operation = this.operation();
    if (!source || !operation || this.busy()) return;
    if ((operation.kind === 'plan' || operation.kind === 'move') && this.planForm.invalid) {
      this.planForm.markAllAsTouched();
      this.error.set('Enter the proposed date, slot and portion quantities.');
      return;
    }
    if (operation.kind === 'actuals' && this.actualForm.invalid) {
      this.actualForm.markAllAsTouched();
      this.error.set('Enter the corrected prepared and eaten quantities.');
      return;
    }
    if (this.needsReview() && !this.reviewed()) {
      this.error.set('Review the current cooking quantities and dependent meals before applying.');
      return;
    }
    if (operation.kind === 'delete' && !this.confirmDelete()) {
      this.error.set('Confirm deleting the cooking event after resolving its dependent meals.');
      return;
    }
    if (this.recipeReviewNeeded() && !this.recipeReviewed()) {
      this.error.set('The selected recipe changed. Review it before applying.');
      return;
    }
    await this.perform(async () => {
      const expected = new Map([[source.sourceCookingEventId, source.sourceRevision]]);
      const dependents: Resolution[] = [];
      for (const group of this.rows.controls) {
        const row = group.getRawValue();
        if (row.action === 'keep') continue;
        const base = {
          mealId: row.mealId,
          mealRevision: row.mealRevision,
          confirmConsumedRemoval: false,
          clearConsumption: false,
        };
        if (row.action === 'delete') {
          const original = source.allocations.find((item) => item.mealId === row.mealId);
          if (original?.allocation.consumedPortions != null && !row.confirmDelete)
            throw new Error(
              'Confirm removing recorded consumption for each deleted leftover meal.',
            );
          dependents.push({ ...base, action: 'delete', confirmConsumedRemoval: row.confirmDelete });
        } else if (row.action === 'release')
          dependents.push({ ...base, action: 'release', reservedRemainder: row.reserved });
        else if (row.action === 'move')
          dependents.push({ ...base, action: 'move', date: row.date, slotId: row.slotId });
        else {
          const change: Resolution = {
            ...base,
            action: row.action === 'reassign' ? 'reassign' : 'edit',
            plannedPortions: row.planned,
            reservedRemainder: row.reserved,
            ...(row.consumption === 'replace'
              ? { consumedPortions: row.consumed }
              : row.consumption === 'clear'
                ? { clearConsumption: true }
                : {}),
          };
          if (row.action === 'reassign') {
            const target = this.targets().get(row.targetId);
            if (!target)
              throw new Error('Choose the new cooking source and load its availability.');
            change.sourceCookingEventId = target.sourceCookingEventId;
            change.date = row.date;
            change.slotId = row.slotId;
            expected.set(target.sourceCookingEventId, target.sourceRevision);
          }
          dependents.push(change);
        }
      }
      const plan = this.planForm.getRawValue();
      const values = this.actualForm.getRawValue();
      const body: components['schemas']['SourceResolutionWrite'] = {
        kind: operation.kind,
        expectedSources: [...expected].map(([sourceCookingEventId, revision]) => ({
          sourceCookingEventId,
          revision,
        })),
        dependents,
        ...(operation.kind === 'plan'
          ? {
              plannedPrepared: plan.prepared,
              plannedEaten: plan.eaten,
              date: plan.date,
              slotId: plan.slotId,
              recipeId: operation.recipeId,
              expectedRecipeRevision: this.recipeReviewed()
                ? this.proposedRecipe()?.revision
                : operation.expectedRecipeRevision,
            }
          : {}),
        ...(operation.kind === 'move' ? { date: plan.date, slotId: plan.slotId } : {}),
        ...(operation.kind === 'actuals'
          ? { actualPrepared: values.prepared, actualEaten: values.eaten }
          : {}),
      };
      await firstValueFrom(
        this.api.write<Meal>(
          'POST',
          `/api/v1/cooking-events/${source.sourceCookingEventId}/resolve`,
          body,
          this.sourceTag,
        ),
      );
      this.markSaved();
    });
  }
  async previewRefresh(): Promise<void> {
    const source = this.source();
    if (!source || this.busy() || this.hasUnsavedChanges()) return;
    await this.perform(async () => {
      const meal = await firstValueFrom(
        this.api.read<Meal>(`/api/v1/meals/${source.sourceMealId}`),
      );
      if (!meal.body?.cookingEvent || meal.body.cookingEvent.revision !== source.sourceRevision)
        throw new Error(
          'This cooking event changed. Reload its current quantities before reviewing the recipe.',
        );
      const recipe = await firstValueFrom(
        this.api.read<Recipe>(`/api/v1/recipes/${source.sourceRecipeId}`),
      );
      if (!recipe.body) return;
      const response = await firstValueFrom(
        this.api.write<Refresh>(
          'POST',
          `/api/v1/meals/${source.sourceMealId}/recipe-refresh`,
          { mode: 'preview', expectedRecipeRevision: recipe.body.revision },
          meal.headers.get('ETag') ?? '',
        ),
      );
      if (response.body?.preview) {
        this.preview.set(response.body.preview);
        this.previewTag = response.headers.get('ETag') ?? '';
      }
    });
  }
  async applyRefresh(): Promise<void> {
    const source = this.source();
    const preview = this.preview();
    if (!source || !preview || this.busy()) return;
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write<Refresh>(
          'POST',
          `/api/v1/meals/${source.sourceMealId}/recipe-refresh`,
          {
            mode: 'apply',
            expectedRecipeRevision: preview.recipeRevision,
            expectedEventRevision: preview.eventRevision,
          },
          this.previewTag,
        ),
      );
      this.markSaved();
    });
  }
  discardPreview(): void {
    this.preview.set(null);
  }
  private markSaved(): void {
    this.allocationForm.markAsPristine();
    this.actualForm.markAsPristine();
    this.consumptionForm.markAsPristine();
    this.planForm.markAsPristine();
    this.resolutionForm.markAsPristine();
    this.saved.emit();
  }
  private fail(error: unknown): void {
    this.error.set(apiMessage(error));
    if (error instanceof HttpErrorResponse) {
      const details = error.error as ApiProblem | null;
      this.conflicts.set(details?.conflicts?.map((item) => item.resourceId) ?? []);
      if (error.status === 412) {
        this.needsReview.set(true);
        this.reviewed.set(false);
      }
    }
  }
  private async perform(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.conflicts.set([]);
    try {
      await action();
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }
}
