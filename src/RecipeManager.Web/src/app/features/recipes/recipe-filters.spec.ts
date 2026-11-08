import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { OrganiserPicker } from './recipe-filters';

describe('Recipe organiser selection', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OrganiserPicker],
      providers: [provideHttpClient(withNoXsrfProtection()), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('retains an unavailable selection until the user explicitly removes it', async () => {
    const fixture = TestBed.createComponent(OrganiserPicker);
    fixture.componentRef.setInput('kind', 'collections');
    fixture.componentRef.setInput('selected', ['removed']);
    fixture.detectChanges();
    http.expectOne('/api/v1/collections?limit=100').flush({ items: [], nextCursor: null });
    await fixture.whenStable();
    expect(fixture.componentInstance.selected()).toEqual(['removed']);
    expect(fixture.componentInstance.missing()).toEqual(['removed']);
    fixture.componentInstance.toggle('removed');
    expect(fixture.componentInstance.selected()).toEqual([]);
  });
  it('loads later pages and allows multiple shared selections without losing existing ones', async () => {
    const fixture = TestBed.createComponent(OrganiserPicker);
    fixture.componentRef.setInput('kind', 'tags');
    fixture.componentRef.setInput('selected', ['first']);
    fixture.detectChanges();
    http
      .expectOne('/api/v1/tags?limit=100')
      .flush({ items: [{ id: 'first', name: 'First', revision: '1' }], nextCursor: 'second-page' });
    await vi.waitFor(() => {
      http
        .expectOne('/api/v1/tags?limit=100&cursor=second-page')
        .flush({ items: [{ id: 'second', name: 'Second', revision: '1' }], nextCursor: null });
    });
    await fixture.whenStable();
    fixture.componentInstance.toggle('second');
    expect(fixture.componentInstance.selected()).toEqual(['first', 'second']);
    expect(fixture.componentInstance.options().length).toBe(2);
  });
});
