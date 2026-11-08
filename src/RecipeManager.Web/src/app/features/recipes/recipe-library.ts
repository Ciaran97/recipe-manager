import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink, convertToParamMap } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, distinctUntilChanged, firstValueFrom, map, of } from 'rxjs';
import { HouseholdSync } from '../../core/sync/household-sync';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';

import { Icon } from '../../core/ui/icon';
import { LibraryNavigation } from '../../core/session/library-navigation';
import { OrganiserPicker } from './recipe-filters';

type Recipe = components['schemas']['RecipeRead'];
type Line = components['schemas']['RecipeIngredientRead'];
type Preview = components['schemas']['RecipeServingPreview'];

@Component({
  selector: 'app-recipe-library',
  imports: [ReactiveFormsModule, RouterLink, OrganiserPicker, Icon],
  templateUrl: './recipe-library.html',
  styleUrl: './recipe-library.scss',
})
export class RecipeLibrary {
  private readonly api = inject(ApiClient);
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly remoteChanged = signal(false);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly items = signal<Recipe[]>([]);
  readonly selected = signal<Recipe | null>(null);
  readonly selectedId = signal<string | null>(null);
  readonly nextCursor = signal<string | null>(null);
  readonly preview = signal<Preview | null>(null);
  readonly showOrganisers = signal(false);
  private readonly navigation = inject(LibraryNavigation);
  readonly activeCollection = signal<string | null>(null);
  readonly collectionTitle = computed(
    () => this.navigation.collections().find((item) => item.id === this.activeCollection())?.name,
  );
  readonly browseTitle = signal('All recipes');
  private listSelection = '';
  readonly filters = this.fb.nonNullable.group({
    query: ['', Validators.maxLength(200)],
    archived: ['exclude'],
    favouriteOnly: [false],
    collectionIds: this.fb.nonNullable.control<string[]>([]),
    tagIds: this.fb.nonNullable.control<string[]>([]),
  });
  readonly servings = this.fb.nonNullable.group({
    target: ['', [Validators.required, Validators.pattern(/^(?:\d+(?:\.\d{0,6})?|\.\d{1,6})$/)]],
  });
  private etag = '';
  private duplicateRetry?: string;
  private loadGeneration = 0;
  private listGeneration = 0;

  constructor() {
    const sync = inject(HouseholdSync);
    effect(() => {
      const change = sync.change();
      if (!change || !(change.personal || change.domains.includes('recipesRevision'))) return;
      untracked(() => {
        if (this.loading() || this.busy() || this.servings.dirty) this.remoteChanged.set(true);
        else void this.reload();
      });
    });
    combineLatest([this.route.paramMap, this.route.queryParamMap ?? of(convertToParamMap({}))])
      .pipe(
        map(([parameters, query]) => ({
          id: parameters.get('id'),
          query: query.get('q') ?? '',
          favourite: query.get('favouriteOnly') === 'true',
          collection: query.get('collection'),
        })),
        distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
        takeUntilDestroyed(),
      )
      .subscribe((parameters) => {
        this.selectedId.set(parameters.id);
        this.activeCollection.set(parameters.collection);
        this.filters.patchValue({
          query: parameters.query,
          favouriteOnly: parameters.favourite,
          collectionIds: parameters.collection ? [parameters.collection] : [],
          tagIds: [],
        });
        this.browseTitle.set(
          parameters.favourite
            ? 'Favourites'
            : parameters.collection
              ? 'Collection recipes'
              : 'All recipes',
        );
        this.preview.set(null);
        this.error.set('');
        void this.reload();
      });
  }
  async reload(): Promise<void> {
    this.remoteChanged.set(false);
    const generation = ++this.loadGeneration;
    this.loading.set(true);
    this.error.set('');
    try {
      const id = this.selectedId();
      if (id) {
        const response = await firstValueFrom(this.api.read<Recipe>(`/api/v1/recipes/${id}`));
        if (generation !== this.loadGeneration || id !== this.selectedId()) return;
        if (response.body) {
          this.selected.set(response.body);
          this.etag = response.headers.get('ETag') ?? '';
          this.servings.reset({ target: response.body.defaultServings });
          this.preview.set(null);
        }
      } else {
        this.selected.set(null);
        await this.loadList(false);
      }
    } catch (error) {
      if (generation === this.loadGeneration) this.error.set(apiMessage(error));
    } finally {
      if (generation === this.loadGeneration) this.loading.set(false);
    }
  }
  private async loadList(append: boolean): Promise<void> {
    const generation = ++this.listGeneration;
    const value = this.filters.getRawValue();
    const selection = JSON.stringify(value);
    const useAppend = append && selection === this.listSelection;
    const cursor = useAppend ? this.nextCursor() : null;
    const organisers =
      value.collectionIds.map((id) => '&collectionIds=' + encodeURIComponent(id)).join('') +
      value.tagIds.map((id) => '&tagIds=' + encodeURIComponent(id)).join('');
    const result = await firstValueFrom(
      this.api.read<components['schemas']['RecipeListPage']>(
        `/api/v1/recipes?limit=25&archived=${value.archived}&favouriteOnly=${value.favouriteOnly}${value.query.trim() ? '&q=' + encodeURIComponent(value.query.trim()) : ''}${organisers}${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
      ),
    );
    if (!result.body || generation !== this.listGeneration) return;
    const items = new Map((useAppend ? this.items() : []).map((item) => [item.id, item]));
    for (const item of result.body.items) items.set(item.id, item);
    this.items.set([...items.values()]);
    this.nextCursor.set(result.body.nextCursor ?? null);
    this.listSelection = selection;
  }
  clearFilters(): void {
    this.activeCollection.set(null);
    this.browseTitle.set('All recipes');
    this.filters.reset({
      query: '',
      archived: 'exclude',
      favouriteOnly: false,
      collectionIds: [],
      tagIds: [],
    });
    void this.search();
  }
  async search(append = false): Promise<void> {
    if (this.busy() || this.filters.invalid) return;
    await this.perform(() => this.loadList(append));
  }
  async favourite(recipe: Recipe): Promise<void> {
    await this.perform(async () => {
      const etag =
        this.selectedId() === recipe.id
          ? this.etag
          : ((
              await firstValueFrom(this.api.read<Recipe>(`/api/v1/recipes/${recipe.id}`))
            ).headers.get('ETag') ?? '');
      const response = await firstValueFrom(
        this.api.write<Recipe>(
          'PUT',
          `/api/v1/recipes/${recipe.id}/favourite`,
          { favourite: !recipe.favourite },
          etag,
        ),
      );
      if (response.body && this.selectedId() === recipe.id) {
        this.selected.set(response.body);
        this.etag = response.headers.get('ETag') ?? '';
      } else await this.loadList(false);
    });
  }
  async archive(): Promise<void> {
    const recipe = this.selected();
    if (!recipe) return;
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.write<Recipe>(
          'PUT',
          `/api/v1/recipes/${recipe.id}/archive`,
          { archived: !recipe.archived },
          this.etag,
        ),
      );
      if (response.body && this.selectedId() === recipe.id) {
        this.selected.set(response.body);
        this.etag = response.headers.get('ETag') ?? '';
      }
    });
  }
  async duplicate(): Promise<void> {
    const recipe = this.selected();
    if (!recipe) return;
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.write<Recipe>(
          'POST',
          `/api/v1/recipes/${recipe.id}/duplicate`,
          {},
          this.etag,
          (this.duplicateRetry ??= crypto.randomUUID()),
        ),
      );
      if (response.body && this.selectedId() === recipe.id) {
        this.duplicateRetry = undefined;
        await this.router.navigate(['/recipes', response.body.id]);
      }
    });
  }
  async remove(): Promise<void> {
    const recipe = this.selected();
    if (
      !recipe ||
      !window.confirm(
        `Delete ${recipe.title}? This cannot be undone. Referenced recipes can be archived instead.`,
      )
    )
      return;
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write('DELETE', `/api/v1/recipes/${recipe.id}`, undefined, this.etag),
      );
      if (this.selectedId() === recipe.id) await this.router.navigate(['/recipes']);
    });
  }
  async scaleServings(): Promise<void> {
    const recipe = this.selected();
    if (!recipe || this.servings.invalid) {
      this.servings.markAllAsTouched();
      return;
    }
    const generation = this.loadGeneration;
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.write<Preview>('POST', `/api/v1/recipes/${recipe.id}/serving-preview`, {
          targetServings: this.servings.controls.target.value,
        }),
      );
      if (response.body && generation === this.loadGeneration && this.selectedId() === recipe.id)
        this.preview.set(response.body);
    });
  }
  ingredientGroups(recipe: Recipe): { id: string; title: string; lines: Line[] }[] {
    const groups = recipe.groups.map((group) => ({
      id: group.id,
      title: group.title,
      lines: recipe.ingredients.filter((row) => row.groupId === group.id),
    }));
    const ungrouped = recipe.ingredients.filter((row) => !row.groupId);
    return ungrouped.length || !groups.length
      ? [{ id: 'ungrouped', title: '', lines: ungrouped }, ...groups]
      : groups;
  }
  quantity(row: Line): string {
    const scaled = this.preview()?.ingredients.find((line) => line.id === row.id);
    return scaled
      ? scaled.displayText || 'Quantity not specified'
      : row.kind === 'unknown'
        ? row.quantityText || 'Quantity not specified'
        : row.quantityText + ' ' + row.unit;
  }
  scaledLine(id: string): Preview['ingredients'][number] | undefined {
    return this.preview()?.ingredients.find((line) => line.id === id);
  }
  private async perform(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
