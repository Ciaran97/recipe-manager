import { DestroyRef, InjectionToken, Injectable, effect, inject, signal } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { SessionState } from '../session/session-state';
import type { components } from '../api/generated/schema';

type Revisions = components['schemas']['HouseholdSyncRead'];
type Domain =
  | 'recipesRevision'
  | 'plannerRevision'
  | 'shoppingRevision'
  | 'settingsRevision'
  | 'membershipRevision';
export interface SyncConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(method: string, ...args: unknown[]): Promise<unknown>;
  on(method: string, callback: (value: unknown) => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
}
export const SYNC_CONNECTION = new InjectionToken<() => SyncConnection>(
  'Household SignalR connection',
  {
    providedIn: 'root',
    factory: () => () =>
      new HubConnectionBuilder()
        .withUrl('/hubs/shopping', { withCredentials: true })
        .configureLogging(LogLevel.None)
        .withAutomaticReconnect([0, 1000, 3000, 10000, 30000])
        .build(),
  },
);
export interface SyncChange {
  sequence: number;
  domains: Domain[];
  personal: boolean;
  list?: { id: string; revision: string; deleted: boolean };
  recovered?: boolean;
}
interface Context {
  hub: SyncConnection;
  tag?: string;
  revisions?: Revisions;
  request?: Promise<void>;
  again: boolean;
  ready: boolean;
  retryCount: number;
  retry?: ReturnType<typeof setTimeout>;
}
const domains: Domain[] = [
  'recipesRevision',
  'plannerRevision',
  'shoppingRevision',
  'settingsRevision',
  'membershipRevision',
];
function listMessage(value: unknown): value is { listId: string; revision: string } {
  if (!value || typeof value !== 'object') return false;
  const row = value as Record<string, unknown>;
  return (
    typeof row['listId'] === 'string' &&
    typeof row['revision'] === 'string' &&
    /^\d+$/.test(row['revision'])
  );
}
@Injectable({ providedIn: 'root' })
export class HouseholdSync {
  private readonly session = inject(SessionState);
  private readonly api = inject(ApiClient);
  private readonly createConnection = inject(SYNC_CONNECTION);
  private context?: Context;
  private readonly watched = new Map<string, number>();
  private sequence = 0;
  readonly change = signal<SyncChange | null>(null);
  constructor() {
    effect(() => {
      const user = this.session.user();
      this.stop();
      this.change.set(null);
      if (user) this.begin();
    });
    const foreground = () => {
      if (document.visibilityState === 'visible') void this.refresh();
    };
    window.addEventListener('focus', foreground);
    document.addEventListener('visibilitychange', foreground);
    const timer = setInterval(foreground, 30000);
    inject(DestroyRef).onDestroy(() => {
      this.stop();
      clearInterval(timer);
      window.removeEventListener('focus', foreground);
      document.removeEventListener('visibilitychange', foreground);
    });
  }
  watchList(id: string): () => void {
    this.watched.set(id, (this.watched.get(id) ?? 0) + 1);
    const context = this.context;
    if (context?.ready)
      void context.hub.invoke('SubscribeList', id).catch(() => {
        void this.refresh();
      });
    return () => {
      const count = (this.watched.get(id) ?? 1) - 1;
      if (count > 0) this.watched.set(id, count);
      else {
        this.watched.delete(id);
        if (this.context?.ready)
          void this.context.hub.invoke('UnsubscribeList', id).catch(() => {});
      }
    };
  }
  private begin(): void {
    const context: Context = {
      hub: this.createConnection(),
      again: false,
      ready: false,
      retryCount: 0,
    };
    this.context = context;
    for (const name of ['HouseholdChanged', 'PersonalChanged'])
      context.hub.on(name, () => {
        if (this.context !== context) return;
        if (context.request) context.again = true;
        void this.refresh();
      });
    for (const name of ['ShoppingListChanged', 'ShoppingListDeleted'])
      context.hub.on(name, (value) => {
        if (this.context !== context || !listMessage(value)) return;
        this.change.set({
          sequence: ++this.sequence,
          domains: [],
          personal: false,
          list: {
            id: value.listId,
            revision: value.revision,
            deleted: name === 'ShoppingListDeleted',
          },
        });
      });
    context.hub.onreconnected(() => {
      if (this.context === context) void this.connected(context);
    });
    context.hub.onclose(() => {
      if (this.context === context) {
        context.ready = false;
        this.retry(context);
      }
    });
    void this.connect(context);
    void this.refresh();
  }
  private async connect(context: Context): Promise<void> {
    try {
      await context.hub.start();
      if (this.context !== context) {
        await context.hub.stop();
        return;
      }
      await this.connected(context);
    } catch {
      if (this.context === context) this.retry(context);
    }
  }
  private async connected(context: Context): Promise<void> {
    try {
      await context.hub.invoke('SubscribeHousehold');
      for (const id of this.watched.keys()) {
        if (this.context !== context) return;
        await context.hub.invoke('SubscribeList', id);
      }
      if (this.context !== context) return;
      context.ready = true;
      context.retryCount = 0;
      // A conditional read recovers notifications missed while disconnected.
      if (context.request) context.again = true;
      else await this.refresh();
    } catch {
      if (this.context === context) {
        context.ready = false;
        await context.hub.stop();
        this.retry(context);
      }
    }
  }
  private retry(context: Context): void {
    if (context.retry || this.context !== context) return;
    const delay = Math.min(30000, 1000 * 2 ** Math.min(context.retryCount++, 5));
    context.retry = setTimeout(() => {
      context.retry = undefined;
      if (this.context === context) void this.connect(context);
    }, delay);
  }
  refresh(): Promise<void> {
    const context = this.context;
    if (!context) return Promise.resolve();
    if (context.request) return context.request;
    context.request = this.read(context).finally(() => {
      context.request = undefined;
      if (this.context === context && context.again) {
        context.again = false;
        void this.refresh();
      }
    });
    return context.request;
  }
  private async read(context: Context): Promise<void> {
    try {
      const response = await firstValueFrom(
        this.api.read<Revisions>('/api/v1/household/sync', context.tag),
      );
      if (this.context !== context || response.status === 304 || !response.body) return;
      const previous = context.revisions,
        next = response.body;
      context.tag = response.headers.get('ETag') ?? undefined;
      context.revisions = next;
      const changed = domains.filter((key) => !previous || previous[key] !== next[key]);
      const personal = !previous || previous.ownPreferencesRevision !== next.ownPreferencesRevision;
      if (previous && (changed.length || personal))
        this.change.set({ sequence: ++this.sequence, domains: changed, personal, recovered: true });
    } catch {
      /* Authentication failures clear SessionState through ApiClient; transient failures retry on focus/fallback. */
    }
  }
  private stop(): void {
    const old = this.context;
    this.context = undefined;
    if (!old) return;
    if (old.retry) clearTimeout(old.retry);
    void old.hub.stop().catch(() => {});
  }
}
