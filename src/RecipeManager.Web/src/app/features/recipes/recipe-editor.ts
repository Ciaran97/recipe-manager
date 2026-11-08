import { OrganiserPicker } from './recipe-filters';
import {
  afterNextRender,
  Component,
  computed,
  viewChildren,
  ElementRef,
  HostListener,
  inject,
  input,
  effect,
  untracked,
  Injector,
  signal,
} from '@angular/core';
import {
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, debounceTime, firstValueFrom, map, of, Subject, switchMap } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';

type Ingredient = components['schemas']['IngredientRead'];
type Recipe = components['schemas']['RecipeRead'];
type Write = components['schemas']['RecipeWrite'];
type Unit = components['schemas']['IngredientUnit'];
type Kind = components['schemas']['QuantityKind'];
export interface RecipeImportSeed {
  candidate?: components['schemas']['ImportedRecipe'];
  reviewedRecipe?: Write;
  ingredientNames?: Record<string, string>;
}
type IngredientForm = FormGroup<{
  id: FormControl<string>;
  groupId: FormControl<string>;
  ingredientId: FormControl<string>;
  ingredientName: FormControl<string>;
  quantityText: FormControl<string>;
  kind: FormControl<Kind>;
  unit: FormControl<Unit | ''>;
  preparation: FormControl<string>;
  uncertain: FormControl<boolean>;
}>;
type GroupForm = FormGroup<{ id: FormControl<string>; title: FormControl<string> }>;
type InstructionForm = FormGroup<{ id: FormControl<string>; text: FormControl<string> }>;

@Component({
  selector: 'app-recipe-editor',
  imports: [ReactiveFormsModule, RouterLink, OrganiserPicker],
  templateUrl: './recipe-editor.html',
  styles: `
    .ingredient-entry {
      border: 1px solid #d5ddd5;
      border-radius: 0.6rem;
      padding: 1rem;
      margin: 0.75rem 0;
    }
    .ingredient-fields {
      display: grid;
      grid-template-columns: minmax(0, 2fr) minmax(5rem, 1fr) minmax(5rem, 1fr);
      gap: 0.75rem;
      align-items: start;
    }
    .ingredient-search {
      position: relative;
    }
    .suggestions {
      border: 1px solid #7e9185;
      background: white;
      border-radius: 0.4rem;
      padding: 0.25rem;
    }
    .suggestions [role='option'] {
      padding: 0.65rem;
      min-height: 44px;
      cursor: pointer;
    }
    .suggestions [aria-selected='true'] {
      background: #e3eee4;
      outline: 2px solid #154a35;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .photo {
      max-width: 100%;
      max-height: 20rem;
      object-fit: contain;
      border-radius: 0.75rem;
    }
    .inline-fields {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: 1rem;
    }
    @media (max-width: 600px) {
      .ingredient-fields {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      }
      .ingredient-search {
        grid-column: 1 / -1;
      }
    }
  `,
})
export class RecipeEditor {
  private readonly organiserPickers = viewChildren(OrganiserPicker);
  readonly creatingOrganiser = computed(() =>
    this.organiserPickers().some((picker) => picker.creating()),
  );
  readonly showOrganisers = signal(false);
  setOrganisers(kind: 'collectionIds' | 'tagIds', ids: string[]): void {
    this.form.controls[kind].setValue(ids);
    this.form.markAsDirty();
  }

  readonly importSeed = input<RecipeImportSeed | null>(null);
  readonly saveImported = input<((recipe: Write) => Promise<string>) | null>(null);
  readonly embedded = input(false);
  readonly submissionUncertain = input(false);
  readonly originalLines = signal<Record<string, string>>({});
  private readonly api = inject(ApiClient);
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});
  readonly recipeId = signal<string | null>(null);
  readonly activeRow = signal<string | null>(null);
  readonly suggestions = signal<Ingredient[]>([]);
  readonly highlighted = signal(0);
  readonly units: { value: Unit; label: string }[] = [
    { value: 'g', label: 'g' },
    { value: 'kg', label: 'kg' },
    { value: 'ml', label: 'ml' },
    { value: 'l', label: 'L' },
    { value: 'tsp', label: 'teaspoon' },
    { value: 'tbsp', label: 'tablespoon' },
    { value: 'cup', label: 'cup' },
    { value: 'item', label: 'whole item' },
    { value: 'pinch', label: 'Pinch' },
    { value: 'dash', label: 'Dash' },
    { value: 'handful', label: 'Handful' },
    { value: 'bunch', label: 'Bunch' },
    { value: 'clove', label: 'Clove' },
    { value: 'slice', label: 'Slice' },
  ];
  readonly form = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(20000)],
    defaultServings: [
      '2',
      [Validators.required, Validators.pattern(/^(?:\d+(?:\.\d{0,6})?|\.\d{1,6})$/)],
    ],
    prepMinutes: this.fb.control<number | null>(null, [
      Validators.min(0),
      Validators.pattern(/^\d+$/),
    ]),
    cookMinutes: this.fb.control<number | null>(null, [
      Validators.min(0),
      Validators.pattern(/^\d+$/),
    ]),
    notes: ['', Validators.maxLength(20000)],
    sourceUrl: ['', Validators.maxLength(2048)],
    measurementStandard: this.fb.nonNullable.control<
      components['schemas']['MeasurementStandard'] | ''
    >('AU', Validators.required),
    imageAssetId: [''],
    acknowledgeUncertainIngredients: [false],
    collectionIds: this.fb.nonNullable.control<string[]>([]),
    tagIds: this.fb.nonNullable.control<string[]>([]),
    ingredients: this.fb.array<IngredientForm>([]),
    groups: this.fb.array<GroupForm>([]),
    instructions: this.fb.array<InstructionForm>([]),
  });
  readonly ingredients = this.form.controls.ingredients;
  readonly groups = this.form.controls.groups;
  readonly instructions = this.form.controls.instructions;
  private etag = '';
  private retryKey?: string;
  private duplicateCatalogKeys = new Map<string, string>();
  private photoRetry?: string;
  private readonly searches = new Subject<{ id: string; query: string }>();

  constructor() {
    effect(() => {
      const seed = this.importSeed();
      if (seed) untracked(() => this.applyImported(seed));
    });
    this.searches
      .pipe(
        debounceTime(150),
        switchMap((search) =>
          this.api
            .read<
              components['schemas']['IngredientPage']
            >(`/api/v1/ingredients?limit=8&q=${encodeURIComponent(search.query)}`)
            .pipe(
              map((response) => ({ search, items: response.body?.items ?? [], error: '' })),
              catchError((error) =>
                of({ search, items: [] as Ingredient[], error: apiMessage(error) }),
              ),
            ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        const row = this.ingredients.controls.find(
          (control) => control.controls.id.value === result.search.id,
        );
        if (
          row &&
          this.activeRow() === result.search.id &&
          row.controls.ingredientName.value === result.search.query &&
          !row.controls.ingredientId.value
        ) {
          this.suggestions.set(result.items);
          this.highlighted.set(0);
          if (result.error) this.error.set(result.error);
        }
      });
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.fieldErrors.set({}));
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.recipeId.set(id);
      void this.load(id);
    } else {
      this.addIngredient();
      this.addInstruction();
      this.form.markAsPristine();
    }
  }
  private ingredient(
    source?: Partial<Omit<Recipe['ingredients'][number], 'unit'>> & { unit?: Unit | '' },
  ): IngredientForm {
    return this.fb.nonNullable.group(
      {
        id: [source?.id ?? crypto.randomUUID()],
        groupId: [source?.groupId ?? ''],
        ingredientId: [source?.ingredientId ?? '', Validators.required],
        ingredientName: [
          source?.ingredientName ?? '',
          [Validators.required, Validators.maxLength(200)],
        ],
        quantityText: [source?.quantityText ?? '', Validators.maxLength(200)],
        kind: this.fb.nonNullable.control<Kind>(source?.kind ?? 'numeric'),
        unit: this.fb.nonNullable.control<Unit | ''>(source?.unit ?? 'g'),
        preparation: [source?.preparation ?? '', Validators.maxLength(20000)],
        uncertain: [source?.uncertain ?? false],
      },
      {
        validators: (row) =>
          row.get('kind')?.value === 'numeric' && !row.get('unit')?.value
            ? { unitRequired: true }
            : null,
      },
    );
  }
  private applyImported(seed: RecipeImportSeed): void {
    const candidate = seed.candidate;
    const write = seed.reviewedRecipe;
    this.form.patchValue({
      title: write?.title ?? candidate?.title ?? '',
      description: write?.description ?? candidate?.description ?? '',
      defaultServings: write?.defaultServings ?? candidate?.defaultServings ?? '',
      prepMinutes: write?.prepMinutes ?? candidate?.prepMinutes ?? null,
      cookMinutes: write?.cookMinutes ?? candidate?.cookMinutes ?? null,
      notes: write?.notes ?? candidate?.notes ?? '',
      sourceUrl: write?.sourceUrl ?? candidate?.sourceUrl ?? '',
      measurementStandard: write?.measurementStandard ?? candidate?.measurementStandard ?? '',
      imageAssetId: write?.imageAssetId ?? '',
      collectionIds: write?.collectionIds ?? [],
      tagIds: write?.tagIds ?? [],
      acknowledgeUncertainIngredients: write?.acknowledgeUncertainIngredients ?? false,
    });
    this.groups.clear();
    this.ingredients.clear();
    this.instructions.clear();
    const groups = new Map<string, string>();
    for (const group of write?.groups ?? [])
      this.groups.push(
        this.fb.nonNullable.group({
          id: this.fb.nonNullable.control<string>(group.id ?? crypto.randomUUID()),
          title: [group.title, [Validators.required, Validators.maxLength(200)]],
        }),
      );
    if (!write)
      for (const row of candidate?.ingredients ?? []) {
        if (row.group && !groups.has(row.group)) {
          const id = crypto.randomUUID();
          groups.set(row.group, id);
          this.groups.push(
            this.fb.nonNullable.group({
              id: this.fb.nonNullable.control<string>(id),
              title: [row.group, [Validators.required, Validators.maxLength(200)]],
            }),
          );
        }
      }
    const originals: Record<string, string> = {};
    if (write)
      for (const row of write.ingredients) {
        const name = seed.ingredientNames?.[row.ingredientId] ?? '';
        this.ingredients.push(
          this.ingredient({
            ...row,
            id: row.id ?? crypto.randomUUID(),
            ingredientId: name ? row.ingredientId : '',
            ingredientName: name,
          }),
        );
      }
    else
      for (const row of candidate?.ingredients ?? []) {
        const id = crypto.randomUUID();
        originals[id] = row.originalText;
        this.ingredients.push(
          this.ingredient({
            id,
            groupId: row.group ? groups.get(row.group) : null,
            ingredientId: row.matchedIngredientId ?? '',
            ingredientName: row.matchedIngredientName ?? row.ingredientName,
            quantityText: row.quantityText,
            kind: row.kind,
            unit: row.unit ?? '',
            preparation: row.preparation,
            uncertain: row.uncertain || row.unit === null,
          }),
        );
      }
    const steps =
      write?.instructions ??
      (candidate?.instructions ?? []).map((step) => ({
        text: step.group ? `${step.group}: ${step.text}` : step.text,
      }));
    for (const step of steps)
      this.instructions.push(
        this.fb.nonNullable.group({
          id: this.fb.nonNullable.control<string>(crypto.randomUUID()),
          text: [step.text, [Validators.required, Validators.maxLength(20000)]],
        }),
      );
    this.originalLines.set(originals);
    this.activeRow.set(null);
    this.suggestions.set([]);
    this.error.set('');
    this.fieldErrors.set({});
    this.form.markAsPristine();
  }
  private async load(id: string): Promise<void> {
    this.loading.set(true);
    try {
      const response = await firstValueFrom(this.api.read<Recipe>(`/api/v1/recipes/${id}`));
      const recipe = response.body;
      if (!recipe) throw new Error('Recipe could not be loaded.');
      this.etag = response.headers.get('ETag') ?? '';
      this.form.patchValue({
        title: recipe.title,
        description: recipe.description ?? '',
        defaultServings: recipe.defaultServings,
        prepMinutes: recipe.prepMinutes,
        cookMinutes: recipe.cookMinutes,
        notes: recipe.notes ?? '',
        sourceUrl: recipe.sourceUrl ?? '',
        measurementStandard: recipe.measurementStandard,
        imageAssetId: recipe.imageAssetId ?? '',
        collectionIds: recipe.collectionIds,
        tagIds: recipe.tagIds,
      });
      this.ingredients.clear();
      for (const row of recipe.ingredients) this.ingredients.push(this.ingredient(row));
      this.groups.clear();
      for (const group of recipe.groups)
        this.groups.push(
          this.fb.nonNullable.group({
            id: [group.id],
            title: [group.title, [Validators.required, Validators.maxLength(200)]],
          }),
        );
      this.instructions.clear();
      for (const step of recipe.instructions)
        this.instructions.push(
          this.fb.nonNullable.group({
            id: [step.id],
            text: [step.text, [Validators.required, Validators.maxLength(20000)]],
          }),
        );
      this.form.markAsPristine();
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.loading.set(false);
    }
  }
  addIngredient(focus = false): void {
    if (this.ingredients.length >= 1000) return;
    const row = this.ingredient();
    this.ingredients.push(row);
    this.form.markAsDirty();
    if (focus) this.focus('ingredient-name-' + row.controls.id.value);
  }
  removeIngredient(index: number): void {
    this.ingredients.removeAt(index);
    this.form.markAsDirty();
  }
  duplicateIngredient(index: number): void {
    if (this.ingredients.length >= 1000) return;
    const row = this.ingredient();
    row.patchValue({ ...this.ingredients.at(index).getRawValue(), id: crypto.randomUUID() });
    this.ingredients.insert(index + 1, row);
    this.form.markAsDirty();
    this.focus('ingredient-name-' + row.controls.id.value);
  }
  moveIngredient(index: number, direction: number): void {
    if (index + direction < 0 || index + direction >= this.ingredients.length) return;
    this.move(this.ingredients, index, direction);
    this.focus('ingredient-name-' + this.ingredients.at(index + direction).controls.id.value);
  }
  addGroup(): void {
    if (this.groups.length < 1000) {
      this.groups.push(
        this.fb.nonNullable.group({
          id: this.fb.nonNullable.control<string>(crypto.randomUUID()),
          title: ['', [Validators.required, Validators.maxLength(200)]],
        }),
      );
      this.form.markAsDirty();
    }
  }
  removeGroup(index: number): void {
    const id = this.groups.at(index).controls.id.value;
    for (const row of this.ingredients.controls)
      if (row.controls.groupId.value === id) row.controls.groupId.setValue('');
    this.groups.removeAt(index);
    this.form.markAsDirty();
  }
  moveGroup(index: number, direction: number): void {
    this.move(this.groups, index, direction);
  }
  addInstruction(): void {
    if (this.instructions.length < 1000) {
      this.instructions.push(
        this.fb.nonNullable.group({
          id: this.fb.nonNullable.control<string>(crypto.randomUUID()),
          text: ['', [Validators.required, Validators.maxLength(20000)]],
        }),
      );
      this.form.markAsDirty();
    }
  }
  removeInstruction(index: number): void {
    this.instructions.removeAt(index);
    this.form.markAsDirty();
  }
  duplicateInstruction(index: number): void {
    if (this.instructions.length < 1000) {
      this.instructions.insert(
        index + 1,
        this.fb.nonNullable.group({
          id: this.fb.nonNullable.control<string>(crypto.randomUUID()),
          text: [
            this.instructions.at(index).controls.text.value,
            [Validators.required, Validators.maxLength(20000)],
          ],
        }),
      );
      this.form.markAsDirty();
    }
  }
  moveInstruction(index: number, direction: number): void {
    this.move(this.instructions, index, direction);
  }
  private move<T extends FormGroup>(array: FormArray<T>, index: number, direction: number): void {
    const target = index + direction;
    if (target < 0 || target >= array.length) return;
    const row = array.at(index) as T;
    array.removeAt(index);
    array.insert(target, row);
    this.form.markAsDirty();
  }
  ingredientNameChanged(index: number): void {
    const row = this.ingredients.at(index);
    row.controls.ingredientId.setValue('');
    this.activeRow.set(row.controls.id.value);
    this.suggestions.set([]);
    this.form.markAsDirty();
    this.searches.next({ id: row.controls.id.value, query: row.controls.ingredientName.value });
  }
  suggest(index: number): void {
    const row = this.ingredients.at(index);
    if (!row.controls.ingredientId.value) {
      this.activeRow.set(row.controls.id.value);
      this.searches.next({ id: row.controls.id.value, query: row.controls.ingredientName.value });
    }
  }
  selectIngredient(index: number, ingredient: Ingredient): void {
    this.ingredients
      .at(index)
      .patchValue({ ingredientId: ingredient.id, ingredientName: ingredient.canonicalName });
    this.activeRow.set(null);
    this.suggestions.set([]);
    this.form.markAsDirty();
    this.focus('ingredient-quantity-' + this.ingredients.at(index).controls.id.value);
  }
  async createIngredient(index: number): Promise<void> {
    if (this.busy()) return;
    const row = this.ingredients.at(index);
    const name = row.controls.ingredientName.value;
    if (!name.trim()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const id = row.controls.id.value;
      const key = this.duplicateCatalogKeys.get(id) ?? crypto.randomUUID();
      this.duplicateCatalogKeys.set(id, key);
      const response = await firstValueFrom(
        this.api.write<Ingredient>(
          'POST',
          '/api/v1/ingredients',
          { canonicalName: name },
          undefined,
          key,
        ),
      );
      if (response.body) {
        this.selectIngredient(index, response.body);
        this.duplicateCatalogKeys.delete(id);
      }
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
  ingredientKeydown(event: KeyboardEvent, index: number): void {
    if (event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault();
      this.moveIngredient(index, event.key === 'ArrowUp' ? -1 : 1);
      return;
    }
    if (event.key === 'Escape') {
      this.activeRow.set(null);
      this.suggestions.set([]);
      return;
    }
    if (
      this.activeRow() !== this.ingredients.at(index).controls.id.value ||
      this.suggestions().length === 0
    )
      return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.highlighted.set(
        (this.highlighted() + (event.key === 'ArrowDown' ? 1 : -1) + this.suggestions().length) %
          this.suggestions().length,
      );
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      this.selectIngredient(index, this.suggestions()[this.highlighted()]);
    }
  }
  setUnknown(index: number, event: Event): void {
    this.ingredients
      .at(index)
      .controls.kind.setValue((event.target as HTMLInputElement).checked ? 'unknown' : 'numeric');
    this.ingredients.at(index).controls.quantityText.updateValueAndValidity();
    this.form.markAsDirty();
  }
  requiresReview(): boolean {
    return this.ingredients.controls.some(
      (row) => row.controls.kind.value === 'unknown' || row.controls.uncertain.value,
    );
  }
  async uploadPhoto(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file || this.busy()) return;
    if (
      !['image/jpeg', 'image/png', 'image/webp'].includes(file.type) ||
      file.size > 10 * 1024 * 1024
    ) {
      this.error.set('Choose a JPEG, PNG or WebP image up to 10 MiB.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.form.markAsDirty();
    try {
      const body = new FormData();
      body.append('file', file);
      this.photoRetry ??= crypto.randomUUID();
      const response = await firstValueFrom(
        this.api.write<components['schemas']['RecipeImageRead']>(
          'POST',
          '/api/v1/images',
          body,
          undefined,
          this.photoRetry,
        ),
      );
      if (response.body) {
        this.form.controls.imageAssetId.setValue(response.body.id);
        this.form.markAsDirty();
        this.photoRetry = undefined;
      }
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
  removePhoto(): void {
    this.form.controls.imageAssetId.setValue('');
    this.form.markAsDirty();
  }
  async save(): Promise<void> {
    if (this.busy() || this.loading() || this.creatingOrganiser()) return;
    this.error.set('');
    this.form.markAllAsTouched();
    for (const row of this.ingredients.controls)
      if (row.controls.kind.value === 'numeric' && !row.controls.quantityText.value.trim())
        row.controls.quantityText.setErrors({ required: true });
    if (this.form.invalid) {
      this.error.set('Check the required fields and choose or create each ingredient.');
      return;
    }
    if (this.requiresReview() && !this.form.controls.acknowledgeUncertainIngredients.value) {
      this.error.set(
        'Review and acknowledge unresolved or uncertain ingredient quantities before saving.',
      );
      return;
    }
    const value = this.form.getRawValue();
    if (!value.measurementStandard) {
      this.error.set('Choose the recipe’s source cup and spoon standard.');
      return;
    }
    const body: Write = {
      title: value.title,
      description: value.description || null,
      defaultServings: value.defaultServings,
      prepMinutes: value.prepMinutes,
      cookMinutes: value.cookMinutes,
      notes: value.notes || null,
      sourceUrl: value.sourceUrl || null,
      imageAssetId: value.imageAssetId || null,
      measurementStandard: value.measurementStandard,
      groups: value.groups.map((group, position) => ({
        id: group.id,
        title: group.title,
        position,
      })),
      ingredients: value.ingredients.map((row, position) => ({
        id: row.id,
        groupId: row.groupId || null,
        position,
        ingredientId: row.ingredientId,
        quantityText: row.quantityText,
        kind: row.kind,
        // Unknown quantities do not participate in unit arithmetic; item is the neutral wire value.
        unit: row.unit || 'item',
        preparation: row.preparation || null,
        uncertain: row.uncertain,
      })),
      instructions: value.instructions.map((step, position) => ({
        id: step.id,
        position,
        text: step.text,
      })),
      collectionIds: value.collectionIds,
      tagIds: value.tagIds,
      acknowledgeUncertainIngredients: value.acknowledgeUncertainIngredients,
    };
    this.busy.set(true);
    try {
      const commitImported = this.saveImported();
      if (commitImported) {
        const created = await commitImported(body);
        this.form.markAsPristine();
        await this.router.navigate(['/recipes', created]);
        return;
      }
      const id = this.recipeId();
      this.retryKey ??= crypto.randomUUID();
      const response = await firstValueFrom(
        this.api.write<Recipe>(
          id ? 'PUT' : 'POST',
          id ? `/api/v1/recipes/${id}` : '/api/v1/recipes',
          body,
          id ? this.etag : undefined,
          id ? undefined : this.retryKey,
        ),
      );
      if (response.body) {
        this.form.markAsPristine();
        this.retryKey = undefined;
        await this.router.navigate(['/recipes', response.body.id]);
      }
    } catch (error) {
      this.error.set(apiMessage(error));
      if (error instanceof HttpErrorResponse)
        this.fieldErrors.set(
          Object.fromEntries(
            (
              (error.error as { errors?: { path: string; message: string }[] } | null)?.errors ?? []
            ).map((field) => [field.path, field.message]),
          ),
        );
    } finally {
      this.busy.set(false);
    }
  }
  hasUnsavedChanges(): boolean {
    return this.form.dirty || this.creatingOrganiser();
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
  private focus(id: string): void {
    const previous = document.activeElement;
    afterNextRender(
      () => {
        // Do not steal focus if the user has already moved to another field.
        if (document.activeElement === previous || document.activeElement === document.body)
          this.element.nativeElement.querySelector<HTMLElement>('#' + id)?.focus();
      },
      { injector: this.injector },
    );
  }
}
