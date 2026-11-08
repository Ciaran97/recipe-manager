import { Component, DestroyRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { apiMessage } from '../auth/auth-session';
import { RecipeEditor, type RecipeImportSeed } from './recipe-editor';

type Draft = components['schemas']['ImportDraftRead'];
type Write = components['schemas']['RecipeWrite'];
type Review = components['schemas']['ImportReview'];
type Result = components['schemas']['ImportCommitResult'];
interface PendingCommit {
  draft: Draft;
  etag: string;
  review: Review;
  key: string;
}

@Component({
  selector: 'app-import-review',
  imports: [ReactiveFormsModule, RouterLink, RecipeEditor, DatePipe],
  templateUrl: './import-review.html',
  styles: `
    .import-source {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      max-height: 24rem;
      overflow: auto;
    }
    .candidate-choices {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .candidate-choices [aria-pressed='true'] {
      outline: 2px solid #154a35;
      background: #e3eee4;
    }
    .import-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      align-items: center;
    }
    textarea {
      min-height: 14rem;
    }
  `,
})
export class ImportReview {
  private readonly api = inject(ApiClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  readonly editor = viewChild(RecipeEditor);
  readonly source = this.fb.nonNullable.group({
    kind: this.fb.nonNullable.control<'url' | 'text'>('url'),
    url: ['', Validators.maxLength(2048)],
    text: [''],
  });
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly aiEnabled = signal(false);
  readonly settingsLoaded = signal(false);
  readonly draft = signal<Draft | null>(null);
  readonly seed = signal<RecipeImportSeed | null>(null);
  readonly selectedIndex = signal(0);
  readonly uncertainSave = signal(false);
  private etag = '';
  private sourceKey = '';
  private fingerprint = '';
  private commitKey = crypto.randomUUID();
  private pending?: PendingCommit;
  private committed = false;
  private epoch = 0;
  private readonly cancel = new Subject<void>();

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.epoch++;
      this.cancel.next();
      this.cancel.complete();
    });
    void this.loadSettings();
    const id = this.route.snapshot.queryParamMap.get('draft');
    if (id) void this.reload(id);
  }
  isBusy(): boolean {
    return this.busy() || !!this.editor()?.busy();
  }
  private async loadSettings(): Promise<void> {
    try {
      const response = await firstValueFrom(
        this.api.read<components['schemas']['HouseholdSettingsRead']>('/api/v1/household/settings'),
      );
      this.aiEnabled.set(response.body?.ai.enabled ?? false);
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.settingsLoaded.set(true);
    }
  }
  async start(): Promise<void> {
    if (this.isBusy()) return;
    const value = this.source.getRawValue();
    const input = value.kind === 'url' ? value.url : value.text;
    if (!input.trim()) {
      this.error.set('Enter a recipe URL or paste recipe text.');
      return;
    }
    if (
      (value.kind === 'url' && input.length > 2048) ||
      (value.kind === 'text' && new TextEncoder().encode(input).length > 100 * 1024)
    ) {
      this.error.set(
        'Use a URL up to 2,048 characters or pasted text up to 100 KiB. Your input is retained.',
      );
      return;
    }
    if (value.kind === 'text' && !this.aiEnabled()) {
      this.error.set(
        'Text import needs a configured household AI provider. URL and manual entry are available.',
      );
      return;
    }
    const fingerprint = JSON.stringify([value.kind, input]);
    if (fingerprint !== this.fingerprint) {
      this.fingerprint = fingerprint;
      this.sourceKey = crypto.randomUUID();
    }
    const epoch = ++this.epoch;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      const body = value.kind === 'url' ? { url: input } : { text: input };
      const response = await firstValueFrom(
        this.api
          .write<
            components['schemas']['ImportStartResult']
          >('POST', `/api/v1/imports/${value.kind}`, body, undefined, this.sourceKey)
          .pipe(takeUntil(this.cancel)),
      );
      if (epoch !== this.epoch) return;
      if (response.body?.commitResult) {
        this.committed = true;
        await this.router.navigate(['/recipes', response.body.commitResult.recipeId]);
        return;
      }
      const draft = response.body?.draft;
      if (!draft) throw new Error('The import returned no review draft. Your input is retained.');
      await this.install(draft, response.headers.get('ETag') ?? '', epoch);
      if (epoch === this.epoch)
        await this.router.navigate(['/recipes/import'], {
          queryParams: { draft: draft.id },
          replaceUrl: true,
        });
    } catch (error) {
      if (epoch === this.epoch) this.error.set(apiMessage(error));
    } finally {
      if (epoch === this.epoch) this.busy.set(false);
    }
  }
  cancelExtraction(): void {
    this.epoch++;
    this.cancel.next();
    this.busy.set(false);
    this.error.set('');
    this.notice.set('Import cancelled. Your input is retained.');
  }
  async reload(id = this.draft()?.id): Promise<void> {
    if (!id || this.isBusy()) return;
    if (this.uncertainSave()) {
      this.error.set('Check the previous save before replacing this review.');
      return;
    }
    if (
      this.editor()?.hasUnsavedChanges() &&
      !window.confirm('Replace your unsaved review with the saved draft?')
    )
      return;
    const epoch = ++this.epoch;
    this.busy.set(true);
    this.error.set('');
    try {
      const response = await firstValueFrom(
        this.api.read<Draft>(`/api/v1/imports/${id}`).pipe(takeUntil(this.cancel)),
      );
      if (epoch === this.epoch && response.body)
        await this.install(response.body, response.headers.get('ETag') ?? '', epoch);
    } catch (error) {
      if (epoch === this.epoch) this.error.set(apiMessage(error));
    } finally {
      if (epoch === this.epoch) this.busy.set(false);
    }
  }
  private async install(draft: Draft, etag: string, epoch: number): Promise<void> {
    this.draft.set(draft);
    this.etag = etag;
    this.selectedIndex.set(draft.reviewedRecipe ? -1 : 0);
    this.source.patchValue({
      kind: draft.sourceKind === 'Url' ? 'url' : 'text',
      url: draft.sourceKind === 'Url' ? draft.originalInput : '',
      text: draft.sourceKind === 'Text' ? draft.originalInput : '',
    });
    const candidate = draft.extraction?.recipes[0];
    if (draft.reviewedRecipe) {
      const names: Record<string, string> = {};
      // Presentation labels only; IDs and quantities are validated/calculated by the backend.
      const ids = [...new Set(draft.reviewedRecipe.ingredients.map((row) => row.ingredientId))];
      for (let offset = 0; offset < ids.length; offset += 4)
        await Promise.all(
          ids.slice(offset, offset + 4).map(async (id) => {
            try {
              const response = await firstValueFrom(
                this.api
                  .read<components['schemas']['IngredientRead']>(`/api/v1/ingredients/${id}`)
                  .pipe(takeUntil(this.cancel)),
              );
              if (response.body) names[id] = response.body.canonicalName;
            } catch (error) {
              if (!(error instanceof HttpErrorResponse && error.status === 404)) throw error;
              this.notice.set('An ingredient was removed. Select a replacement before saving.');
            }
          }),
        );
      if (epoch === this.epoch)
        this.seed.set({ candidate, reviewedRecipe: draft.reviewedRecipe, ingredientNames: names });
    } else if (candidate) this.seed.set({ candidate });
    else
      throw new Error(
        'This draft has no structured recipe. Keep the original input and enter it manually.',
      );
  }
  chooseCandidate(index: number): void {
    const candidates = this.draft()?.extraction?.recipes;
    if (
      !candidates?.[index] ||
      this.isBusy() ||
      this.uncertainSave() ||
      index === this.selectedIndex()
    )
      return;
    if (
      this.editor()?.hasUnsavedChanges() &&
      !window.confirm('Replace your edits with the selected imported recipe?')
    )
      return;
    this.selectedIndex.set(index);
    this.seed.set({ candidate: candidates[index] });
  }
  readonly saveImported = async (recipe: Write): Promise<string> => {
    if (this.uncertainSave())
      throw new Error('Check the previous save before changing this import.');
    const draft = this.draft();
    if (!draft) throw new Error('Reload the import draft before saving.');
    const attempt: PendingCommit = {
      draft,
      etag: this.etag,
      review: {
        recipe: structuredClone(recipe),
        confirmedMeasurementStandard: recipe.measurementStandard ?? null,
        reviewConfirmed: true,
      },
      key: this.commitKey,
    };
    this.pending = attempt;
    try {
      const result = await this.commit(attempt);
      this.committed = true;
      this.pending = undefined;
      this.uncertainSave.set(false);
      return result.recipeId;
    } catch (error) {
      this.uncertainSave.set(
        !(error instanceof HttpErrorResponse) ||
          error.status === 0 ||
          error.status >= 500 ||
          (error.status >= 200 && error.status < 300),
      );
      if (!this.uncertainSave()) this.pending = undefined;
      throw error;
    }
  };
  private async commit(attempt: PendingCommit): Promise<Result> {
    if (attempt.draft.handoffId) {
      const status = await firstValueFrom(
        this.api.read<components['schemas']['ImportHandoffRead']>(
          `/api/v1/import-handoffs/${attempt.draft.handoffId}`,
        ),
      );
      if (status.body?.result) return status.body.result;
      const response = await firstValueFrom(
        this.api.write<Result>(
          'POST',
          `/api/v1/import-handoffs/${attempt.draft.handoffId}/commit`,
          {
            draftId: attempt.draft.id,
            expectedDraftRevision: attempt.draft.revision,
            review: attempt.review,
          },
        ),
      );
      if (response.body) return response.body;
    } else {
      const response = await firstValueFrom(
        this.api.write<Result>(
          'POST',
          `/api/v1/imports/${attempt.draft.id}/commit`,
          attempt.review,
          attempt.etag,
          attempt.key,
        ),
      );
      if (response.body) return response.body;
    }
    throw new Error(
      'The save returned no result. Check the previous save before trying a new import.',
    );
  }
  async retrySave(): Promise<void> {
    if (!this.pending || this.isBusy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.commit(this.pending);
      this.committed = true;
      this.pending = undefined;
      this.uncertainSave.set(false);
      await this.router.navigate(['/recipes', result.recipeId]);
    } catch (error) {
      this.error.set(apiMessage(error));
      const uncertain =
        !(error instanceof HttpErrorResponse) ||
        error.status === 0 ||
        error.status >= 500 ||
        (error.status >= 200 && error.status < 300);
      this.uncertainSave.set(uncertain);
      if (!uncertain) {
        this.pending = undefined;
        this.editor()?.error.set('');
      }
    } finally {
      this.busy.set(false);
    }
  }
  async discard(): Promise<void> {
    if (this.isBusy()) return;
    if (
      this.uncertainSave() &&
      !window.confirm('The previous save may already have succeeded. Leave without checking it?')
    )
      return;
    const draft = this.draft();
    this.busy.set(true);
    this.error.set('');
    try {
      if (draft && !this.uncertainSave()) {
        try {
          await firstValueFrom(
            this.api.write('DELETE', `/api/v1/imports/${draft.id}`, undefined, this.etag),
          );
        } catch (error) {
          if (!(error instanceof HttpErrorResponse && error.status === 410)) throw error;
        }
      }
      this.committed = true;
      this.source.reset();
      await this.router.navigate(['/recipes']);
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
  hasUnsavedChanges(): boolean {
    return (
      !this.committed &&
      (!!this.draft() || !!this.source.controls.text.value || !!this.source.controls.url.value)
    );
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
}
