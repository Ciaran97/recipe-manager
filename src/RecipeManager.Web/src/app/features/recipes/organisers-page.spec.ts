import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { OrganisersPage } from './organisers-page';

describe('Shared recipe organisers', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OrganisersPage],
      providers: [
        provideRouter([]),
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('retains an edited name after a household name conflict', async () => {
    const fixture = TestBed.createComponent(OrganisersPage);
    http.expectOne('/api/v1/collections?limit=100').flush({ items: [], nextCursor: null });
    await fixture.whenStable();
    const page = fixture.componentInstance;
    page.form.controls.name.setValue('Weeknight');
    const pending = page.save();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    http
      .expectOne('/api/v1/collections')
      .flush({ code: 'name_conflict' }, { status: 409, statusText: 'Conflict' });
    await pending;
    expect(page.form.controls.name.value).toBe('Weeknight');
    expect(page.error()).toContain('name');
  });
  it('reads a current validator before editing and sends it with the rename', async () => {
    const fixture = TestBed.createComponent(OrganisersPage);
    http
      .expectOne('/api/v1/collections?limit=100')
      .flush({ items: [{ id: 'id', name: 'Old', revision: '1' }], nextCursor: null });
    await fixture.whenStable();
    const page = fixture.componentInstance;
    const editing = page.edit('id');
    http
      .expectOne('/api/v1/collections/id')
      .flush({ id: 'id', name: 'Old', revision: '2' }, { headers: { ETag: '"current"' } });
    await editing;
    page.form.controls.name.setValue('New');
    const saving = page.save();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const update = http.expectOne('/api/v1/collections/id');
    expect(update.request.headers.get('If-Match')).toBe('"current"');
    update.flush({ id: 'id', name: 'New', revision: '3' }, { headers: { ETag: '"next"' } });
    await vi.waitFor(() => {
      http
        .expectOne('/api/v1/collections?limit=100')
        .flush({ items: [{ id: 'id', name: 'New', revision: '3' }], nextCursor: null });
    });
    await saving;
    expect(page.form.controls.name.value).toBe('');
  });
});
