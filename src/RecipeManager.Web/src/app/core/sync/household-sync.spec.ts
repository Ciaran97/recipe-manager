import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HouseholdSync, SYNC_CONNECTION, type SyncConnection } from './household-sync';
import { SessionState } from '../session/session-state';

class Connection implements SyncConnection {
  handlers = new Map<string, (value: unknown) => void>();
  calls: string[] = [];
  start = vi.fn(async () => {});
  stop = vi.fn(async () => {});
  on(name: string, handler: (value: unknown) => void) {
    this.handlers.set(name, handler);
  }
  invoke = vi.fn(async (name: string) => {
    this.calls.push(name);
  });
  onreconnected(handler: () => void) {
    this.reconnected = handler;
  }
  onclose(handler: () => void) {
    this.closed = handler;
  }
  reconnected?: () => void;
  closed?: () => void;
}
const revisions = {
  contentRevision: '4',
  recipesRevision: '2',
  plannerRevision: '1',
  shoppingRevision: '3',
  settingsRevision: '1',
  membershipRevision: '1',
  ownPreferencesRevision: '5',
};
describe('Household sync', () => {
  let http: HttpTestingController;
  let connection: Connection;
  let sync: HouseholdSync;
  let session: SessionState;
  beforeEach(() => {
    connection = new Connection();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SYNC_CONNECTION, useValue: () => connection },
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
  async function login() {
    session.user.set({ id: 'user', householdId: 'household', role: 'Member' });
    TestBed.tick();
    http.expectOne('/api/v1/household/sync').flush(revisions, { headers: { ETag: '"compound"' } });
    await new Promise((resolve) => setTimeout(resolve, 0));
    for (const request of http.match('/api/v1/household/sync'))
      request.flush(null, { status: 304, statusText: 'Not Modified' });
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
  it('uses the compound validator and emits no invalidation on 304', async () => {
    await login();
    const before = sync.change();
    const pending = sync.refresh();
    const request = http.expectOne('/api/v1/household/sync');
    expect(request.request.headers.get('If-None-Match')).toBe('"compound"');
    request.flush(null, { status: 304, statusText: 'Not Modified' });
    await pending;
    expect(sync.change()).toBe(before);
    expect(connection.calls).toContain('SubscribeHousehold');
  });
  it('coalesces refreshes and detects requesting-user preference changes', async () => {
    await login();
    const a = sync.refresh();
    const b = sync.refresh();
    http
      .expectOne('/api/v1/household/sync')
      .flush({ ...revisions, ownPreferencesRevision: '6' }, { headers: { ETag: '"personal"' } });
    await Promise.all([a, b]);
    expect(sync.change()?.personal).toBe(true);
    expect(sync.change()?.domains).toEqual([]);
  });
  it('discards late events and stops the old connection after logout', async () => {
    await login();
    session.clear();
    TestBed.tick();
    const before = sync.change();
    connection.handlers.get('ShoppingListChanged')?.({ listId: 'old', revision: '9' });
    expect(sync.change()).toBe(before);
    expect(connection.stop).toHaveBeenCalled();
    http.expectNone('/api/v1/household/sync');
  });
  it('resubscribes visible lists and refreshes after reconnection', async () => {
    await login();
    sync.watchList('list');
    await Promise.resolve();
    connection.reconnected?.();
    await new Promise((resolve) => setTimeout(resolve, 0));
    http
      .expectOne('/api/v1/household/sync')
      .flush(null, { status: 304, statusText: 'Not Modified' });
    expect(connection.calls.filter((x) => x === 'SubscribeList').length).toBeGreaterThanOrEqual(2);
  });
  it('retries an initial transport failure and subscribes after recovery', async () => {
    connection.start.mockRejectedValueOnce(new Error('Network unavailable'));
    await login();
    await vi.waitFor(() => expect(connection.start).toHaveBeenCalledTimes(2), { timeout: 2000 });
    await new Promise((resolve) => setTimeout(resolve, 0));
    http
      .expectOne('/api/v1/household/sync')
      .flush(null, { status: 304, statusText: 'Not Modified' });
    expect(connection.calls).toContain('SubscribeHousehold');
  });
  it('refreshes immediately on focus and foreground visibility', async () => {
    await login();
    const visibility = Object.getOwnPropertyDescriptor(document, 'visibilityState');
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    vi.useFakeTimers();
    try {
      window.dispatchEvent(new Event('focus'));
      http
        .expectOne('/api/v1/household/sync')
        .flush(null, { status: 304, statusText: 'Not Modified' });
      await Promise.resolve();
      await Promise.resolve();
      // The interval was registered before fake timers; foreground visibility uses the same refresh path.
      document.dispatchEvent(new Event('visibilitychange'));
      http
        .expectOne('/api/v1/household/sync')
        .flush(null, { status: 304, statusText: 'Not Modified' });
    } finally {
      vi.useRealTimers();
      if (visibility) Object.defineProperty(document, 'visibilityState', visibility);
      else Reflect.deleteProperty(document, 'visibilityState');
    }
  });
});
