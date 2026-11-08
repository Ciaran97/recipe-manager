import { Component, computed, inject, signal } from '@angular/core';
import { HttpResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';

type Ingredient = components['schemas']['IngredientRead'];
type Alias = components['schemas']['IngredientAliasRead'];
type Category = components['schemas']['CategoryRead'];

@Component({
  selector: 'app-catalogue-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './catalogue-page.html',
  styles: `
    .catalogue-layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1.3fr);
      gap: 1.25rem;
      align-items: start;
    }
    .ingredient-list {
      list-style: none;
      padding: 0;
    }
    .ingredient-list button {
      width: 100%;
      text-align: left;
    }
    .ingredient-list small {
      display: block;
      font-weight: 400;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    @media (max-width: 720px) {
      .catalogue-layout {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class CataloguePage {
  private readonly api = inject(ApiClient);
  private readonly fb = inject(FormBuilder);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly items = signal<Ingredient[]>([]);
  readonly categories = signal<Category[]>([]);
  readonly sortedCategories = computed(() =>
    [...this.categories()].sort(
      (a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name),
    ),
  );
  readonly selected = signal<Ingredient | null>(null);
  readonly selectedCategory = signal<Category | null>(null);
  readonly editingAlias = signal<Alias | null>(null);
  readonly nextCursor = signal<string | null>(null);
  readonly searchForm = this.fb.nonNullable.group({ query: ['', Validators.maxLength(200)] });
  readonly ingredientForm = this.fb.nonNullable.group({
    canonicalName: ['', [Validators.required, Validators.maxLength(200)]],
    categoryId: [''],
    retainOldNameAsAlias: [false],
  });
  readonly aliasForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
  });
  readonly categoryForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    sortOrder: [
      0,
      [
        Validators.required,
        Validators.min(0),
        Validators.max(2147483647),
        Validators.pattern(/^\d+$/),
      ],
    ],
  });
  private ingredientEtag = '';
  private categoryEtag = '';
  private ingredientRetry?: string;
  private aliasRetry?: string;
  private categoryRetry?: string;

  constructor() {
    void this.load();
  }
  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      await Promise.all([this.loadIngredients(false), this.loadCategories()]);
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.loading.set(false);
    }
  }
  private async loadIngredients(append: boolean): Promise<void> {
    const query = this.searchForm.controls.query.value.trim();
    const cursor = append ? this.nextCursor() : null;
    const response = await firstValueFrom(
      this.api.read<components['schemas']['IngredientPage']>(
        `/api/v1/ingredients?limit=25${query ? '&q=' + encodeURIComponent(query) : ''}${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
      ),
    );
    if (!response.body) return;
    const merged = new Map((append ? this.items() : []).map((item) => [item.id, item]));
    for (const item of response.body.items) merged.set(item.id, item);
    this.items.set([...merged.values()]);
    this.nextCursor.set(response.body.nextCursor ?? null);
  }
  private async loadCategories(): Promise<void> {
    const categories: Category[] = [];
    let cursor: string | null = null;
    do {
      const response: HttpResponse<components['schemas']['CategoryPage']> = await firstValueFrom(
        this.api.read<components['schemas']['CategoryPage']>(
          `/api/v1/ingredient-categories?limit=100${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
        ),
      );
      categories.push(...(response.body?.items ?? []));
      cursor = response.body?.nextCursor ?? null;
    } while (cursor);
    this.categories.set(categories);
  }
  async search(append = false): Promise<void> {
    if (this.busy() || this.searchForm.invalid) return;
    await this.perform(() => this.loadIngredients(append));
  }
  detailsDirty(): boolean {
    const value = this.ingredientForm.getRawValue();
    const selected = this.selected();
    return (
      value.canonicalName !== (selected?.canonicalName ?? '') ||
      value.categoryId !== (selected?.categoryId ?? '') ||
      value.retainOldNameAsAlias
    );
  }
  private canDiscard(): boolean {
    return (
      (!this.detailsDirty() &&
        this.aliasForm.controls.name.value === (this.editingAlias()?.name ?? '')) ||
      window.confirm('Discard unsaved ingredient changes?')
    );
  }
  newIngredient(): void {
    if (this.busy() || !this.canDiscard()) return;
    this.selected.set(null);
    this.ingredientEtag = '';
    this.ingredientRetry = undefined;
    this.ingredientForm.reset({ canonicalName: '', categoryId: '', retainOldNameAsAlias: false });
    this.clearAlias();
    this.error.set('');
    this.notice.set('');
  }
  async choose(id: string, discard = false): Promise<void> {
    if (this.busy() || (!discard && !this.canDiscard())) return;
    await this.perform(async () => {
      const response = await firstValueFrom(this.api.read<Ingredient>(`/api/v1/ingredients/${id}`));
      this.accept(response);
      this.clearAlias();
    });
  }
  async saveIngredient(): Promise<void> {
    if (this.busy() || this.ingredientForm.invalid) {
      this.ingredientForm.markAllAsTouched();
      return;
    }
    const current = this.selected();
    const value = this.ingredientForm.getRawValue();
    await this.perform(async () => {
      const response = current
        ? await firstValueFrom(
            this.api.write<Ingredient>(
              'PUT',
              `/api/v1/ingredients/${current.id}`,
              {
                canonicalName: value.canonicalName,
                categoryId: value.categoryId || null,
                retainOldNameAsAlias: value.retainOldNameAsAlias,
              },
              this.ingredientEtag,
            ),
          )
        : await firstValueFrom(
            this.api.write<Ingredient>(
              'POST',
              '/api/v1/ingredients',
              { canonicalName: value.canonicalName, categoryId: value.categoryId || null },
              undefined,
              (this.ingredientRetry ??= crypto.randomUUID()),
            ),
          );
      this.accept(response);
      this.ingredientRetry = undefined;
      this.notice.set('Ingredient saved.');
    });
  }
  confirmDeleteIngredient(): void {
    const selected = this.selected();
    if (
      selected &&
      window.confirm(
        `Delete ${selected.canonicalName}? Ingredients used by recipes or planned meals cannot be deleted.`,
      )
    )
      void this.deleteIngredient();
  }
  async deleteIngredient(): Promise<void> {
    const selected = this.selected();
    if (!selected || this.busy()) return;
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write(
          'DELETE',
          `/api/v1/ingredients/${selected.id}`,
          undefined,
          this.ingredientEtag,
        ),
      );
      this.items.update((items) => items.filter((item) => item.id !== selected.id));
      this.selected.set(null);
      this.ingredientForm.reset({ canonicalName: '', categoryId: '', retainOldNameAsAlias: false });
      this.clearAlias();
      this.notice.set('Ingredient deleted.');
    });
  }
  editAlias(alias: Alias): void {
    this.editingAlias.set(alias);
    this.aliasForm.reset({ name: alias.name });
    this.aliasRetry = undefined;
  }
  clearAlias(): void {
    this.editingAlias.set(null);
    this.aliasForm.reset({ name: '' });
    this.aliasRetry = undefined;
  }
  async saveAlias(): Promise<void> {
    const current = this.selected();
    if (!current || this.busy() || this.aliasForm.invalid) {
      this.aliasForm.markAllAsTouched();
      return;
    }
    if (this.detailsDirty()) {
      this.error.set('Save or discard ingredient details before changing aliases.');
      return;
    }
    const alias = this.editingAlias();
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.write<Ingredient>(
          alias ? 'PUT' : 'POST',
          `/api/v1/ingredients/${current.id}/aliases${alias ? '/' + alias.id : ''}`,
          { name: this.aliasForm.controls.name.value },
          this.ingredientEtag,
          alias ? undefined : (this.aliasRetry ??= crypto.randomUUID()),
        ),
      );
      this.accept(response);
      this.clearAlias();
      this.notice.set('Alias saved.');
    });
  }
  async removeAlias(alias: Alias): Promise<void> {
    const current = this.selected();
    if (!current || this.busy()) return;
    if (this.detailsDirty()) {
      this.error.set('Save or discard ingredient details before changing aliases.');
      return;
    }
    await this.perform(async () => {
      this.accept(
        await firstValueFrom(
          this.api.write<Ingredient>(
            'DELETE',
            `/api/v1/ingredients/${current.id}/aliases/${alias.id}`,
            undefined,
            this.ingredientEtag,
          ),
        ),
      );
      this.clearAlias();
      this.notice.set('Alias removed.');
    });
  }
  newCategory(discard = false): void {
    if (!discard && this.categoryForm.dirty && !window.confirm('Discard unsaved category changes?'))
      return;
    this.selectedCategory.set(null);
    this.categoryForm.reset({ name: '', sortOrder: 0 });
    this.categoryRetry = undefined;
  }
  async chooseCategory(id: string): Promise<void> {
    if (
      this.busy() ||
      (this.categoryForm.dirty && !window.confirm('Discard unsaved category changes?'))
    )
      return;
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.read<Category>(`/api/v1/ingredient-categories/${id}`),
      );
      if (response.body) {
        this.selectedCategory.set(response.body);
        this.categoryEtag = response.headers.get('ETag') ?? '';
        this.categoryForm.reset({ name: response.body.name, sortOrder: response.body.sortOrder });
      }
    });
  }
  async saveCategory(): Promise<void> {
    if (this.busy() || this.categoryForm.invalid) {
      this.categoryForm.markAllAsTouched();
      return;
    }
    const selected = this.selectedCategory();
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.write<Category>(
          selected ? 'PUT' : 'POST',
          selected
            ? `/api/v1/ingredient-categories/${selected.id}`
            : '/api/v1/ingredient-categories',
          this.categoryForm.getRawValue(),
          selected ? this.categoryEtag : undefined,
          selected ? undefined : (this.categoryRetry ??= crypto.randomUUID()),
        ),
      );
      if (response.body) {
        const item = response.body;
        this.categories.update((items) => [...items.filter((x) => x.id !== item.id), item]);
        this.selectedCategory.set(item);
        this.categoryEtag = response.headers.get('ETag') ?? '';
        this.categoryForm.reset({ name: item.name, sortOrder: item.sortOrder });
        this.categoryRetry = undefined;
      }
      this.notice.set(
        'Category saved. Reload an open ingredient before making further changes to it.',
      );
    });
  }
  confirmDeleteCategory(): void {
    const category = this.selectedCategory();
    if (
      category &&
      window.confirm(`Delete ${category.name}? Its ingredients will move to Uncategorized.`)
    )
      void this.deleteCategory();
  }
  async deleteCategory(): Promise<void> {
    const category = this.selectedCategory();
    if (!category || this.busy()) return;
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write(
          'DELETE',
          `/api/v1/ingredient-categories/${category.id}`,
          undefined,
          this.categoryEtag,
        ),
      );
      this.categories.update((items) => items.filter((x) => x.id !== category.id));
      this.newCategory(true);
      this.notice.set(
        'Category deleted. Its ingredients are now Uncategorized. Reload an open ingredient before editing it.',
      );
    });
  }
  private accept(response: HttpResponse<Ingredient>): void {
    if (!response.body) return;
    const ingredient = response.body;
    this.selected.set(ingredient);
    this.ingredientEtag = response.headers.get('ETag') ?? '';
    this.ingredientForm.reset({
      canonicalName: ingredient.canonicalName,
      categoryId: ingredient.categoryId ?? '',
      retainOldNameAsAlias: false,
    });
    this.items.update((items) =>
      items.some((item) => item.id === ingredient.id)
        ? items.map((item) => (item.id === ingredient.id ? ingredient : item))
        : [ingredient, ...items],
    );
  }
  private async perform(action: () => Promise<void>): Promise<void> {
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
