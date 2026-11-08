import type { CanDeactivateFn } from '@angular/router';
import type { MealPlanner } from './meal-planner';
export const confirmPlannerDraft: CanDeactivateFn<MealPlanner> = (component) =>
  !component.hasUnsavedChanges() ||
  (!component.isBusy() && window.confirm('Discard unsaved meal changes?'));
