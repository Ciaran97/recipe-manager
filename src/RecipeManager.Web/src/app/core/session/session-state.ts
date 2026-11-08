import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { ApiClient } from '../api/api-client';
import { CanActivateFn, Router } from '@angular/router';

export interface SessionUser {
  id: string;
  householdId: string;
  role: 'Administrator' | 'Member';
}

@Injectable({ providedIn: 'root' })
export class SessionState {
  private readonly api = inject(ApiClient);
  constructor() {
    effect(() => {
      if (this.api.authenticationRequired()) this.clear();
    });
  }
  readonly user = signal<SessionUser | null>(null);
  readonly canManageHousehold = computed(() => this.user()?.role === 'Administrator');
  clear(): void {
    this.user.set(null);
  }
}

// Presentation only; backend always enforces the current database membership.
export const administratorGuard: CanActivateFn = () =>
  inject(SessionState).canManageHousehold() ? true : inject(Router).createUrlTree(['/settings']);
