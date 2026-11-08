import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  untracked,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { HouseholdSync } from '../../core/sync/household-sync';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';
import { addDays, dayLabel, householdToday, mondayOf, weekDays } from './planner-dates';
import {
  inputValue,
  portionPattern,
  type Calendar,
  type Meal,
  type Recipe,
  type Slot,
  type SourceIntent,
} from './planner-types';
import { PortionInput } from './portion-input';
import { LeftoversPanel } from './leftovers-panel';
import { RecipeContentView } from '../recipes/recipe-content';

type Editor = 'cook' | 'other' | 'leftovers' | 'source' | 'position' | 'duplicate';
@Component({
  selector: 'app-meal-planner',
  imports: [ReactiveFormsModule, RouterLink, PortionInput, LeftoversPanel, RecipeContentView],
  templateUrl: './meal-planner.html',
  styleUrl: './meal-planner.css',
})
export class MealPlanner {
  private readonly api = inject(ApiClient);
  private readonly fb = inject(FormBuilder);
  private readonly destroyed = inject(DestroyRef);
  private readonly panel = viewChild(LeftoversPanel);
  readonly calendar = signal<Calendar | null>(null);
  readonly weekStart = signal(mondayOf(householdToday('UTC')));
  readonly days = computed(() => weekDays(this.weekStart()));
  readonly timezone = signal('UTC');
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly remoteChanged = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly editor = signal<Editor | null>(null);
  readonly selected = signal<Meal | null>(null);
  readonly recipes = signal<Recipe[]>([]);
  readonly recipeQuery = signal('');
  readonly recipeNext = signal<string | null>(null);
  readonly intent = signal<SourceIntent | null>(null);
  readonly activeSlots = computed(
    () => this.calendar()?.slots.filter((slot) => !slot.archived) ?? [],
  );
  readonly visibleSlots = computed(
    () =>
      this.calendar()?.slots.filter(
        (slot) => !slot.archived || this.calendar()?.items.some((meal) => meal.slotId === slot.id),
      ) ?? [],
  );
  readonly isBusy = computed(() => this.busy() || (this.panel()?.busy() ?? false));
  readonly form = this.fb.nonNullable.group({
    recipeId: [''],
    date: ['', Validators.required],
    slotId: ['', Validators.required],
    plannedPrepared: ['2', [Validators.required, Validators.pattern(portionPattern)]],
    plannedEaten: ['2', [Validators.required, Validators.pattern(portionPattern)]],
    label: [''],
  });
  readonly slotForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    position: [0, [Validators.required, Validators.min(0)]],
  });
  readonly editingSlot = signal<Slot | null>(null);
  readonly slotEditor = signal(false);
  readonly inputValue = inputValue;
  readonly dayLabel = dayLabel;
  private etag = '';
  private slotTag = '';
  private recipeVersion?: string;
  private retryKey?: string;
  private slotRetry?: string;
  private generation = 0;
  private recipeGeneration = 0;
  private previousFocus: HTMLElement | null = null;
  constructor() {
    const sync = inject(HouseholdSync);
    effect(() => {
      const change = sync.change();
      if (
        !change ||
        !change.domains.some(
          (key) =>
            key === 'plannerRevision' || key === 'recipesRevision' || key === 'settingsRevision',
        )
      )
        return;
      untracked(() => {
        if (this.loading() || this.isBusy() || this.editor() || this.slotEditor())
          this.remoteChanged.set(true);
        else void this.loadWeek();
      });
    });
    void this.initialize();
  }
  private async initialize(): Promise<void> {
    try {
      const settings = await firstValueFrom(
        this.api.read<components['schemas']['HouseholdSettingsRead']>('/api/v1/household/settings'),
      );
      if (settings.body) this.timezone.set(settings.body.timezone);
      this.weekStart.set(mondayOf(householdToday(this.timezone())));
      await this.loadWeek();
    } catch (error) {
      this.error.set(apiMessage(error));
      this.loading.set(false);
    }
  }
  async loadWeek(): Promise<void> {
    this.remoteChanged.set(false);
    const generation = ++this.generation;
    const from = this.weekStart();
    this.loading.set(true);
    try {
      const result = await firstValueFrom(
        this.api.read<Calendar>(`/api/v1/meals?from=${from}&to=${addDays(from, 6)}`),
      );
      if (result.body && generation === this.generation && from === this.weekStart())
        this.calendar.set(result.body);
    } catch (error) {
      if (generation === this.generation) this.error.set(apiMessage(error));
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }
  async goToWeek(value: string): Promise<void> {
    if (this.isBusy() || !this.discardAllowed()) return;
    try {
      this.resetEditor();
      this.weekStart.set(mondayOf(value));
      await this.loadWeek();
    } catch (error) {
      this.error.set(apiMessage(error));
    }
  }
  async shiftWeek(offset: number): Promise<void> {
    await this.goToWeek(addDays(this.weekStart(), offset * 7));
  }
  today(): void {
    void this.goToWeek(householdToday(this.timezone()));
  }
  mealsFor(date: string, slotId: string): Meal[] {
    return (
      this.calendar()?.items.filter((meal) => meal.date === date && meal.slotId === slotId) ?? []
    );
  }
  title(meal: Meal): string {
    return (
      meal.cookingEvent?.recipeSnapshot.title ?? meal.leftovers?.recipeTitle ?? meal.label ?? 'Meal'
    );
  }
  private defaultSlot(): string {
    return (
      this.activeSlots().find((slot) => slot.isDefault && slot.name === 'Dinner')?.id ??
      this.activeSlots()[0]?.id ??
      ''
    );
  }
  async openNew(
    kind: 'cook' | 'other' | 'leftovers',
    date = this.weekStart(),
    slotId = this.defaultSlot(),
  ): Promise<void> {
    if (this.isBusy() || !this.discardAllowed()) return;
    this.rememberFocus();
    this.resetEditor();
    this.form.reset({
      recipeId: '',
      date,
      slotId,
      plannedPrepared: '2',
      plannedEaten: '2',
      label: '',
    });
    this.editor.set(kind);
    this.configureValidation(kind);
    this.focusEditor();
    if (kind === 'cook') await this.loadRecipes();
  }
  async openEdit(id: string): Promise<void> {
    if (this.isBusy() || !this.discardAllowed()) return;
    this.rememberFocus();
    this.resetEditor();
    await this.perform(async () => {
      const response = await firstValueFrom(this.api.read<Meal>(`/api/v1/meals/${id}`));
      if (!response.body) return;
      this.accept(response.body, response.headers.get('ETag') ?? '');
      const kind = response.body.type;
      this.editor.set(kind);
      this.configureValidation(kind);
      this.focusEditor();
      if (kind === 'cook') await this.loadRecipes();
    });
  }
  async openSource(meal: Meal): Promise<void> {
    if (meal.leftovers) {
      await this.openSourceId(meal.leftovers.sourceCookingEventId);
      return;
    }
    await this.openSourceId(meal.cookingEvent?.id ?? '');
  }
  private async openSourceId(id: string): Promise<void> {
    if (!id || this.isBusy() || !this.discardAllowed()) return;
    this.rememberFocus();
    this.resetEditor();
    await this.perform(async () => {
      const availability = await firstValueFrom(
        this.api.read<components['schemas']['LeftoversAvailability']>(
          `/api/v1/cooking-events/${id}/leftovers`,
        ),
      );
      if (!availability.body) return;
      const result = await firstValueFrom(
        this.api.read<Meal>(`/api/v1/meals/${availability.body.sourceMealId}`),
      );
      if (!result.body) return;
      this.accept(result.body, result.headers.get('ETag') ?? '');
      this.editor.set('source');
      this.focusEditor();
    });
  }
  private accept(meal: Meal, tag: string): void {
    this.selected.set(meal);
    this.etag = tag;
    this.recipeVersion = undefined;
    this.form.reset({
      recipeId: meal.cookingEvent?.sourceRecipeId ?? '',
      date: meal.date,
      slotId: meal.slotId,
      plannedPrepared: meal.cookingEvent?.plannedPrepared ?? '2',
      plannedEaten: meal.cookingEvent?.plannedEaten ?? '2',
      label: meal.label ?? '',
    });
  }
  private configureValidation(kind: string): void {
    this.form.controls.recipeId.setValidators(kind === 'cook' ? Validators.required : []);
    this.form.controls.label.setValidators(
      kind === 'other' ? [Validators.required, Validators.maxLength(200)] : [],
    );
    this.form.controls.recipeId.updateValueAndValidity();
    this.form.controls.label.updateValueAndValidity();
  }
  async loadRecipes(append = false): Promise<void> {
    const generation = ++this.recipeGeneration;
    const query = this.recipeQuery().trim();
    const cursor = append ? this.recipeNext() : null;
    try {
      const response = await firstValueFrom(
        this.api.read<components['schemas']['RecipeListPage']>(
          `/api/v1/recipes?limit=100&archived=exclude${query ? '&q=' + encodeURIComponent(query) : ''}${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
        ),
      );
      if (!response.body || generation !== this.recipeGeneration) return;
      const values = new Map((append ? this.recipes() : []).map((recipe) => [recipe.id, recipe]));
      for (const recipe of response.body.items) values.set(recipe.id, recipe);
      this.recipes.set([...values.values()]);
      this.recipeNext.set(response.body.nextCursor ?? null);
    } catch (error) {
      if (generation === this.recipeGeneration) this.error.set(apiMessage(error));
    }
  }
  chooseRecipe(id: string): void {
    const recipe = this.recipes().find((item) => item.id === id);
    this.recipeVersion = recipe?.revision;
    if (
      !this.selected() &&
      recipe &&
      !this.form.controls.plannedPrepared.dirty &&
      !this.form.controls.plannedEaten.dirty
    )
      this.form.patchValue({
        plannedPrepared: recipe.defaultServings,
        plannedEaten: recipe.defaultServings,
      });
  }
  portion(field: 'plannedPrepared' | 'plannedEaten', value: string): void {
    this.form.controls[field].setValue(value);
    this.form.controls[field].markAsDirty();
  }
  async saveMeal(): Promise<void> {
    if (this.form.invalid || this.isBusy()) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const current = this.selected();
    const kind = this.editor();
    const body =
      kind === 'cook'
        ? {
            recipeId: value.recipeId,
            date: value.date,
            slotId: value.slotId,
            plannedPrepared: value.plannedPrepared,
            plannedEaten: value.plannedEaten,
            ...(this.recipeVersion && value.recipeId !== current?.cookingEvent?.sourceRecipeId
              ? { expectedRecipeRevision: this.recipeVersion }
              : {}),
          }
        : { date: value.date, slotId: value.slotId, label: value.label };
    await this.perform(async () => {
      try {
        await firstValueFrom(
          this.api.write<Meal>(
            current ? 'PUT' : 'POST',
            current
              ? `/api/v1/meals/${current.id}/${kind === 'cook' ? 'cooking' : 'other'}`
              : kind === 'cook'
                ? '/api/v1/meals/cooking'
                : '/api/v1/meals/other',
            body,
            current ? this.etag : undefined,
            current ? undefined : (this.retryKey ??= crypto.randomUUID()),
          ),
        );
        await this.saved();
      } catch (error) {
        if (
          kind === 'cook' &&
          current?.cookingEvent &&
          error instanceof HttpErrorResponse &&
          error.status === 409
        ) {
          this.intent.set({
            kind: 'plan',
            plannedPrepared: value.plannedPrepared,
            plannedEaten: value.plannedEaten,
            date: value.date,
            slotId: value.slotId,
            ...(value.recipeId !== current.cookingEvent.sourceRecipeId
              ? { recipeId: value.recipeId, expectedRecipeRevision: this.recipeVersion }
              : {}),
          });
          this.editor.set('source');
          this.focusEditor();
        }
        throw error;
      }
    });
  }
  async openPosition(meal: Meal, duplicate: boolean): Promise<void> {
    if (this.isBusy() || !this.discardAllowed()) return;
    this.rememberFocus();
    this.resetEditor();
    await this.perform(async () => {
      const response = await firstValueFrom(this.api.read<Meal>(`/api/v1/meals/${meal.id}`));
      if (!response.body) return;
      this.accept(response.body, response.headers.get('ETag') ?? '');
      this.editor.set(duplicate ? 'duplicate' : 'position');
      this.configureValidation('position');
      this.focusEditor();
    });
  }
  async savePosition(): Promise<void> {
    if (this.isBusy() || !this.form.controls.date.value || !this.form.controls.slotId.value) return;
    const meal = this.selected();
    if (!meal) return;
    const duplicate = this.editor() === 'duplicate';
    const value = this.form.getRawValue();
    const body = {
      date: value.date,
      slotId: value.slotId,
      sourceRevision: meal.cookingEvent?.revision ?? meal.leftovers?.sourceRevision ?? null,
    };
    await this.perform(async () => {
      try {
        await firstValueFrom(
          this.api.write<Meal>(
            duplicate ? 'POST' : 'PUT',
            `/api/v1/meals/${meal.id}/${duplicate ? 'duplicate' : 'position'}`,
            body,
            this.etag,
            duplicate ? (this.retryKey ??= crypto.randomUUID()) : undefined,
          ),
        );
        await this.saved();
      } catch (error) {
        if (
          !duplicate &&
          meal.cookingEvent &&
          error instanceof HttpErrorResponse &&
          error.status === 409
        ) {
          this.intent.set({ kind: 'move', date: value.date, slotId: value.slotId });
          this.editor.set('source');
        }
        throw error;
      }
    });
  }
  async remove(meal: Meal): Promise<void> {
    if (this.isBusy() || !this.discardAllowed()) return;
    const recorded = meal.leftovers?.consumedPortions != null;
    if (
      !window.confirm(
        recorded
          ? 'Delete this meal and explicitly remove its recorded consumption and reservation?'
          : 'Delete this scheduled meal?',
      )
    )
      return;
    await this.perform(async () => {
      const response = await firstValueFrom(this.api.read<Meal>(`/api/v1/meals/${meal.id}`));
      if (!response.body) return;
      const current = response.body;
      const tag = response.headers.get('ETag') ?? '';
      if (
        current.leftovers?.consumedPortions != null &&
        !recorded &&
        !window.confirm(
          'This meal now has recorded consumption. Confirm removing that consumption and its reservation.',
        )
      )
        return;
      try {
        await firstValueFrom(
          this.api.write(
            'DELETE',
            `/api/v1/meals/${current.id}`,
            {
              sourceRevision:
                current.cookingEvent?.revision ?? current.leftovers?.sourceRevision ?? null,
              confirmConsumedRemoval: current.leftovers?.consumedPortions != null,
            },
            tag,
          ),
        );
        await this.saved();
      } catch (error) {
        if (current.cookingEvent && error instanceof HttpErrorResponse && error.status === 409) {
          this.accept(current, tag);
          this.intent.set({ kind: 'delete' });
          this.editor.set('source');
          this.focusEditor();
        }
        throw error;
      }
    });
  }
  async saved(): Promise<void> {
    this.resetEditor();
    this.notice.set('Meal plan updated.');
    await this.loadWeek();
    this.restoreFocus();
  }
  cancelEditor(): void {
    if (this.isBusy() || !this.discardAllowed()) return;
    this.resetEditor();
    this.restoreFocus();
  }
  hasUnsavedChanges(): boolean {
    return this.form.dirty || this.slotForm.dirty || (this.panel()?.hasUnsavedChanges() ?? false);
  }
  private discardAllowed(): boolean {
    if (!this.hasUnsavedChanges()) return true;
    if (!window.confirm('Discard unsaved meal changes?')) return false;
    this.resetEditor();
    this.slotForm.reset({ name: '', position: this.calendar()?.slots.length ?? 0 });
    this.slotEditor.set(false);
    return true;
  }
  private resetEditor(): void {
    this.notice.set('');
    this.editor.set(null);
    this.selected.set(null);
    this.intent.set(null);
    this.form.markAsPristine();
    this.error.set('');
    this.retryKey = undefined;
    this.recipeVersion = undefined;
  }
  private rememberFocus(): void {
    this.previousFocus =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }
  private restoreFocus(): void {
    this.previousFocus?.focus();
  }
  private focusEditor(): void {
    setTimeout(() => {
      if (this.destroyed.destroyed) return;
      const heading = document.getElementById('meal-editor-heading');
      heading?.scrollIntoView?.({ block: 'start' });
      heading?.focus();
    }, 0);
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
  newSlot(): void {
    if (this.isBusy() || (this.slotForm.dirty && !window.confirm('Discard unsaved slot changes?')))
      return;
    this.editingSlot.set(null);
    this.slotTag = '';
    this.slotRetry = undefined;
    this.slotForm.reset({ name: '', position: this.calendar()?.slots.length ?? 0 });
    this.slotEditor.set(true);
  }
  async editSlot(slot: Slot): Promise<void> {
    if (this.isBusy() || (this.slotForm.dirty && !window.confirm('Discard unsaved slot changes?')))
      return;
    await this.perform(async () => {
      const response = await firstValueFrom(this.api.read<Slot>(`/api/v1/meal-slots/${slot.id}`));
      if (!response.body) return;
      this.editingSlot.set(response.body);
      this.slotTag = response.headers.get('ETag') ?? '';
      this.slotForm.reset({ name: response.body.name, position: response.body.position });
      this.slotEditor.set(true);
    });
  }
  async saveSlot(): Promise<void> {
    if (this.slotForm.invalid || this.isBusy()) {
      this.slotForm.markAllAsTouched();
      return;
    }
    await this.perform(async () => {
      const selected = this.editingSlot();
      await firstValueFrom(
        this.api.write<Slot>(
          selected ? 'PUT' : 'POST',
          selected ? `/api/v1/meal-slots/${selected.id}` : '/api/v1/meal-slots',
          this.slotForm.getRawValue(),
          selected ? this.slotTag : undefined,
          selected ? undefined : (this.slotRetry ??= crypto.randomUUID()),
        ),
      );
      this.slotForm.markAsPristine();
      this.slotEditor.set(false);
      await this.loadWeek();
      this.notice.set('Meal slot saved.');
    });
  }
  async deleteSlot(): Promise<void> {
    const selected = this.editingSlot();
    if (
      !selected ||
      this.isBusy() ||
      !window.confirm('Remove this slot? Used slots are archived and existing meals are kept.')
    )
      return;
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write('DELETE', `/api/v1/meal-slots/${selected.id}`, undefined, this.slotTag),
      );
      this.slotForm.markAsPristine();
      this.slotEditor.set(false);
      await this.loadWeek();
      this.notice.set('Meal slot removed.');
    });
  }
  cancelSlot(): void {
    if (this.slotForm.dirty && !window.confirm('Discard unsaved slot changes?')) return;
    this.slotForm.markAsPristine();
    this.slotEditor.set(false);
  }
  private async perform(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
