import { Routes } from '@angular/router';

export const authRoutes: Routes = [
  {
    path: 'login',
    data: { mode: 'login' },
    loadComponent: () => import('./auth-page').then((m) => m.AuthPage),
  },
  {
    path: 'setup',
    data: { mode: 'setup' },
    loadComponent: () => import('./auth-page').then((m) => m.AuthPage),
  },
  {
    path: 'invite',
    data: { mode: 'invite' },
    loadComponent: () => import('./auth-page').then((m) => m.AuthPage),
  },
];
