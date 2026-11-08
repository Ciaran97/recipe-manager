import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CataloguePage } from './catalogue-page';

const ingredient = {
  id: '10000000-0000-0000-0000-000000000001',
  canonicalName: 'Red capsicum',
  categoryId: null,
  categoryName: null,
  revision: '1',
  aliases: [{ id: '20000000-0000-0000-0000-000000000001', name: 'Red pepper' }],
};

describe('Household ingredient catalogue', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CataloguePage],
      providers: [
        provideRouter([]),
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  async function load() {
    const fixture = TestBed.createComponent(CataloguePage);
    http.expectOne('/api/v1/ingredients?limit=25').flush({ items: [ingredient], nextCursor: null });
    http
      .expectOne('/api/v1/ingredient-categories?limit=100')
      .flush({ items: [], nextCursor: null });
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }
  it('displays canonical ingredients and aliases with labelled search and editing controls', async () => {
    const fixture = await load();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Red capsicum');
    expect(element.textContent).toContain('Red pepper');
    expect(element.querySelector('input[type="search"]')?.getAttribute('id')).toBeTruthy();
  });
  it('keeps a conflicting correction and sends the exact parent ETag', async () => {
    const fixture = await load();
    const choose = fixture.componentInstance.choose(ingredient.id);
    http
      .expectOne(`/api/v1/ingredients/${ingredient.id}`)
      .flush(ingredient, { headers: { ETag: '"opaque-parent"' } });
    await choose;
    fixture.componentInstance.ingredientForm.controls.canonicalName.setValue('Beef mince');
    const save = fixture.componentInstance.saveIngredient();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const update = http.expectOne(`/api/v1/ingredients/${ingredient.id}`);
    expect(update.request.headers.get('If-Match')).toBe('"opaque-parent"');
    update.flush({ code: 'name_conflict' }, { status: 409, statusText: 'Conflict' });
    await save;
    expect(fixture.componentInstance.ingredientForm.controls.canonicalName.value).toBe(
      'Beef mince',
    );
    expect(fixture.componentInstance.error()).toContain('name');
  });
  it('uses the parent validator when correcting an alias and retains a conflict for review', async () => {
    const fixture = await load();
    const choose = fixture.componentInstance.choose(ingredient.id);
    http
      .expectOne(`/api/v1/ingredients/${ingredient.id}`)
      .flush(ingredient, { headers: { ETag: '"parent"' } });
    await choose;
    fixture.componentInstance.editAlias(ingredient.aliases[0]);
    fixture.componentInstance.aliasForm.controls.name.setValue('Beef mince');
    const save = fixture.componentInstance.saveAlias();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const alias = http.expectOne(
      `/api/v1/ingredients/${ingredient.id}/aliases/${ingredient.aliases[0].id}`,
    );
    expect(alias.request.method).toBe('PUT');
    expect(alias.request.headers.get('If-Match')).toBe('"parent"');
    alias.flush({ code: 'name_conflict' }, { status: 409, statusText: 'Conflict' });
    await save;
    expect(fixture.componentInstance.aliasForm.controls.name.value).toBe('Beef mince');
    expect(fixture.componentInstance.selected()?.aliases[0].name).toBe('Red pepper');
  });

  it('explains referenced deletion while retaining the selected ingredient', async () => {
    const fixture = await load();
    const choose = fixture.componentInstance.choose(ingredient.id);
    http
      .expectOne(`/api/v1/ingredients/${ingredient.id}`)
      .flush(ingredient, { headers: { ETag: '"parent"' } });
    await choose;
    const remove = fixture.componentInstance.deleteIngredient();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    http
      .expectOne(`/api/v1/ingredients/${ingredient.id}`)
      .flush({ code: 'ingredient_referenced' }, { status: 409, statusText: 'Conflict' });
    await remove;
    expect(fixture.componentInstance.selected()?.id).toBe(ingredient.id);
    expect(fixture.componentInstance.error()).toContain('used');
  });
});
