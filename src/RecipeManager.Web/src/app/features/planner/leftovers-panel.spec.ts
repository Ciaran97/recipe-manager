import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LeftoversPanel } from './leftovers-panel';
import { PortionInput } from './portion-input';

const slot = {
  id: 'dinner',
  name: 'Dinner',
  position: 2,
  isDefault: true,
  archived: false,
  revision: '1',
};
const allocation = {
  id: 'allocation',
  sourceCookingEventId: 'source',
  sourceRevision: '4',
  recipeTitle: 'Rice',
  sourceDate: '2026-11-02',
  plannedPortions: '2',
  consumedPortions: '1.5',
  reservedRemainder: '0.5',
  commitment: '2',
  confirmedAt: '2026-11-03T10:00:00Z',
};
const meal = {
  id: 'leftover-meal',
  revision: '2',
  date: '2026-11-03',
  slotId: slot.id,
  type: 'leftovers',
  label: null,
  cookingEvent: null,
  leftovers: allocation,
};
const available = {
  sourceCookingEventId: 'source',
  sourceRevision: '4',
  sourceMealId: 'cook-meal',
  date: '2026-11-02',
  slotId: slot.id,
  recipeTitle: 'Rice',
  sourceRecipeId: 'recipe',
  recipeRevision: '1',
  plannedPrepared: '4',
  plannedEaten: '2',
  actualPrepared: '4',
  actualEaten: '2',
  confirmedAt: '2026-11-02T10:00:00Z',
  basis: 'actual',
  capacity: '2',
  availablePortions: '0',
  allocations: [
    { mealId: meal.id, mealRevision: meal.revision, date: meal.date, slotId: slot.id, allocation },
  ],
};

describe('Explicit leftover workflow state', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LeftoversPanel, PortionInput],
      providers: [provideHttpClient(withNoXsrfProtection()), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function create(
    mode: 'allocation' | 'source',
    existing = false,
  ): ComponentFixture<LeftoversPanel> {
    const fixture = TestBed.createComponent(LeftoversPanel);
    fixture.componentRef.setInput('mode', mode);
    fixture.componentRef.setInput('date', '2026-11-03');
    fixture.componentRef.setInput('slotId', slot.id);
    fixture.componentRef.setInput('slots', [slot]);
    if (mode === 'source') fixture.componentRef.setInput('sourceId', 'source');
    if (existing) fixture.componentRef.setInput('mealId', meal.id);
    fixture.detectChanges();
    return fixture;
  }
  function choices() {
    http
      .expectOne((request) => request.url.startsWith('/api/v1/meals?'))
      .flush({ from: '2026-11-02', to: '2026-11-08', items: [], slots: [slot], revision: '1' });
  }
  async function existing() {
    const fixture = create('allocation', true);
    http
      .expectOne('/api/v1/meals/leftover-meal')
      .flush(meal, { headers: { ETag: '"opaque-meal"' } });
    await Promise.resolve();
    http
      .expectOne('/api/v1/cooking-events/source/leftovers')
      .flush(available, { headers: { ETag: '"opaque-source"' } });
    await Promise.resolve();
    await Promise.resolve();
    choices();
    await fixture.whenStable();
    return fixture;
  }
  it('creates an explicit fractional reservation without recording source production or consumption', async () => {
    const fixture = create('allocation');
    choices();
    await vi.waitFor(() => expect(fixture.componentInstance.busy()).toBe(false));
    await fixture.whenStable();
    const panel = fixture.componentInstance;
    panel.allocationForm.controls.sourceId.setValue('source');
    const loading = panel.selectSource('source');
    http.expectOne('/api/v1/cooking-events/source/leftovers').flush(
      {
        ...available,
        basis: 'planned',
        actualPrepared: null,
        actualEaten: null,
        confirmedAt: null,
        availablePortions: '2',
        allocations: [],
      },
      { headers: { ETag: '"source"' } },
    );
    await loading;
    panel.allocationForm.controls.plannedPortions.setValue('1.5');
    const save = panel.saveAllocation();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/meals/leftovers');
    expect(request.request.body.plannedPortions).toBe('1.5');
    expect(request.request.body.sourceRevision).toBe('4');
    expect(request.request.body.consumedPortions).toBeUndefined();
    expect(request.request.body.actualPrepared).toBeUndefined();
    request.flush(meal, { status: 201, statusText: 'Created' });
    await save;
  });
  it('releases only a reservation while keeping planned and consumed values separate', async () => {
    const fixture = await existing();
    const panel = fixture.componentInstance;
    panel.releaseReservation();
    const save = panel.saveAllocation();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/meals/leftover-meal/leftovers');
    expect(request.request.headers.get('If-Match')).toBe('"opaque-meal"');
    expect(request.request.body.plannedPortions).toBe('2');
    expect(request.request.body.reservedRemainder).toBe('0');
    expect(request.request.body.consumedPortions).toBeUndefined();
    expect(panel.meal()?.leftovers?.consumedPortions).toBe('1.5');
    request.flush(meal);
    await save;
  });
  it('does not consume against a newly selected but unsaved source even when revisions happen to match', async () => {
    const fixture = await existing();
    const panel = fixture.componentInstance;
    panel.allocationForm.controls.sourceId.setValue('different-source');
    await panel.confirmConsumption();
    http.expectNone('/api/v1/auth/csrf');
    expect(panel.error()).toContain('Save or cancel planned');
  });
  it('requires explicit dependent choices after an actual correction conflicts', async () => {
    const fixture = create('source');
    http
      .expectOne('/api/v1/cooking-events/source/leftovers')
      .flush(available, { headers: { ETag: '"opaque-source"' } });
    await vi.waitFor(() => expect(fixture.componentInstance.busy()).toBe(false));
    await fixture.whenStable();
    const panel = fixture.componentInstance;
    panel.actualForm.patchValue({ prepared: '3', eaten: '1.5' });
    const save = panel.saveActuals();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    http.expectOne('/api/v1/cooking-events/source/actuals').flush(
      {
        code: 'leftovers_overallocated',
        conflicts: [{ resourceId: meal.id, reason: 'Resolve the reservation.' }],
      },
      { status: 409, statusText: 'Conflict' },
    );
    await save;
    expect(panel.actualForm.controls.prepared.value).toBe('3');
    expect(panel.operation()?.kind).toBe('actuals');
    http.expectNone('/api/v1/cooking-events/source/resolve');
    panel.rows.at(0).patchValue({ action: 'release', reserved: '0' });
    const applying = panel.applyResolution();
    const request = http.expectOne('/api/v1/cooking-events/source/resolve');
    expect(request.request.headers.get('If-Match')).toBe('"opaque-source"');
    expect(request.request.body.expectedSources).toEqual([
      { sourceCookingEventId: 'source', revision: '4' },
    ]);
    expect(request.request.body.actualPrepared).toBe('3');
    expect(request.request.body.actualEaten).toBe('1.5');
    expect(request.request.body.dependents[0].action).toBe('release');
    expect(request.request.body.dependents[0].reservedRemainder).toBe('0');
    expect(request.request.body.dependents[0].consumedPortions).toBeUndefined();
    request.flush({});
    await applying;
  });
  it('keeps direct decimal text in native number-input presentation controls', () => {
    const fixture = TestBed.createComponent(PortionInput);
    fixture.componentRef.setInput('id', 'portion');
    fixture.componentRef.setInput('label', 'Portions');
    fixture.componentRef.setInput('value', '1');
    fixture.detectChanges();
    const values: string[] = [];
    fixture.componentInstance.valueChange.subscribe((value) => values.push(value));
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = '0.1';
    input.dispatchEvent(new Event('input'));
    expect(values).toEqual(['0.1']);
    expect(input.step).toBe('0.5');
  });
});
