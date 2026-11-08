import { authRoutes } from './features/auth/auth.routes';
import { memberGuard } from './features/auth/auth-session';
import { Routes } from '@angular/router';
import { confirmRecipeDraft, confirmImportDraft } from './features/recipes/recipe-guards';
import { confirmPlannerDraft } from './features/planner/planner-guards';
import { confirmShoppingDraft } from './features/shopping/shopping-guards';

export const routes: Routes = [
  ...authRoutes,
  { path: '', pathMatch: 'full', redirectTo: 'recipes' },
  {
    path: 'recipes',
    canActivate: [memberGuard],
    loadComponent: () => import('./features/recipes/recipe-library').then((m) => m.RecipeLibrary),
  },
  {
    path: 'recipes/new',
    canActivate: [memberGuard],
    canDeactivate: [confirmRecipeDraft],
    loadComponent: () => import('./features/recipes/recipe-editor').then((m) => m.RecipeEditor),
  },
  {
    path: 'recipes/import',
    canActivate: [memberGuard],
    canDeactivate: [confirmImportDraft],
    loadComponent: () => import('./features/recipes/import-review').then((m) => m.ImportReview),
  },
  {
    path: 'recipes/organisers',
    canActivate: [memberGuard],
    canDeactivate: [
      (component: { hasUnsavedChanges(): boolean; busy(): boolean }) =>
        !component.hasUnsavedChanges() ||
        (!component.busy() && window.confirm('Discard unsaved organiser changes?')),
    ],
    loadComponent: () => import('./features/recipes/organisers-page').then((m) => m.OrganisersPage),
  },
  {
    path: 'recipes/:id/edit',
    canActivate: [memberGuard],
    canDeactivate: [confirmRecipeDraft],
    loadComponent: () => import('./features/recipes/recipe-editor').then((m) => m.RecipeEditor),
  },
  {
    path: 'recipes/:id',
    canActivate: [memberGuard],
    loadComponent: () => import('./features/recipes/recipe-library').then((m) => m.RecipeLibrary),
  },
  {
    path: 'ingredients',
    canActivate: [memberGuard],
    loadComponent: () =>
      import('./features/ingredients/catalogue-page').then((m) => m.CataloguePage),
  },
  {
    path: 'meal-planner',
    canActivate: [memberGuard],
    canDeactivate: [confirmPlannerDraft],
    loadComponent: () => import('./features/planner/meal-planner').then((m) => m.MealPlanner),
  },
  {
    path: 'shopping-lists',
    canActivate: [memberGuard],
    canDeactivate: [confirmShoppingDraft],
    loadComponent: () =>
      import('./features/shopping/shopping-list-page').then((m) => m.ShoppingListPage),
  },
  {
    path: 'settings',
    canActivate: [memberGuard],
    loadComponent: () => import('./features/settings/settings-page').then((m) => m.SettingsPage),
  },
  { path: '**', redirectTo: 'recipes' },
];
