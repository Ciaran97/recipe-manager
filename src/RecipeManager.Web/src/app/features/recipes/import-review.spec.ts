import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ImportReview } from './import-review';

@Component({ template: '' })
class RecordedRecipe {}

describe('Import review entry and recovery', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportReview],
      providers: [
        provideRouter([
          { path: 'recipes/import', component: RecordedRecipe },
          { path: 'recipes/:id', component: RecordedRecipe },
        ]),
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  async function page(enabled = true) {
    const fixture = TestBed.createComponent(ImportReview);
    http.expectOne('/api/v1/household/settings').flush({ ai: { enabled } });
    await fixture.whenStable();
    return fixture;
  }
  it('keeps pasted text and its retry key after provider failure', async () => {
    const fixture = await page();
    const review = fixture.componentInstance;
    review.source.patchValue({ kind: 'text', text: 'Soup\nsalt to taste' });
    const pending = review.start();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/imports/text');
    const key = request.request.headers.get('Idempotency-Key');
    expect(request.request.body.text).toBe('Soup\nsalt to taste');
    request.flush({ code: 'import_provider_failed' }, { status: 502, statusText: 'Bad Gateway' });
    await pending;
    expect(review.source.controls.text.value).toBe('Soup\nsalt to taste');
    expect(review.error()).toContain('provider');
    const again = review.start();
    const retried = http.expectOne('/api/v1/imports/text');
    expect(retried.request.headers.get('Idempotency-Key')).toBe(key);
    retried.flush({ code: 'import_timeout' }, { status: 504, statusText: 'Gateway Timeout' });
    await again;
  });
  it('keeps URL importing available with AI disabled and saves no recipe during extraction', async () => {
    const fixture = await page(false);
    const review = fixture.componentInstance;
    review.source.controls.url.setValue('https://recipes.example/soup');
    const pending = review.start();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    http.expectOne('/api/v1/imports/url').flush(
      {
        draft: {
          id: 'draft',
          revision: '1',
          handoffId: null,
          sourceKind: 'Url',
          originalInput: 'https://recipes.example/soup',
          uncertaintyFields: ['measurementStandard'],
          measurementStandard: null,
          expiresAt: '2026-11-04T00:00:00Z',
          extraction: {
            recipes: [
              {
                title: 'Soup',
                description: null,
                defaultServings: null,
                prepMinutes: null,
                cookMinutes: null,
                ingredients: [],
                instructions: [],
                notes: null,
                sourceUrl: 'https://recipes.example/soup',
                imageUrl: null,
                measurementStandard: null,
                suggestedTags: [],
                uncertaintyFields: ['measurementStandard'],
              },
            ],
          },
          reviewedRecipe: null,
        },
        commitResult: null,
      },
      { status: 201, statusText: 'Created', headers: { ETag: '"draft:1"' } },
    );
    await pending;
    expect(review.draft()?.id).toBe('draft');
    expect(review.seed()?.candidate?.title).toBe('Soup');
    http.expectNone('/api/v1/recipes');
  });
  it('cancels extraction without clearing the original input or showing a stale failure', async () => {
    const fixture = await page();
    const review = fixture.componentInstance;
    review.source.controls.url.setValue('https://recipes.example/soup');
    const pending = review.start();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const request = http.expectOne('/api/v1/imports/url');
    review.cancelExtraction();
    await pending;
    expect(request.cancelled).toBe(true);
    expect(review.source.controls.url.value).toContain('/soup');
    expect(review.error()).toBe('');
  });
});
