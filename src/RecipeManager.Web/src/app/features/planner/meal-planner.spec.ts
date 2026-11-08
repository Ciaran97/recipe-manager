import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MealPlanner } from './meal-planner';

const slot = {
  id: 'slot-dinner',
  name: 'Dinner',
  position: 2,
  isDefault: true,
  archived: false,
  revision: '1',
};
const meal = {
  id: 'cook-meal',
  revision: '1',
  date: '2026-11-02',
  slotId: slot.id,
  type: 'cook',
  label: null,
  cookingEvent: {
    id: 'source',
    revision: '1',
    sourceRecipeId: 'rice',
    recipeRevision: '1',
    plannedPrepared: '4',
    plannedEaten: '2',
    actualPrepared: null,
    actualEaten: null,
    confirmedAt: null,
    availabilityBasis: 'planned',
    availablePortions: '2',
    recipeSnapshot: {
      schemaVersion: 1,
      recipeId: 'rice',
      recipeRevision: '1',
      title: 'Rice',
      description: null,
      imageAssetId: null,
      defaultServings: '2',
      prepMinutes: null,
      cookMinutes: null,
      notes: null,
      sourceUrl: null,
      measurementStandard: 'AU',
      policyVersion: 1,
      groups: [],
      ingredients: [],
      instructions: [],
    },
  },
  leftovers: null,
};

describe('Meal planner client state', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MealPlanner],
      providers: [
        provideRouter([]),
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function calendar(items: unknown[] = []) {
    const request = http.expectOne((value) => value.url.startsWith('/api/v1/meals?'));
    const query = new URL(request.request.url, 'https://localhost').searchParams;
    request.flush({
      from: query.get('from'),
      to: query.get('to'),
      items,
      slots: [slot],
      revision: '1',
    });
  }
  async function ready() {
    const fixture = TestBed.createComponent(MealPlanner);
    http.expectOne('/api/v1/household/settings').flush({ timezone: 'Australia/Perth' });
    await Promise.resolve();
    calendar();
    await fixture.whenStable();
    return fixture;
  }
  it('sends separate planned portion strings and accepts only server-scheduled meals', async () => {
    const fixture = await ready();
    const page = fixture.componentInstance;
    const opening = page.openNew('cook', '2026-11-02', slot.id);
    http
      .expectOne('/api/v1/recipes?limit=100&archived=exclude')
      .flush({ items: [], nextCursor: null });
    await opening;
    page.form.patchValue({ recipeId: 'rice', plannedPrepared: '4', plannedEaten: '2' });
    const saving = page.saveMeal();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/meals/cooking');
    expect(request.request.body.plannedPrepared).toBe('4');
    expect(request.request.body.plannedEaten).toBe('2');
    expect(request.request.body.actualPrepared).toBeUndefined();
    request.flush(meal, { status: 201, statusText: 'Created', headers: { ETag: '"meal:1:1"' } });
    await Promise.resolve();
    calendar([meal]);
    await saving;
    expect(page.calendar()?.items).toHaveLength(1);
    expect(page.mealsFor('2026-11-02', slot.id)[0].type).toBe('cook');
  });
  it('keeps entered portions when another household member changed the cooking source', async () => {
    const fixture = await ready();
    const page = fixture.componentInstance;
    const opening = page.openEdit('cook-meal');
    http.expectOne('/api/v1/meals/cook-meal').flush(meal, { headers: { ETag: '"meal:1:1"' } });
    await Promise.resolve();
    http
      .expectOne('/api/v1/recipes?limit=100&archived=exclude')
      .flush({ items: [], nextCursor: null });
    await opening;
    page.form.patchValue({ plannedPrepared: '6', plannedEaten: '2' });
    const saving = page.saveMeal();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const update = http.expectOne('/api/v1/meals/cook-meal/cooking');
    expect(update.request.headers.get('If-Match')).toBe('"meal:1:1"');
    update.flush(
      { code: 'stale_revision', errors: [], conflicts: [] },
      { status: 412, statusText: 'Precondition Failed' },
    );
    await saving;
    expect(page.form.controls.plannedPrepared.value).toBe('6');
    expect(page.form.controls.plannedEaten.value).toBe('2');
    expect(page.error()).toBeTruthy();
  });
});
