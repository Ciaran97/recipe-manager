import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';
const storageKey = 'recipe-manager.theme';

@Injectable({ providedIn: 'root' })
export class Theme {
  private readonly document = inject(DOCUMENT);
  private readonly window = this.document.defaultView;
  private readonly media = this.window?.matchMedia?.('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.media?.matches ?? false);
  readonly preference = signal<ThemePreference>(this.read());
  readonly resolved = computed(() =>
    this.preference() === 'system' ? (this.systemDark() ? 'dark' : 'light') : this.preference(),
  );

  constructor() {
    this.apply();
    const changed = (event: MediaQueryListEvent) => {
      this.systemDark.set(event.matches);
      this.apply();
    };
    const storage = (event: StorageEvent) => {
      if (event.key === storageKey || event.key === null) {
        this.preference.set(this.read());
        this.apply();
      }
    };
    this.media?.addEventListener('change', changed);
    this.window?.addEventListener('storage', storage);
    inject(DestroyRef).onDestroy(() => {
      this.media?.removeEventListener('change', changed);
      this.window?.removeEventListener('storage', storage);
    });
  }
  set(value: string): void {
    if (value !== 'system' && value !== 'light' && value !== 'dark') return;
    this.preference.set(value);
    try {
      this.window?.localStorage.setItem(storageKey, value);
    } catch {
      /* A theme still works when storage is unavailable. */
    }
    this.apply();
  }
  private read(): ThemePreference {
    try {
      const value = this.window?.localStorage.getItem(storageKey);
      return value === 'light' || value === 'dark' ? value : 'system';
    } catch {
      return 'system';
    }
  }
  private apply(): void {
    this.document.documentElement.dataset['theme'] = this.resolved();
    this.document.documentElement.style.colorScheme = this.resolved();
  }
}
