import { signal } from '@angular/core';
import { HouseholdSync, type SyncChange } from '../../core/sync/household-sync';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsPage } from './settings-page';
import { AuthSession } from '../auth/auth-session';
import { SessionState } from '../../core/session/session-state';

describe('Role-appropriate household settings', () => {
  let http: HttpTestingController;
  let changes = signal<SyncChange | null>(null);
  beforeEach(() => {
    changes = signal<SyncChange | null>(null);
    TestBed.configureTestingModule({
      imports: [SettingsPage],
      providers: [
        provideRouter([]),
        { provide: HouseholdSync, useValue: { change: changes } },
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
        {
          provide: AuthSession,
          useFactory: () => ({
            state: TestBed.inject(SessionState),
            logout: vi.fn(),
            endSession: vi.fn(),
          }),
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  async function load(role: 'Administrator' | 'Member') {
    TestBed.inject(SessionState).user.set({ id: 'user', householdId: 'household', role });
    const fixture = TestBed.createComponent(SettingsPage);
    http
      .expectOne('/api/v1/profile')
      .flush(
        { id: 'user', name: 'Ciaran', preferences: { existing: true }, revision: '1' },
        { headers: { ETag: '"opaque-profile"' } },
      );
    http.expectOne('/api/v1/household/settings').flush(
      {
        id: 'household',
        name: 'Home',
        timezone: 'UTC',
        revision: '3',
        ai: {
          enabled: true,
          provider: 'OpenAICompatible',
          model: 'local-model',
          baseUrl: 'http://localhost:11434/v1',
          hasApiKey: true,
        },
      },
      { headers: { ETag: '"opaque-household"' } },
    );
    await vi.waitFor(() =>
      http.expectOne('/api/v1/household/members').flush({ items: [], nextCursor: null }),
    );
    if (role === 'Administrator')
      await vi.waitFor(() =>
        http.expectOne('/api/v1/household/invitations').flush({ items: [], nextCursor: null }),
      );
    await fixture.whenStable();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    fixture.detectChanges();
    return fixture;
  }
  it('retains a dirty personal form on a cross-device preference invalidation', async () => {
    const fixture = await load('Member');
    const page = fixture.componentInstance;
    page.profileForm.controls.name.setValue('Unsaved name');
    page.profileForm.markAsDirty();
    const reload = vi.spyOn(page, 'reload');
    changes.set({ sequence: 1, personal: true, domains: [] });
    TestBed.tick();
    expect(page.profileForm.controls.name.value).toBe('Unsaved name');
    expect(page.remoteChanged()).toBe(true);
    expect(reload).not.toHaveBeenCalled();
  });
  it('does not expose invitation or household editing controls to a Member', async () => {
    const fixture = await load('Member');
    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).not.toContain('Create invitation');
    expect(text).not.toContain('Save household');
    expect(text).toContain('Save profile');
    http.expectNone('/api/v1/household/invitations');
  });
  it('preserves personal preferences and the exact backend ETag when changing a profile', async () => {
    const fixture = await load('Member');
    fixture.componentInstance.profileForm.controls.name.setValue('Updated');
    const save = fixture.componentInstance.saveProfile();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/profile');
    expect(request.request.headers.get('If-Match')).toBe('"opaque-profile"');
    expect(request.request.body).toEqual({ name: 'Updated', preferences: { existing: true } });
    request.flush(
      { id: 'user', name: 'Updated', preferences: { existing: true }, revision: '2' },
      { headers: { ETag: '"new-opaque-profile"' } },
    );
    await save;
  });
  it('keeps a configured key when the new-key field is blank and never fills a secret from GET', async () => {
    const fixture = await load('Administrator');
    expect(fixture.componentInstance.householdForm.controls.apiKey.value).toBe('');
    const save = fixture.componentInstance.saveHousehold();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/household/settings');
    expect(request.request.headers.get('If-Match')).toBe('"opaque-household"');
    expect(request.request.body.ai.apiKey).toBeUndefined();
    expect(request.request.body.ai.clearApiKey).toBe(false);
    request.flush(
      {
        id: 'household',
        name: 'Home',
        timezone: 'UTC',
        revision: '4',
        ai: {
          enabled: true,
          provider: 'OpenAICompatible',
          model: 'local-model',
          baseUrl: 'http://localhost:11434/v1',
          hasApiKey: true,
        },
      },
      { headers: { ETag: '"next"' } },
    );
    await save;
  });
});
