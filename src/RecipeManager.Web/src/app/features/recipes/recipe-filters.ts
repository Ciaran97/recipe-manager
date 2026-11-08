import type { OnInit } from '@angular/core';
import type { HttpResponse } from '@angular/common/http';
import { Component, computed, inject, input, model, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';

@Component({
  selector: 'app-organiser-picker',
  template: `<fieldset>
    <legend>{{ kind() === 'collections' ? 'Collections' : 'Tags' }}</legend>
    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
    @if (loading()) {
      <p role="status">Loading {{ kind() }}…</p>
    }
    @for (item of options(); track item.id) {
      <label class="check"
        ><input
          type="checkbox"
          [checked]="selected().includes(item.id)"
          (change)="toggle(item.id)"
        />{{ item.name }}</label
      >
    }
    @for (id of missing(); track id) {
      <label class="check"
        ><input type="checkbox" checked (change)="toggle(id)" />Unavailable selection — uncheck to
        remove</label
      >
    }
    @if (!loading() && !options().length) {
      <p>No {{ kind() }} yet.</p>
    }
    @if (allowCreate()) {
      <label
        >{{ kind() === 'collections' ? 'New collection name' : 'New tag name' }}
        <input
          [value]="newName()"
          (input)="newName.set($any($event.target).value)"
          maxlength="200"
          [disabled]="creating()"
        />
      </label>
      <button
        type="button"
        (click)="create()"
        [disabled]="creating() || loading() || !newName().trim()"
      >
        {{ kind() === 'collections' ? 'Create collection' : 'Create tag' }}
      </button>
    }
    <button type="button" (click)="load()" [disabled]="loading() || creating()">
      Reload {{ kind() }}
    </button>
  </fieldset>`,
})
export class OrganiserPicker implements OnInit {
  readonly kind = input.required<'collections' | 'tags'>();
  readonly selected = model<string[]>([]);
  readonly allowCreate = input(false);
  readonly newName = signal('');
  readonly creating = signal(false);
  private retryName = '';
  private retryKey = '';
  async create(): Promise<void> {
    const name = this.newName().trim();
    if (!this.allowCreate() || !name || this.creating() || this.loading()) return;
    this.creating.set(true);
    this.error.set('');
    if (name !== this.retryName) {
      this.retryName = name;
      this.retryKey = crypto.randomUUID();
    }
    try {
      const response = await firstValueFrom(
        this.api.write<components['schemas']['OrganiserRead']>(
          'POST',
          `/api/v1/${this.kind()}`,
          { name },
          undefined,
          this.retryKey,
        ),
      );
      if (!response.body) throw new Error('The new organiser could not be loaded.');
      const item = response.body;
      this.options.update((items) => [...items.filter((x) => x.id !== item.id), item]);
      this.selected.update((ids) => (ids.includes(item.id) ? ids : [...ids, item.id]));
      this.newName.set('');
      this.retryName = '';
      this.retryKey = '';
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.creating.set(false);
    }
  }
  readonly options = signal<components['schemas']['OrganiserRead'][]>([]);
  readonly missing = computed(() =>
    this.selected().filter((id) => !this.options().some((x) => x.id === id)),
  );
  readonly loading = signal(false);
  readonly error = signal('');
  private readonly api = inject(ApiClient);
  ngOnInit(): void {
    void this.load();
  }
  toggle(id: string): void {
    this.selected.update((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));
  }
  async load(): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    try {
      const rows: components['schemas']['OrganiserRead'][] = [];
      let cursor: string | null = null;
      do {
        const response: HttpResponse<components['schemas']['OrganiserListPage']> =
          await firstValueFrom(
            this.api.read<components['schemas']['OrganiserListPage']>(
              `/api/v1/${this.kind()}?limit=100${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
            ),
          );
        if (!response.body) throw new Error('Unavailable organiser list.');
        rows.push(...response.body.items);
        const next: string | null = response.body.nextCursor ?? null;
        if (next && next === cursor) throw new Error('Invalid organiser pagination.');
        cursor = next;
      } while (cursor);
      this.options.set(rows);
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.loading.set(false);
    }
  }
}
