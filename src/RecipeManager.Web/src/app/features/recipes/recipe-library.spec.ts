import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withNoXsrfProtection } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { RecipeLibrary } from './recipe-library';
import type { components } from '../../core/api/generated/schema';

const recipe = (id: string): components['schemas']['RecipeRead'] => ({
  id,
  revision: '1',
  title: id,
  description: null,
  imageAssetId: null,
  prepMinutes: null,
  cookMinutes: null,
  notes: null,
  sourceUrl: null,
  defaultServings: '2',
  measurementStandard: 'AU',
  policyVersion: 1,
  archived: false,
  favourite: false,
  groups: [],
  ingredients: [],
  instructions: [],
  collectionIds: [],
  tagIds: [],
});

describe('Recipe navigation and asynchronous results', () => {
  let http: HttpTestingController;
  let parameters: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    parameters = new BehaviorSubject(convertToParamMap({ id: 'first' }));
    TestBed.configureTestingModule({
      imports: [RecipeLibrary],
      providers: [
        provideRouter([]),
        provideHttpClient(withNoXsrfProtection()),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: parameters } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('does not attach an earlier recipe serving preview after navigating to another recipe', async () => {
    const fixture = TestBed.createComponent(RecipeLibrary);
    http.expectOne('/api/v1/recipes/first').flush(recipe('first'));
    await fixture.whenStable();
    const preview = fixture.componentInstance.scaleServings();
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf' });
    const pending = http.expectOne('/api/v1/recipes/first/serving-preview');
    parameters.next(convertToParamMap({ id: 'second' }));
    http.expectOne('/api/v1/recipes/second').flush(recipe('second'));
    await Promise.resolve();
    pending.flush({
      recipeId: 'first',
      recipeRevision: '1',
      defaultServings: '2',
      targetServings: '4',
      ingredients: [],
    });
    await preview;
    expect(fixture.componentInstance.selected()?.id).toBe('second');
    expect(fixture.componentInstance.preview()).toBeNull();
  });
});
