import { Injectable, effect, inject, signal } from '@angular/core';
import type { HttpResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../api/api-client';
import type { components } from '../api/generated/schema';
import { HouseholdSync } from '../sync/household-sync';
import { SessionState } from './session-state';

@Injectable({ providedIn: 'root' })
export class LibraryNavigation {
  readonly collections = signal<components['schemas']['OrganiserRead'][]>([]);
  readonly unavailable = signal(false);
  private readonly api = inject(ApiClient);
  private generation = 0;
  private identity = '';
  constructor() {
    const session = inject(SessionState);
    const sync = inject(HouseholdSync);
    effect(() => {
      const user = session.user();
      const change = sync.change();
      const identity = user ? `${user.householdId}:${user.id}` : '';
      const changedIdentity = identity !== this.identity;
      if (changedIdentity) {
        this.identity = identity;
        this.generation++;
        this.collections.set([]);
        this.unavailable.set(false);
      }
      if (!user) {
        this.generation++;
        this.collections.set([]);
        this.unavailable.set(false);
      } else if (changedIdentity || !change || change.domains.includes('recipesRevision'))
        void this.load();
    });
  }
  private async load(): Promise<void> {
    const generation = ++this.generation;
    try {
      const rows: components['schemas']['OrganiserRead'][] = [];
      let cursor: string | null = null;
      do {
        const response: HttpResponse<components['schemas']['OrganiserListPage']> =
          await firstValueFrom(
            this.api.read(
              `/api/v1/collections?limit=100${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
            ),
          );
        if (generation !== this.generation) return;
        rows.push(...(response.body?.items ?? []));
        const next: string | null = response.body?.nextCursor ?? null;
        if (next && next === cursor) throw new Error('Invalid collection cursor.');
        cursor = next;
      } while (cursor);
      if (generation !== this.generation) return;
      this.collections.set(rows.sort((a, b) => a.name.localeCompare(b.name)));
      this.unavailable.set(false);
    } catch {
      if (generation === this.generation) this.unavailable.set(true);
    }
  }
}
