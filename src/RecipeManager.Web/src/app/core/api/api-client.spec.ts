import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiClient } from './api-client';

describe('API transport', () => {
  let api: ApiClient;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withNoXsrfProtection()), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiClient);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('bootstraps CSRF once for concurrent writes and preserves string amounts and ETags', () => {
    api.write('PUT', '/api/v1/recipes/a', { servings: '1.5' }, '"a:2"').subscribe();
    api.write('POST', '/api/v1/recipes', { servings: '0.5' }).subscribe();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-fixture' });
    const update = http.expectOne('/api/v1/recipes/a');
    expect(update.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-fixture');
    expect(update.request.headers.get('If-Match')).toBe('"a:2"');
    expect(update.request.body).toEqual({ servings: '1.5' });
    update.flush({ revision: '3' }, { headers: { ETag: '"a:3"' } });
    const create = http.expectOne('/api/v1/recipes');
    expect(create.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-fixture');
    create.flush({});
  });

  it('sends conditional reads and retains the revision string', () => {
    api.read('/api/v1/recipes/a', '"a:9007199254740993"').subscribe();
    const request = http.expectOne('/api/v1/recipes/a');
    expect(request.request.headers.get('If-None-Match')).toBe('"a:9007199254740993"');
    request.flush({});
  });

  it('returns 304 as a cache-validation response instead of a failure', () => {
    let status = 0;
    api.read('/api/v1/recipes/a', '"a:2"').subscribe((response) => {
      status = response.status;
    });
    http.expectOne('/api/v1/recipes/a').flush(null, { status: 304, statusText: 'Not Modified' });
    expect(status).toBe(304);
  });

  it('does not send a waiting write after the session changes', () => {
    let failed = false;
    api.write('POST', '/api/v1/recipes', {}).subscribe({
      error: () => {
        failed = true;
      },
    });
    const csrf = http.expectOne('/api/v1/auth/csrf');
    api.resetSession();
    csrf.flush({ token: 'old-token' });
    http.expectNone('/api/v1/recipes');
    expect(failed).toBe(true);
  });

  it('discards a successful write response from an old session', () => {
    let accepted = false;
    let rejected = false;
    api.write('POST', '/api/v1/recipes', {}).subscribe({
      next: () => {
        accepted = true;
      },
      error: () => {
        rejected = true;
      },
    });
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/recipes');
    api.sessionEstablished();
    request.flush({ id: 'old-session-recipe' });
    expect(accepted).toBe(false);
    expect(rejected).toBe(true);
    expect(api.authenticationRequired()).toBe(false);
  });

  it('rejects foreign URLs before disclosing cookies or CSRF', () => {
    expect(() => api.read('https://foreign.example/api/v1/recipes' as never)).toThrow();
    http.expectNone('https://foreign.example/api/v1/recipes');
  });

  it('discards responses from an old session without signing out the new session', () => {
    api.read('/api/v1/auth/me').subscribe({ error: () => undefined });
    const old = http.expectOne('/api/v1/auth/me');
    api.sessionEstablished();
    old.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(api.authenticationRequired()).toBe(false);
  });

  it('clears protected session transport state on 401 and requires fresh CSRF', () => {
    api.read('/api/v1/auth/me').subscribe({ error: () => undefined });
    http.expectOne('/api/v1/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(api.authenticationRequired()).toBe(true);
    api.write('POST', '/api/v1/auth/login', {}).subscribe();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'new-token' });
    const request = http.expectOne('/api/v1/auth/login');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('new-token');
    request.flush({});
  });
});
