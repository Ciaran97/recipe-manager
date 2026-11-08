import type { CanDeactivateFn } from '@angular/router';
import type { ShoppingListPage } from './shopping-list-page';

export const confirmShoppingDraft: CanDeactivateFn<ShoppingListPage> = (component) =>
  !component.hasUnsavedChanges() ||
  (!component.isBusy() && window.confirm('Discard unsaved shopping changes?'));
