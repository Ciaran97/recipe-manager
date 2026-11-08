import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { AuthSession, invitationToken } from './auth-session';
import { ApiClient } from '../../core/api/api-client';

describe('Household session workflows', () => {
  let auth: AuthSession;
  let http: HttpTestingController;
  const navigate = vi.fn(() => Promise.resolve(true));
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigateByUrl: navigate } },
      ],
    });
    auth = TestBed.inject(AuthSession);
    http = TestBed.inject(HttpTestingController);
    navigate.mockClear();
  });
  afterEach(() => http.verify());

  it('routes a new installation toward setup without attempting a protected session', async () => {
    const restoring = auth.ensure();
    http.expectOne('/api/v1/setup').flush({ setupRequired: true });
    expect(await restoring).toBeNull();
    expect(auth.setupRequired()).toBe(true);
    http.expectNone('/api/v1/auth/me');
  });

  async function signIn(): Promise<void> {
    const login = auth.login('user@example.test', 'private-test-password');
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'anonymous-csrf' });
    http
      .expectOne('/api/v1/auth/login')
      .flush({ id: 'user', householdId: 'household', role: 'Member' });
    await login;
  }
  it('refreshes CSRF after login and exposes membership without storing credentials', async () => {
    await signIn();
    expect(auth.state.user()?.role).toBe('Member');
    TestBed.inject(ApiClient).write('PUT', '/api/v1/profile', {}, '"user:1"').subscribe();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'member-csrf' });
    const update = http.expectOne('/api/v1/profile');
    expect(update.request.headers.get('X-XSRF-TOKEN')).toBe('member-csrf');
    update.flush({});
  });
  it('does not claim all-session logout succeeded after a network failure', async () => {
    await signIn();
    const logout = auth.logout();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'member-csrf' });
    http.expectOne('/api/v1/auth/logout').flush({}, { status: 503, statusText: 'Unavailable' });
    await expect(logout).rejects.toBeDefined();
    expect(auth.state.user()).not.toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });
  it('clears protected membership on a current-session 401', async () => {
    await signIn();
    TestBed.inject(ApiClient)
      .read('/api/v1/profile')
      .subscribe({ error: () => undefined });
    http.expectOne('/api/v1/profile').flush({}, { status: 401, statusText: 'Unauthorized' });
    TestBed.tick();
    expect(auth.state.user()).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/login', { replaceUrl: true });
  });
  it('never forwards an invitation token to a different origin', () => {
    expect(invitationToken(' raw-token ', 'https://recipes.test')).toBe('raw-token');
    expect(
      invitationToken('https://recipes.test/invite?token=secret', 'https://recipes.test'),
    ).toBe('secret');
    expect(() =>
      invitationToken('https://foreign.test/invite?token=secret', 'https://recipes.test'),
    ).toThrow();
  });
});
