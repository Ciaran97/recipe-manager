import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';
import { addDays, householdToday, mondayOf } from '../planner/planner-dates';
import {
  newerRevision,
  type IngredientUnit,
  type MeasurementStandard,
  type QuantityKind,
  type RecipeEntry,
  type ShoppingBasis,
  type ShoppingChoice,
  type ShoppingItem,
  type ShoppingList,
  type ShoppingPreview,
  type ShoppingSelection,
} from './shopping-types';

@Injectable()
export class ShoppingListState {
  private readonly api = inject(ApiClient);
  private readonly fb = inject(FormBuilder).nonNullable;
  private destroyed = false;
  private generation = 0;
  private tag = '';
  private retry?: string;
  private recipeCursor?: string;
  private recipesGeneration = 0;
  private entriesDirty = false;
  private pendingRemote = false;
  private sourceGeneration = 0;
  private editInitial = '';
  readonly current = signal<ShoppingList | null>(null);
  readonly lists = signal<components['schemas']['ShoppingListBrief'][]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly busy = signal(false);
  readonly initializing = signal(true);
  readonly error = signal('');
  readonly notice = signal('');
  readonly outdated = signal(false);
  readonly deletedRemotely = signal(false);
  readonly sourcesChanged = signal(false);
  readonly editor = signal<'create' | 'manual' | 'item' | 'rename' | 'reconcile' | null>(null);
  readonly editingItem = signal<ShoppingItem | null>(null);
  readonly sourceKind = signal<'mealRange' | 'recipes' | 'manual'>('mealRange');
  readonly recipeEntries = signal<RecipeEntry[]>([]);
  readonly recipes = signal<components['schemas']['RecipeRead'][]>([]);
  readonly moreRecipes = signal(false);
  readonly categories = signal<components['schemas']['CategoryRead'][]>([]);
  readonly preview = signal<ShoppingPreview | null>(null);
  readonly choices = signal<Record<string, ShoppingChoice>>({});
  readonly reviewRequired = signal(false);
  readonly reviewed = signal(false);
  readonly showRemoved = signal(false);
  readonly creationForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    from: '',
    to: '',
    basis: this.fb.control<ShoppingBasis>('planned'),
  });
  readonly manualForm = this.fb.group({
    label: ['', [Validators.required, Validators.maxLength(200)]],
    quantityText: ['', Validators.maxLength(200)],
    kind: this.fb.control<QuantityKind>('unknown'),
    unit: this.fb.control<IngredientUnit>('item'),
    measurementStandard: this.fb.control<MeasurementStandard>('AU'),
    categoryId: '',
  });
  readonly itemForm = this.fb.group({
    label: '',
    quantityText: ['', Validators.maxLength(200)],
    kind: this.fb.control<QuantityKind>('numeric'),
    unit: this.fb.control<IngredientUnit>('g'),
    measurementStandard: this.fb.control<MeasurementStandard>('AU'),
  });
  readonly renameForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
  });
  readonly recipeQuery = this.fb.control('');
  readonly groups = computed(() => {
    const groups = new Map<string, ShoppingItem[]>();
    for (const item of this.current()?.items ?? []) {
      if (item.removed && !this.showRemoved()) continue;
      const name = item.categoryName ?? 'Other';
      groups.set(name, [...(groups.get(name) ?? []), item]);
    }
    return [...groups]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([name, items]) => ({ name, items }));
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.generation++;
      this.recipesGeneration++;
    });
    this.creationForm.valueChanges.subscribe(() => this.invalidatePreview());
    this.defaultDates('UTC');
  }
  async initialize(): Promise<void> {
    try {
      const response = await firstValueFrom(
        this.api.read<components['schemas']['HouseholdSettingsRead']>('/api/v1/household/settings'),
      );
      if (!this.destroyed && response.body && !this.creationForm.dirty)
        this.defaultDates(response.body.timezone);
    } catch (error) {
      if (!this.destroyed) this.error.set(apiMessage(error));
    }
    await this.loadIndex();
    this.initializing.set(false);
  }
  private defaultDates(timezone: string): void {
    const monday = mondayOf(householdToday(timezone));
    this.creationForm.patchValue({ from: monday, to: addDays(monday, 6) }, { emitEvent: false });
  }
  async loadIndex(append = false): Promise<void> {
    await this.perform(async () => {
      const cursor = append ? this.nextCursor() : null;
      const result = await firstValueFrom(
        this.api.read<components['schemas']['ShoppingListPageRead']>(
          `/api/v1/shopping-lists?limit=25${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
        ),
      );
      if (!result.body) return;
      const map = new Map((append ? this.lists() : []).map((item) => [item.id, item]));
      for (const item of result.body.items) map.set(item.id, item);
      this.lists.set([...map.values()]);
      this.nextCursor.set(result.body.nextCursor);
    }, true);
  }
  async open(id: string): Promise<void> {
    if (this.busy() || !this.discardAllowed()) return;
    const generation = ++this.generation;
    this.resetEditor();
    this.current.set(null);
    this.tag = '';
    this.deletedRemotely.set(false);
    this.sourcesChanged.set(false);
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.read<ShoppingList>(`/api/v1/shopping-lists/${id}`),
      );
      if (generation === this.generation) this.accept(response);
    });
  }
  async refresh(): Promise<void> {
    const current = this.current();
    if (!current || this.busy()) return;
    if (!this.discardAllowed()) return;
    this.resetEditor();
    await this.readCurrent(current.id);
  }
  private async readCurrent(id: string): Promise<void> {
    await this.perform(async () => {
      try {
        this.accept(
          await firstValueFrom(this.api.read<ShoppingList>(`/api/v1/shopping-lists/${id}`)),
        );
      } catch (error) {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.deletedRemotely.set(true);
          this.outdated.set(true);
        }
        throw error;
      }
    }, true);
  }
  beginCreate(): void {
    if (this.busy() || !this.discardAllowed()) return;
    this.resetEditor();
    this.creationForm.controls.name.reset('');
    this.creationForm.controls.basis.reset('planned');
    this.sourceKind.set('mealRange');
    this.recipeEntries.set([]);
    this.editor.set('create');
  }
  changeKind(kind: 'mealRange' | 'recipes' | 'manual'): void {
    this.sourceKind.set(kind);
    this.creationForm.markAsDirty();
    this.invalidatePreview();
  }
  setServings(id: string, value: string): void {
    this.recipeEntries.update((rows) =>
      rows.map((row) => (row.recipeId === id ? { ...row, servings: value } : row)),
    );
    this.entriesDirty = true;
    this.invalidatePreview();
  }
  addRecipe(recipe: components['schemas']['RecipeRead']): void {
    if (this.recipeEntries().some((row) => row.recipeId === recipe.id)) return;
    this.recipeEntries.update((rows) => [
      ...rows,
      { recipeId: recipe.id, title: recipe.title, servings: recipe.defaultServings },
    ]);
    this.entriesDirty = true;
    this.invalidatePreview();
  }
  removeRecipe(id: string): void {
    this.recipeEntries.update((rows) => rows.filter((row) => row.recipeId !== id));
    this.entriesDirty = true;
    this.invalidatePreview();
  }
  async searchRecipes(append = false): Promise<void> {
    const generation = ++this.recipesGeneration;
    try {
      const result = await firstValueFrom(
        this.api.read<components['schemas']['RecipeListPage']>(
          `/api/v1/recipes?limit=25&q=${encodeURIComponent(this.recipeQuery.value)}${append && this.recipeCursor ? '&cursor=' + encodeURIComponent(this.recipeCursor) : ''}`,
        ),
      );
      if (this.destroyed || generation !== this.recipesGeneration || !result.body) return;
      const map = new Map((append ? this.recipes() : []).map((row) => [row.id, row]));
      for (const row of result.body.items) map.set(row.id, row);
      this.recipes.set([...map.values()]);
      this.recipeCursor = result.body.nextCursor ?? undefined;
      this.moreRecipes.set(!!this.recipeCursor);
    } catch (error) {
      if (!this.destroyed && generation === this.recipesGeneration)
        this.error.set(apiMessage(error));
    }
  }
  private selection(): ShoppingSelection {
    if (this.sourceKind() === 'recipes')
      return {
        kind: 'recipes',
        entries: this.recipeEntries().map(({ recipeId, servings }) => ({ recipeId, servings })),
      };
    return {
      kind: 'mealRange',
      from: this.creationForm.controls.from.value,
      to: this.creationForm.controls.to.value,
    };
  }
  async create(): Promise<void> {
    if (this.creationForm.invalid) {
      this.creationForm.markAllAsTouched();
      return;
    }
    await this.perform(async () => {
      const manual = this.sourceKind() === 'manual';
      const body = manual
        ? { name: this.creationForm.controls.name.value }
        : {
            name: this.creationForm.controls.name.value,
            selection: this.selection(),
            basis:
              this.sourceKind() === 'recipes' ? 'planned' : this.creationForm.controls.basis.value,
          };
      this.accept(
        await firstValueFrom(
          this.api.write<ShoppingList>(
            'POST',
            manual ? '/api/v1/shopping-lists' : '/api/v1/shopping-lists/generate',
            body,
            undefined,
            (this.retry ??= crypto.randomUUID()),
          ),
        ),
      );
      this.resetEditor();
      this.notice.set('Shopping list created.');
    });
  }
  beginManual(): void {
    if (this.busy() || !this.discardAllowed()) return;
    this.resetEditor();
    this.manualForm.reset({
      label: '',
      quantityText: '',
      kind: 'unknown',
      unit: 'item',
      measurementStandard: 'AU',
      categoryId: '',
    });
    this.editor.set('manual');
  }
  async loadCategories(): Promise<void> {
    try {
      let cursor: string | null = null;
      const values: components['schemas']['CategoryRead'][] = [];
      do {
        const response: HttpResponse<components['schemas']['CategoryPage']> = await firstValueFrom(
          this.api.read<components['schemas']['CategoryPage']>(
            `/api/v1/ingredient-categories?limit=100${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
          ),
        );
        if (this.destroyed || !response.body) return;
        values.push(...response.body.items);
        cursor = response.body.nextCursor ?? null;
      } while (cursor);
      this.categories.set(values);
    } catch (error) {
      if (!this.destroyed) this.error.set(apiMessage(error));
    }
  }
  async saveManual(): Promise<void> {
    const current = this.current();
    if (!current || this.manualForm.invalid) {
      this.manualForm.markAllAsTouched();
      return;
    }
    await this.perform(async () => {
      const value = this.manualForm.getRawValue();
      this.accept(
        await firstValueFrom(
          this.api.write<ShoppingList>(
            'POST',
            `/api/v1/shopping-lists/${current.id}/items`,
            { ...value, categoryId: value.categoryId || null },
            this.tag,
            (this.retry ??= crypto.randomUUID()),
          ),
        ),
      );
      this.resetEditor();
      this.notice.set('Manual item added.');
    });
  }
  beginEdit(item: ShoppingItem): void {
    if (this.busy() || !this.discardAllowed()) return;
    this.resetEditor();
    this.editingItem.set(item);
    const quantity = item.quantityOverride;
    const exact = item.effectiveExact;
    this.itemForm.reset({
      label: item.label,
      quantityText:
        quantity?.quantityText ??
        (exact
          ? exact.denominator === '1'
            ? exact.numerator
            : `${exact.numerator}/${exact.denominator}`
          : (item.contributions[0]?.quantityText ?? item.displayText)),
      kind: item.unresolved ? 'unknown' : 'numeric',
      unit:
        quantity?.unit ??
        (item.dimension === 'Mass' ? 'g' : item.dimension === 'Volume' ? 'ml' : 'item'),
      measurementStandard: quantity?.measurementStandard ?? 'AU',
    });
    this.editInitial = JSON.stringify(this.itemForm.getRawValue());
    this.editor.set('item');
  }
  async saveItem(): Promise<void> {
    const current = this.current(),
      item = this.editingItem();
    if (!current || !item || this.itemForm.invalid) return;
    await this.perform(async () => {
      const value = this.itemForm.getRawValue();
      const initial = JSON.parse(this.editInitial) as typeof value;
      const changedQuantity = ['quantityText', 'kind', 'unit', 'measurementStandard'].some(
        (key) => value[key as keyof typeof value] !== initial[key as keyof typeof value],
      );
      const body: components['schemas']['ShoppingItemWrite'] = {
        clearOverride: false,
        ...(changedQuantity
          ? {
              quantityOverride: {
                quantityText: value.quantityText,
                kind: value.kind,
                unit: value.unit,
                measurementStandard: value.measurementStandard,
              },
            }
          : {}),
        ...(item.manual && value.label !== item.label ? { label: value.label } : {}),
      };
      this.accept(
        await firstValueFrom(
          this.api.write<ShoppingList>(
            'PUT',
            `/api/v1/shopping-lists/${current.id}/items/${item.id}`,
            body,
            this.tag,
          ),
        ),
      );
      this.resetEditor();
      this.notice.set('Shopping item saved.');
    });
  }
  async purchase(item: ShoppingItem, purchased: boolean): Promise<void> {
    await this.itemWrite(item, { clearOverride: false, purchased });
  }
  async clearOverride(item: ShoppingItem): Promise<void> {
    if (this.discardAllowed()) {
      this.resetEditor();
      await this.itemWrite(item, { clearOverride: true });
    }
  }
  async restore(item: ShoppingItem): Promise<void> {
    await this.itemWrite(item, { clearOverride: false, removed: false });
  }
  private async itemWrite(
    item: ShoppingItem,
    body: components['schemas']['ShoppingItemWrite'],
  ): Promise<void> {
    const current = this.current();
    if (!current || this.hasUnsavedChanges()) return;
    await this.perform(async () => {
      this.accept(
        await firstValueFrom(
          this.api.write<ShoppingList>(
            'PUT',
            `/api/v1/shopping-lists/${current.id}/items/${item.id}`,
            body,
            this.tag,
          ),
        ),
      );
      this.notice.set('Shopping list updated.');
    });
  }
  async remove(item: ShoppingItem): Promise<void> {
    const current = this.current();
    if (!current || !this.discardAllowed()) return;
    this.resetEditor();
    await this.perform(async () => {
      this.accept(
        await firstValueFrom(
          this.api.write<ShoppingList>(
            'DELETE',
            `/api/v1/shopping-lists/${current.id}/items/${item.id}`,
            undefined,
            this.tag,
          ),
        ),
      );
      this.notice.set('Item removed.');
    });
  }
  beginRename(): void {
    const current = this.current();
    if (!current || !this.discardAllowed()) return;
    this.resetEditor();
    this.renameForm.reset({ name: current.name });
    this.editor.set('rename');
  }
  async rename(): Promise<void> {
    const current = this.current();
    if (!current || this.renameForm.invalid) return;
    await this.perform(async () => {
      this.accept(
        await firstValueFrom(
          this.api.write<ShoppingList>(
            'PUT',
            `/api/v1/shopping-lists/${current.id}`,
            this.renameForm.getRawValue(),
            this.tag,
          ),
        ),
      );
      this.resetEditor();
      this.notice.set('Shopping list renamed.');
    });
  }
  async deleteList(): Promise<void> {
    const current = this.current();
    if (
      !current ||
      !this.discardAllowed() ||
      !window.confirm('Delete this shopping list and its items?')
    )
      return;
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write('DELETE', `/api/v1/shopping-lists/${current.id}`, undefined, this.tag),
      );
      this.resetEditor();
      this.current.set(null);
      this.lists.update((rows) => rows.filter((row) => row.id !== current.id));
      this.notice.set('Shopping list deleted.');
    });
  }
  beginReconcile(): void {
    const current = this.current();
    if (!current?.selection || this.busy() || !this.discardAllowed()) return;
    this.resetEditor();
    this.sourceKind.set(current.selection.kind);
    this.creationForm.patchValue(
      {
        name: current.name,
        from: current.selection.from ?? this.creationForm.controls.from.value,
        to: current.selection.to ?? this.creationForm.controls.to.value,
        basis: current.basis,
      },
      { emitEvent: false },
    );
    this.creationForm.markAsPristine();
    const names = new Map(
      current.items.flatMap((row) =>
        row.contributions.map((source) => [source.sourceRecipeId, source.sourceLabel] as const),
      ),
    );
    this.recipeEntries.set(
      (current.selection.entries ?? []).map((row) => ({
        ...row,
        title: names.get(row.recipeId) ?? 'Selected recipe',
      })),
    );
    this.editor.set('reconcile');
  }
  async previewChanges(): Promise<void> {
    const current = this.current();
    if (!current) return;
    const generation = this.sourceGeneration;
    await this.perform(async () => {
      const body: components['schemas']['ShoppingPreviewWrite'] = {
        selection: this.selection(),
        basis: this.sourceKind() === 'recipes' ? 'planned' : this.creationForm.controls.basis.value,
      };
      const response = await firstValueFrom(
        this.api.write<ShoppingPreview>(
          'POST',
          `/api/v1/shopping-lists/${current.id}/reconciliation/preview`,
          body,
          this.tag,
        ),
      );
      if (!response.body) return;
      const previous = this.choices();
      const values: Record<string, ShoppingChoice> = {};
      for (const change of response.body.changes) {
        const old = previous[change.key];
        values[change.key] =
          old && change.allowedChoices.includes(old.choice)
            ? old
            : { key: change.key, choice: change.defaultChoice };
      }
      this.preview.set(response.body);
      this.choices.set(values);
      this.reviewRequired.set(this.reviewRequired() || this.outdated() || this.sourcesChanged());
      this.reviewed.set(false);
      this.sourcesChanged.set(generation !== this.sourceGeneration);
    });
  }
  choose(key: string, choice: ShoppingChoice['choice']): void {
    this.choices.update((values) => ({ ...values, [key]: { ...values[key], key, choice } }));
  }
  choosePurchased(key: string, purchased: boolean | null): void {
    this.choices.update((values) => {
      const next = { ...values[key] };
      if (purchased === null) delete next.purchased;
      else next.purchased = purchased;
      return { ...values, [key]: next };
    });
  }
  async reloadReview(): Promise<void> {
    const current = this.current();
    if (!current || this.busy()) return;
    await this.perform(async () => {
      this.accept(
        await firstValueFrom(this.api.read<ShoppingList>(`/api/v1/shopping-lists/${current.id}`)),
      );
      this.preview.set(null);
      this.reviewRequired.set(true);
      this.reviewed.set(false);
    });
  }
  async applyChanges(): Promise<void> {
    const current = this.current(),
      preview = this.preview();
    if (!current || !preview || (this.reviewRequired() && !this.reviewed())) return;
    if (
      preview.changes.some(
        (change) =>
          change.purchaseDecisionRequired &&
          this.choices()[change.key]?.choice === 'acceptNew' &&
          this.choices()[change.key]?.purchased === undefined,
      )
    ) {
      this.error.set('Choose whether each increased requirement has already been purchased.');
      return;
    }
    const generation = this.sourceGeneration;
    await this.perform(async () => {
      const body: components['schemas']['ShoppingApplyWrite'] = {
        listRevision: preview.listRevision,
        householdSourceRevision: preview.householdSourceRevision,
        sourceRevisions: preview.sourceRevisions,
        policyVersion: preview.policyVersion,
        selection: preview.selection,
        basis: preview.basis,
        choices: preview.changes.map((change) => this.choices()[change.key]),
      };
      this.accept(
        await firstValueFrom(
          this.api.write<ShoppingList>(
            'POST',
            `/api/v1/shopping-lists/${current.id}/reconciliation/apply`,
            body,
            this.tag,
          ),
        ),
      );
      this.resetEditor();
      this.sourcesChanged.set(generation !== this.sourceGeneration);
      this.notice.set('Reviewed requirements applied.');
    });
  }
  markSourcesChanged(): void {
    this.sourceGeneration++;
    if (this.current()?.selection) {
      this.sourcesChanged.set(true);
      if (this.preview()) {
        // A delayed notification may describe changes already included in this
        // preview. Preserve its explicit review; the backend validates its exact
        // source revisions transactionally and rejects genuinely stale apply.
        this.reviewRequired.set(true);
      }
    }
  }
  async remoteChange(id: string, revision: string, deleted: boolean): Promise<void> {
    const current = this.current();
    if (!current || current.id !== id || !newerRevision(revision, current.revision)) return;
    this.outdated.set(true);
    if (deleted) this.deletedRemotely.set(true);
    if (this.hasUnsavedChanges() || this.editor() === 'reconcile') return;
    if (this.busy()) {
      this.pendingRemote = true;
      return;
    }
    if (!deleted) await this.readCurrent(id);
  }
  async recoverRemote(): Promise<void> {
    const id = this.current()?.id;
    if (id) {
      if (this.hasUnsavedChanges() || this.editor() === 'reconcile') this.outdated.set(true);
      else if (this.busy()) this.pendingRemote = true;
      else await this.readCurrent(id);
    }
    if (!this.busy()) await this.loadIndex();
  }
  cancelEditor(): void {
    if (this.discardAllowed()) this.resetEditor();
  }
  hasUnsavedChanges(): boolean {
    return (
      (this.editor() === 'manual' && this.manualForm.dirty) ||
      (this.editor() === 'item' && this.itemForm.dirty) ||
      (this.editor() === 'rename' && this.renameForm.dirty) ||
      ((this.editor() === 'create' || this.editor() === 'reconcile') &&
        (this.creationForm.dirty || this.entriesDirty)) ||
      this.preview() !== null
    );
  }
  discardAllowed(): boolean {
    return !this.hasUnsavedChanges() || window.confirm('Discard unsaved shopping changes?');
  }
  private invalidatePreview(): void {
    this.preview.set(null);
    this.reviewed.set(false);
  }
  private resetEditor(): void {
    this.editor.set(null);
    this.editingItem.set(null);
    this.preview.set(null);
    this.choices.set({});
    this.reviewRequired.set(false);
    this.reviewed.set(false);
    this.retry = undefined;
    this.entriesDirty = false;
    this.creationForm.markAsPristine();
    this.manualForm.markAsPristine();
    this.itemForm.markAsPristine();
    this.renameForm.markAsPristine();
    this.notice.set('');
    this.error.set('');
  }
  private accept(response: HttpResponse<ShoppingList>): void {
    if (this.destroyed || !response.body) return;
    const tag = response.headers.get('ETag');
    if (!tag) throw new Error('The server did not return a list revision. Reload before saving.');
    this.current.set(response.body);
    this.tag = tag;
    this.outdated.set(false);
    this.deletedRemotely.set(false);
    const { id, revision, name, basis, summary } = response.body;
    this.lists.update((rows) => [
      ...rows.filter((row) => row.id !== id),
      { id, revision, name, basis, summary },
    ]);
  }
  private async perform(action: () => Promise<void>, background = false): Promise<void> {
    if (this.busy() || this.destroyed) return;
    this.busy.set(true);
    if (!background) {
      this.error.set('');
      this.notice.set('');
    }
    try {
      await action();
    } catch (error) {
      if (!this.destroyed) {
        this.error.set(apiMessage(error));
        if (error instanceof HttpErrorResponse && (error.status === 409 || error.status === 412)) {
          this.outdated.set(true);
          this.reviewRequired.set(true);
          this.reviewed.set(false);
        }
      }
    } finally {
      this.busy.set(false);
      if (this.pendingRemote && !this.hasUnsavedChanges() && !this.destroyed) {
        this.pendingRemote = false;
        const id = this.current()?.id;
        if (id)
          queueMicrotask(() => {
            void this.readCurrent(id);
          });
      }
    }
  }
}
