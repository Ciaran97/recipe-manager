import { Component, inject } from '@angular/core';
import { HouseholdSync } from './core/sync/household-sync';
import { AuthSession } from './features/auth/auth-session';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly auth = inject(AuthSession);
  private readonly sync = inject(HouseholdSync);
}
