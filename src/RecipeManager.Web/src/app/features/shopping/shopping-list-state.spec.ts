import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ShoppingListState } from './shopping-list-state';

const list = {
  id: 'list',
  revision: '1',
  name: 'Shopping',
  selection: null,
  basis: 'planned',
  policyVersion: 1,
  householdSourceRevision: '4',
  sourceRevisions: [],
  items: [
    {
      id: 'known',
      key: 'known:salt',
      ingredientId: 'salt',
      label: 'Salt',
      categoryId: null,
      categoryName: 'Pantry',
      dimension: 'Mass',
      baselineExact: { numerator: '5', denominator: '1' },
      effectiveExact: { numerator: '5', denominator: '1' },
      quantityOverride: null,
      hasOverride: false,
      displayText: '5 g',
      isApproximate: false,
      unresolved: false,
      additionalAmountUnresolved: true,
      purchased: true,
      manual: false,
      removed: false,
      position: 0,
      contributions: [],
    },
    {
      id: 'unknown',
      key: 'unknown:salt',
      ingredientId: 'salt',
      label: 'Salt',
      categoryId: null,
      categoryName: 'Pantry',
      dimension: null,
      baselineExact: null,
      effectiveExact: null,
      quantityOverride: null,
      hasOverride: false,
      displayText: 'to taste',
      isApproximate: false,
      unresolved: true,
      additionalAmountUnresolved: false,
      purchased: false,
      manual: false,
      removed: false,
      position: 1,
      contributions: [],
    },
  ],
  summary: {
    knownItemCount: 1,
    unresolvedCount: 1,
    purchasedCount: 1,
    removedCount: 0,
    knownTotals: [],
  },
};

describe('Shopping list state', () => {
  let http: HttpTestingController;
  let state: ShoppingListState;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ShoppingListState,
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    state = TestBed.inject(ShoppingListState);
  });
  afterEach(() => http.verify());
  async function open() {
    const pending = state.open('list');
    http
      .expectOne('/api/v1/shopping-lists/list')
      .flush(list, { headers: { ETag: '"opaque-list"' } });
    await pending;
  }
  it('preserves the accepted action notice during background recovery', async () => {
    await open();
    state.notice.set('Reviewed requirements applied.');
    const pending = state.recoverRemote();
    http.expectOne('/api/v1/shopping-lists/list').flush(list, { headers: { ETag: '"current"' } });
    await new Promise((resolve) => setTimeout(resolve, 0));
    http.expectOne('/api/v1/shopping-lists?limit=25').flush({ items: [], nextCursor: null });
    await pending;
    expect(state.notice()).toBe('Reviewed requirements applied.');
  });
  it('keeps unresolved rows independently visible beside purchased known quantities', async () => {
    await open();
    expect(state.groups()[0].items).toHaveLength(2);
    expect(state.groups()[0].items[1].unresolved).toBe(true);
    expect(state.current()?.summary.unresolvedCount).toBe(1);
  });
  it('retains a manual draft and validator on stale writes', async () => {
    await open();
    state.beginManual();
    state.manualForm.controls.label.setValue('Bread');
    state.manualForm.markAsDirty();
    const pending = state.saveManual();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    await Promise.resolve();
    const request = http.expectOne('/api/v1/shopping-lists/list/items');
    expect(request.request.headers.get('If-Match')).toBe('"opaque-list"');
    request.flush({ code: 'stale_revision' }, { status: 412, statusText: 'Precondition Failed' });
    await pending;
    expect(state.manualForm.controls.label.value).toBe('Bread');
    expect(state.hasUnsavedChanges()).toBe(true);
    expect(state.outdated()).toBe(true);
  });
  it('does not overwrite a dirty form when a household member changes the list', async () => {
    await open();
    state.beginManual();
    state.manualForm.controls.label.setValue('Bread');
    state.manualForm.markAsDirty();
    await state.remoteChange('list', '9007199254740993', false);
    http.expectNone('/api/v1/shopping-lists/list');
    expect(state.manualForm.controls.label.value).toBe('Bread');
    expect(state.outdated()).toBe(true);
  });
  it('preserves entered recipe serving decimals in generation requests', async () => {
    state.beginCreate();
    state.creationForm.controls.name.setValue('Recipes');
    state.sourceKind.set('recipes');
    state.recipeEntries.set([{ recipeId: 'recipe', title: 'Rice', servings: '2.125' }]);
    const pending = state.create();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    await Promise.resolve();
    const request = http.expectOne('/api/v1/shopping-lists/generate');
    expect(request.request.body.selection.entries[0].servings).toBe('2.125');
    expect(request.request.body.basis).toBe('planned');
    request.flush(
      { ...list, name: 'Recipes' },
      { status: 201, statusText: 'Created', headers: { ETag: '"created"' } },
    );
    await pending;
    expect(state.current()?.name).toBe('Recipes');
  });
  function reviewedPreview() {
    const selection = { kind: 'mealRange' as const, from: '2026-11-02', to: '2026-11-04' };
    state.current.set({ ...state.current()!, selection });
    state.beginReconcile();
    state.preview.set({
      listRevision: '1',
      householdSourceRevision: '4',
      sourceRevisions: [],
      policyVersion: 1,
      selection,
      basis: 'planned',
      changes: [],
      manualItems: [],
    });
    state.reviewRequired.set(true);
    state.reviewed.set(true);
  }
  it('retains explicit review after a delayed source hint and submits the exact preview fence', async () => {
    await open();
    reviewedPreview();
    state.markSourcesChanged();
    expect(state.reviewed()).toBe(true);
    const pending = state.applyChanges();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    await Promise.resolve();
    const request = http.expectOne('/api/v1/shopping-lists/list/reconciliation/apply');
    expect(request.request.body.householdSourceRevision).toBe('4');
    expect(request.request.body.listRevision).toBe('1');
    expect(request.request.headers.get('If-Match')).toBe('"opaque-list"');
    request.flush(list, { headers: { ETag: '"applied"' } });
    await pending;
    expect(state.notice()).toBe('Reviewed requirements applied.');
    expect(state.sourcesChanged()).toBe(false);
  });
  it('requires fresh explicit review when the backend rejects a genuinely stale source fence', async () => {
    await open();
    reviewedPreview();
    state.markSourcesChanged();
    const pending = state.applyChanges();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    await Promise.resolve();
    http
      .expectOne('/api/v1/shopping-lists/list/reconciliation/apply')
      .flush({ code: 'shopping_sources_changed' }, { status: 409, statusText: 'Conflict' });
    await pending;
    expect(state.reviewed()).toBe(false);
    expect(state.reviewRequired()).toBe(true);
    expect(state.outdated()).toBe(true);
    expect(state.current()?.revision).toBe('1');
    expect(state.current()?.items[0].purchased).toBe(true);
  });
});
