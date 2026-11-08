import { TestBed } from '@angular/core/testing';
import { HttpResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { Subject } from 'rxjs';
import { ApiClient } from '../api/api-client';
import type { components } from '../api/generated/schema';
import { HouseholdSync } from '../sync/household-sync';
import { SessionState, type SessionUser } from './session-state';
import { LibraryNavigation } from './library-navigation';

it('clears sidebar data on account changes and ignores a late previous-household response', async () => {
  const user = signal<SessionUser | null>({
    id: 'first-user',
    householdId: 'first-home',
    role: 'Member',
  });
  const previous = new Subject<HttpResponse<components['schemas']['OrganiserListPage']>>();
  const next = new Subject<HttpResponse<components['schemas']['OrganiserListPage']>>();
  const read = vi.fn().mockReturnValueOnce(previous).mockReturnValueOnce(next);
  TestBed.configureTestingModule({
    providers: [
      { provide: SessionState, useValue: { user } },
      { provide: HouseholdSync, useValue: { change: signal(null) } },
      { provide: ApiClient, useValue: { read } },
    ],
  });
  const navigation = TestBed.inject(LibraryNavigation);
  TestBed.tick();
  user.set({ id: 'second-user', householdId: 'second-home', role: 'Member' });
  TestBed.tick();
  expect(navigation.collections()).toEqual([]);
  previous.next(
    new HttpResponse({
      body: {
        items: [{ id: 'private-old', name: 'Previous household', revision: '1' }],
        nextCursor: null,
      },
    }),
  );
  await Promise.resolve();
  expect(navigation.collections()).toEqual([]);
  next.next(
    new HttpResponse({
      body: {
        items: [{ id: 'current', name: 'Current household', revision: '1' }],
        nextCursor: null,
      },
    }),
  );
  await Promise.resolve();
  expect(navigation.collections().map((item) => item.id)).toEqual(['current']);
  user.set(null);
  TestBed.tick();
  expect(navigation.collections()).toEqual([]);
});
