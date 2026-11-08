import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HouseholdSync, SYNC_CONNECTION, type SyncConnection } from './household-sync';
import { SessionState } from '../session/session-state';

class PersonalConnection implements SyncConnection {
  handlers = new Map<string, (value: unknown) => void>();
  async start() {}
  async stop() {}
  async invoke() {}
  on(name: string, callback: (value: unknown) => void) {
    this.handlers.set(name, callback);
  }
  onreconnected() {}
  onclose() {}
}
const baseline = {
  contentRevision: '5',
  recipesRevision: '2',
  plannerRevision: '1',
  shoppingRevision: '1',
  settingsRevision: '1',
  membershipRevision: '1',
  ownPreferencesRevision: '3',
};
describe('Personal notification and cache isolation', () => {
  let http: HttpTestingController;
  let sync: HouseholdSync;
  let session: SessionState;
  let connections: PersonalConnection[];
  beforeEach(() => {
    connections = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: SYNC_CONNECTION,
          useValue: () => {
            const connection = new PersonalConnection();
            connections.push(connection);
            return connection;
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    sync = TestBed.inject(HouseholdSync);
    session = TestBed.inject(SessionState);
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    http.verify();
  });
  async function establish(id: string, tag: string) {
    session.user.set({ id, householdId: 'household', role: 'Member' });
    TestBed.tick();
    const request = http.expectOne('/api/v1/household/sync');
    expect(request.request.headers.has('If-None-Match')).toBe(false);
    request.flush(baseline, { headers: { ETag: tag } });
    await new Promise((resolve) => setTimeout(resolve, 0));
    for (const pending of http.match('/api/v1/household/sync'))
      pending.flush(null, { status: 304, statusText: 'Not Modified' });
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
  it('fetches personal changes with the compound validator without invalidating household domains', async () => {
    await establish('user-a', '"user-a:5:3"');
    connections[0].handlers.get('PersonalChanged')?.({ ownPreferencesRevision: '4' });
    const request = http.expectOne('/api/v1/household/sync');
    expect(request.request.headers.get('If-None-Match')).toBe('"user-a:5:3"');
    request.flush(
      { ...baseline, ownPreferencesRevision: '4' },
      { headers: { ETag: '"user-a:5:4"' } },
    );
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(sync.change()?.personal).toBe(true);
    expect(sync.change()?.domains).toEqual([]);
  });
  it('does not reuse another account’s validator or accept its late notifications', async () => {
    await establish('user-a', '"user-a:5:3"');
    const old = connections[0];
    await establish('user-b', '"user-b:5:3"');
    old.handlers.get('PersonalChanged')?.({ ownPreferencesRevision: '99' });
    http.expectNone('/api/v1/household/sync');
    expect(sync.change()).toBeNull();
    const pending = sync.refresh();
    const request = http.expectOne('/api/v1/household/sync');
    expect(request.request.headers.get('If-None-Match')).toBe('"user-b:5:3"');
    request.flush(null, { status: 304, statusText: 'Not Modified' });
    await pending;
  });
});
