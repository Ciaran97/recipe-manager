import { HouseholdSync } from '../../core/sync/household-sync';
import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';

type Organiser = components['schemas']['OrganiserRead'];
type Kind = 'collections' | 'tags';
@Component({
  selector: 'app-organisers-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: ` <header class="page-header">
      <div>
        <a routerLink="/recipes">Recipe library</a>
        <h1>Collections and tags</h1>
        <p>Shared with your household. Removing one keeps its recipes.</p>
      </div>
    </header>
    <div class="actions">
      <button
        type="button"
        (click)="switchKind('collections')"
        [attr.aria-pressed]="kind() === 'collections'"
        [disabled]="busy()"
      >
        Collections</button
      ><button
        type="button"
        (click)="switchKind('tags')"
        [attr.aria-pressed]="kind() === 'tags'"
        [disabled]="busy()"
      >
        Tags
      </button>
    </div>
    @if (remoteChanged()) {
      <p role="status">
        Collections or tags changed on another device. Reload the list; your unsaved name is
        retained.
      </p>
    }
    @if (error()) {
      <p role="alert" class="error">{{ error() }}</p>
    }
    @if (notice()) {
      <p role="status">{{ notice() }}</p>
    }
    <section class="card">
      <h2>
        {{ editing() ? 'Rename' : 'Create' }} {{ kind() === 'collections' ? 'collection' : 'tag' }}
      </h2>
      <form [formGroup]="form" (ngSubmit)="save()">
        <fieldset [disabled]="busy()">
          <label for="organiser-name"
            >Name<input id="organiser-name" formControlName="name" maxlength="200" /></label
          ><button type="submit" [disabled]="form.invalid">
            {{ editing() ? 'Save name' : 'Create' }}</button
          ><button type="button" (click)="cancel()">Cancel edit</button>
        </fieldset>
      </form>
    </section>
    <section class="card">
      <h2>{{ kind() === 'collections' ? 'Collections' : 'Tags' }}</h2>
      <button type="button" (click)="load()" [disabled]="busy()">Reload list</button>
      @if (busy()) {
        <p role="status">Working…</p>
      }
      <ul>
        @for (item of items(); track item.id) {
          <li>
            <span>{{ item.name }}</span>
            <button
              type="button"
              (click)="edit(item.id)"
              [disabled]="busy()"
              [attr.aria-label]="'Rename ' + item.name"
            >
              Rename
            </button>
            <button
              type="button"
              (click)="remove(item)"
              [disabled]="busy()"
              [attr.aria-label]="'Delete ' + item.name"
            >
              Delete
            </button>
          </li>
        }
      </ul>
      @if (!busy() && !items().length) {
        <p>No {{ kind() }} yet.</p>
      }
      @if (cursor()) {
        <button type="button" (click)="load(true)" [disabled]="busy()">Load more</button>
      }
    </section>`,
})
export class OrganisersPage {
  private readonly api = inject(ApiClient);
  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200), Validators.pattern(/\S/)]],
  });
  readonly remoteChanged = signal(false);
  readonly kind = signal<Kind>('collections');
  readonly items = signal<Organiser[]>([]);
  readonly cursor = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly editing = signal<string | null>(null);
  private etag = '';
  private retryKey = '';
  private retryBody = '';
  constructor() {
    const sync = inject(HouseholdSync);
    effect(() => {
      const change = sync.change();
      if (change?.domains.includes('recipesRevision'))
        untracked(() => this.remoteChanged.set(true));
    });
    void this.load();
  }
  hasUnsavedChanges(): boolean {
    return this.busy() || this.form.dirty;
  }
  private abandon(): boolean {
    return !this.form.dirty || window.confirm('Discard the unsaved organiser name?');
  }
  async switchKind(kind: Kind): Promise<void> {
    if (this.busy() || kind === this.kind() || !this.abandon()) return;
    this.reset();
    this.kind.set(kind);
    this.items.set([]);
    this.cursor.set(null);
    await this.load();
  }
  cancel(): void {
    if (!this.busy() && this.abandon()) this.reset();
  }
  private reset(): void {
    this.form.reset();
    this.editing.set(null);
    this.etag = '';
    this.retryKey = '';
    this.retryBody = '';
  }
  async load(append = false): Promise<void> {
    await this.perform(async () => {
      this.remoteChanged.set(false);
      const cursor = append ? this.cursor() : null;
      const response = await firstValueFrom(
        this.api.read<components['schemas']['OrganiserListPage']>(
          `/api/v1/${this.kind()}?limit=100${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
        ),
      );
      if (!response.body) return;
      const entries = new Map((append ? this.items() : []).map((x) => [x.id, x]));
      response.body.items.forEach((x) => entries.set(x.id, x));
      this.items.set([...entries.values()]);
      this.cursor.set(response.body.nextCursor ?? null);
    });
  }
  async edit(id: string): Promise<void> {
    if (this.busy() || !this.abandon()) return;
    await this.perform(async () => {
      const result = await firstValueFrom(this.api.read<Organiser>(`/api/v1/${this.kind()}/${id}`));
      if (!result.body) return;
      this.editing.set(id);
      this.etag = result.headers.get('ETag') ?? '';
      this.form.reset({ name: result.body.name });
    });
  }
  async save(): Promise<void> {
    if (this.busy() || this.form.invalid) return;
    let saved = false;
    await this.perform(async () => {
      const id = this.editing(),
        body = { name: this.form.controls.name.value.trim() };
      const fingerprint = JSON.stringify([this.kind(), body]);
      if (fingerprint !== this.retryBody) {
        this.retryBody = fingerprint;
        this.retryKey = crypto.randomUUID();
      }
      await firstValueFrom(
        this.api.write<Organiser>(
          id ? 'PUT' : 'POST',
          `/api/v1/${this.kind()}${id ? '/' + id : ''}`,
          body,
          id ? this.etag : undefined,
          id ? undefined : this.retryKey,
        ),
      );
      this.reset();
      this.notice.set('Saved.');
      saved = true;
    });
    if (saved) await this.load();
  }
  async remove(item: Organiser): Promise<void> {
    if (
      this.busy() ||
      !window.confirm(
        `Delete ${item.name}? Recipes will be kept; their association will be removed.`,
      )
    )
      return;
    let removed = false;
    await this.perform(async () => {
      const detail = await firstValueFrom(
        this.api.read<Organiser>(`/api/v1/${this.kind()}/${item.id}`),
      );
      if (!detail.body || detail.body.revision !== item.revision || detail.body.name !== item.name)
        throw new Error('This organiser changed. Reload the list before deleting it.');
      await firstValueFrom(
        this.api.write(
          'DELETE',
          `/api/v1/${this.kind()}/${item.id}`,
          undefined,
          detail.headers.get('ETag') ?? '',
        ),
      );
      if (this.editing() === item.id) this.reset();
      this.notice.set('Deleted. Recipes were kept.');
      removed = true;
    });
    if (removed) await this.load();
  }
  private async perform(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
