import type { CanDeactivateFn } from '@angular/router';
import type { ImportReview } from './import-review';
import type { RecipeEditor } from './recipe-editor';

export const confirmRecipeDraft: CanDeactivateFn<RecipeEditor> = (component) =>
  !component.hasUnsavedChanges() ||
  (!component.busy() && window.confirm('Discard unsaved recipe changes?'));

export const confirmImportDraft: CanDeactivateFn<ImportReview> = (component) =>
  !component.hasUnsavedChanges() ||
  (!component.isBusy() && window.confirm('Leave this import and its unsaved input or review?'));
