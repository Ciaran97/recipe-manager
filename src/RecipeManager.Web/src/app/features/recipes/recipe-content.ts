import { Component, computed, input } from '@angular/core';
import type { components } from '../../core/api/generated/schema';

type RecipeContent = Pick<
  components['schemas']['RecipeRead'],
  | 'title'
  | 'description'
  | 'imageAssetId'
  | 'defaultServings'
  | 'prepMinutes'
  | 'cookMinutes'
  | 'measurementStandard'
  | 'groups'
  | 'ingredients'
  | 'instructions'
  | 'notes'
  | 'sourceUrl'
>;
@Component({
  selector: 'app-recipe-content',
  template: `<p>
      <strong>{{ recipe().title }}</strong>
    </p>
    @if (recipe().imageAssetId; as image) {
      <img [src]="'/api/v1/images/' + image" [alt]="recipe().title" />
    }
    @if (recipe().description) {
      <p class="preserve">{{ recipe().description }}</p>
    }
    <p>
      Original servings: {{ recipe().defaultServings }}.
      {{ recipe().measurementStandard === 'AU' ? 'Australian measures' : 'US measures' }}.
    </p>
    @if (recipe().prepMinutes !== null) {
      <p>Preparation: {{ recipe().prepMinutes }} minutes.</p>
    }
    @if (recipe().cookMinutes !== null) {
      <p>Cooking: {{ recipe().cookMinutes }} minutes.</p>
    }
    @for (group of groups(); track group.id) {
      @if (group.title) {
        <p>
          <strong>{{ group.title }}</strong>
        </p>
      }
      <ul>
        @for (line of group.lines; track line.id) {
          <li>
            {{ line.quantityText || 'Quantity unspecified' }}
            {{ line.kind === 'numeric' ? line.unit : '' }} {{ line.ingredientName }}
            @if (line.preparation) {
              <span> — {{ line.preparation }}</span>
            }
            @if (line.kind === 'unknown' || line.uncertain) {
              <span class="hint"> (quantity needs review)</span>
            }
          </li>
        }
      </ul>
    }
    <ol>
      @for (step of recipe().instructions; track step.id) {
        <li class="preserve">{{ step.text }}</li>
      }
    </ol>
    @if (recipe().notes) {
      <p class="preserve">{{ recipe().notes }}</p>
    }
    @if (recipe().sourceUrl; as url) {
      <a [href]="url" target="_blank" rel="noopener noreferrer">Original source</a>
    }`,
  styles: `
    :host {
      display: block;
    }
    img {
      max-width: 100%;
      max-height: 18rem;
      object-fit: contain;
      border-radius: 0.6rem;
    }
    .preserve {
      white-space: pre-wrap;
    }
    li {
      margin-bottom: 0.5rem;
    }
  `,
})
export class RecipeContentView {
  readonly recipe = input.required<RecipeContent>();
  readonly groups = computed(() => {
    const recipe = this.recipe();
    const ungrouped = recipe.ingredients.filter((line) => !line.groupId);
    return [
      ...(ungrouped.length ? [{ id: 'ungrouped', title: '', lines: ungrouped }] : []),
      ...recipe.groups.map((group) => ({
        id: group.id,
        title: group.title,
        lines: recipe.ingredients.filter((line) => line.groupId === group.id),
      })),
    ];
  });
}
