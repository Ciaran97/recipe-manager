import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RecipeEditor } from './recipe-editor';

@Component({ template: '' })
class SavedRecipe {}

describe('Structured recipe editor', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RecipeEditor],
      providers: [
        provideRouter([{ path: 'recipes/:id', component: SavedRecipe }]),
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function valid() {
    const fixture = TestBed.createComponent(RecipeEditor);
    const editor = fixture.componentInstance;
    editor.form.controls.title.setValue('Rice bowls');
    editor.form.controls.defaultServings.setValue('3');
    editor.ingredients.at(0).patchValue({
      ingredientId: '10000000-0000-0000-0000-000000000001',
      ingredientName: 'Rice',
      quantityText: '1/3',
      kind: 'numeric',
      unit: 'g',
    });
    editor.instructions.at(0).controls.text.setValue('Cook rice.');
    return fixture;
  }
  it('submits original fraction strings and Australian defaults without client calculations', async () => {
    const fixture = valid();
    const save = fixture.componentInstance.save();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/recipes');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body.defaultServings).toBe('3');
    expect(request.request.body.measurementStandard).toBe('AU');
    expect(request.request.body.ingredients[0].quantityText).toBe('1/3');
    expect(request.request.body.ingredients[0].unit).toBe('g');
    request.flush({ id: 'recipe-created' }, { status: 201, statusText: 'Created' });
    await save;
  });
  it('requires review of unresolved quantities instead of supplying a numeric value', async () => {
    const fixture = valid();
    const editor = fixture.componentInstance;
    editor.ingredients.at(0).patchValue({ kind: 'unknown', quantityText: 'to taste' });
    await editor.save();
    http.expectNone('/api/v1/auth/csrf');
    expect(editor.error()).toContain('Review');
    expect(editor.ingredients.at(0).controls.quantityText.value).toBe('to taste');
  });
  it('saves an unspecified quantity without making the user select a unit', async () => {
    const fixture = valid();
    const editor = fixture.componentInstance;
    editor.ingredients.at(0).patchValue({ kind: 'unknown', quantityText: 'to taste', unit: '' });
    editor.form.controls.acknowledgeUncertainIngredients.setValue(true);
    const save = editor.save();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/recipes');
    expect(request.request.body.ingredients[0].kind).toBe('unknown');
    expect(request.request.body.ingredients[0].quantityText).toBe('to taste');
    expect(request.request.body.ingredients[0].unit).toBe('item');
    expect(editor.ingredients.at(0).controls.unit.value).toBe('');
    request.flush({ id: 'unknown-saved' }, { status: 201, statusText: 'Created' });
    await save;
  });
  it('requires a unit again when an unspecified quantity becomes numeric', async () => {
    const fixture = valid();
    const editor = fixture.componentInstance;
    editor.ingredients.at(0).patchValue({ kind: 'unknown', unit: '' });
    expect(editor.ingredients.at(0).valid).toBe(true);
    editor.ingredients.at(0).controls.kind.setValue('numeric');
    expect(editor.ingredients.at(0).hasError('unitRequired')).toBe(true);
    await editor.save();
    http.expectNone('/api/v1/auth/csrf');
  });
  it('duplicates and reorders ingredient rows with new identities while preserving source fields', () => {
    const fixture = valid();
    const editor = fixture.componentInstance;
    const original = editor.ingredients.at(0).getRawValue();
    editor.duplicateIngredient(0);
    const copy = editor.ingredients.at(1).getRawValue();
    expect(copy.id).not.toBe(original.id);
    expect(copy.quantityText).toBe('1/3');
    expect(copy.ingredientId).toBe(original.ingredientId);
    editor.moveIngredient(1, -1);
    expect(editor.ingredients.at(0).controls.id.value).toBe(copy.id);
    expect(editor.ingredients.at(1).controls.id.value).toBe(original.id);
  });
  it('clears a selected canonical identity when its visible name is changed', () => {
    const fixture = valid();
    const editor = fixture.componentInstance;
    editor.ingredients.at(0).controls.ingredientName.setValue('Beef mince');
    editor.ingredientNameChanged(0);
    expect(editor.ingredients.at(0).controls.ingredientId.value).toBe('');
  });
});

describe('Imported recipe editor', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RecipeEditor],
      providers: [
        provideRouter([{ path: 'recipes/:id', component: SavedRecipe }]),
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('keeps missing source measures, servings and units empty instead of taking manual defaults', async () => {
    const fixture = TestBed.createComponent(RecipeEditor);
    fixture.componentRef.setInput('importSeed', {
      candidate: {
        title: 'Soup',
        description: null,
        defaultServings: null,
        prepMinutes: null,
        cookMinutes: null,
        ingredients: [
          {
            originalText: 'salt to taste',
            ingredientName: 'salt',
            quantityText: 'to taste',
            kind: 'unknown',
            unit: null,
            originalUnit: null,
            preparation: null,
            group: null,
            uncertain: true,
            matchedIngredientId: null,
            matchedIngredientName: null,
          },
        ],
        instructions: [{ text: 'Stir.', group: null }],
        notes: null,
        sourceUrl: null,
        imageUrl: null,
        measurementStandard: null,
        suggestedTags: [],
        uncertaintyFields: ['measurementStandard'],
      },
    });
    fixture.detectChanges();
    await fixture.whenStable();
    const editor = fixture.componentInstance;
    expect(editor.form.controls.measurementStandard.value).toBe('');
    expect(editor.form.controls.defaultServings.value).toBe('');
    expect(editor.ingredients.at(0).controls.unit.value).toBe('');
    expect(editor.ingredients.at(0).controls.quantityText.value).toBe('to taste');
    await editor.save();
    http.expectNone('/api/v1/auth/csrf');
  });
  it('uses the import commit callback instead of ordinary recipe creation and preserves source fractions', async () => {
    const fixture = TestBed.createComponent(RecipeEditor);
    let submitted: Record<string, unknown> | undefined;
    fixture.componentRef.setInput('saveImported', async (body: Record<string, unknown>) => {
      submitted = body;
      return 'saved-import';
    });
    const editor = fixture.componentInstance;
    editor.form.controls.title.setValue('Rice');
    editor.form.controls.defaultServings.setValue('2');
    editor.ingredients.at(0).patchValue({
      ingredientId: 'ingredient',
      ingredientName: 'Rice',
      quantityText: '1/3',
      kind: 'numeric',
      unit: 'g',
    });
    editor.instructions.at(0).controls.text.setValue('Cook.');
    await editor.save();
    expect(submitted?.['defaultServings']).toBe('2');
    expect((submitted?.['ingredients'] as { quantityText: string }[])[0].quantityText).toBe('1/3');
    http.expectNone('/api/v1/recipes');
  });
});
