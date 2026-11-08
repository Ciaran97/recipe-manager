import { Location } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthSession, apiMessage, invitationToken } from './auth-session';

@Component({
  selector: 'app-auth-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: ` <section class="card auth-card" aria-labelledby="auth-title">
    <h2 id="auth-title">
      {{
        mode === 'setup'
          ? 'Create your household'
          : mode === 'invite'
            ? 'Join your household'
            : 'Welcome back'
      }}
    </h2>
    @if (auth.notice()) {
      <p role="status">{{ auth.notice() }}</p>
    }
    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }
    @if (mode === 'invite' && auth.state.user()) {
      <p>Sign out of your current account before accepting this invitation.</p>
      <button type="button" (click)="signOut()">Sign out all sessions</button>
    } @else {
      <form [formGroup]="form" (ngSubmit)="submit()">
        @if (mode === 'setup') {
          <p>Create the administrator account and the household you will share.</p>
          <label
            >Household name<input
              formControlName="householdName"
              autocomplete="organization"
              maxlength="200"
              required
          /></label>
          <label
            >Household time zone<input formControlName="timezone" list="timezones" required
          /></label>
          <datalist id="timezones">
            @for (zone of timezones; track zone) {
              <option [value]="zone"></option>
            }
          </datalist>
          <label
            >Operator setup secret<input
              formControlName="setupToken"
              type="password"
              autocomplete="off"
              required
          /></label>
        }
        @if (mode === 'invite') {
          <label
            >Invitation link or token<input
              formControlName="token"
              type="password"
              autocomplete="off"
              required
          /></label>
          <label
            >Your name (optional)<input formControlName="name" autocomplete="name" maxlength="200"
          /></label>
        }
        <label
          >Email<input
            formControlName="email"
            type="email"
            autocomplete="username"
            maxlength="254"
            required
        /></label>
        <label
          >Password<input
            formControlName="password"
            type="password"
            [autocomplete]="mode === 'login' ? 'current-password' : 'new-password'"
            minlength="12"
            maxlength="256"
            required
        /></label>
        @if (mode !== 'login') {
          <label
            >Confirm password<input
              formControlName="confirmation"
              type="password"
              autocomplete="new-password"
              minlength="12"
              required
          /></label>
          <p class="hint">
            Use at least 12 characters. Each household member has their own account.
          </p>
        }
        <button class="primary" type="submit" [disabled]="busy()">
          {{
            busy()
              ? 'Please wait…'
              : mode === 'login'
                ? 'Sign in'
                : mode === 'setup'
                  ? 'Create household'
                  : 'Join household'
          }}
        </button>
      </form>
      @if (mode === 'invite') {
        <button
          type="button"
          (click)="openNative()"
          [disabled]="busy() || !form.controls.token.value"
        >
          Copy invitation and open iOS app
        </button>
      }
      @if (mode === 'login' && auth.setupRequired()) {
        <p><a routerLink="/setup">Set up this installation</a></p>
      }
      @if (mode === 'login') {
        <p><a routerLink="/invite">Have an invitation?</a></p>
      }
      @if (mode !== 'login') {
        <p><a routerLink="/login">Already have an account? Sign in</a></p>
      }
    }
  </section>`,
})
export class AuthPage {
  readonly auth = inject(AuthSession);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly fb = inject(FormBuilder);
  readonly mode = this.route.snapshot.data['mode'] as 'login' | 'setup' | 'invite';
  readonly busy = signal(false);
  readonly error = signal('');
  readonly timezones = Intl.supportedValuesOf('timeZone');
  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(256)]],
    confirmation: [''],
    householdName: [''],
    timezone: [Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'],
    setupToken: [''],
    name: [''],
    token: [''],
  });
  constructor() {
    if (this.mode === 'setup')
      for (const name of ['householdName', 'timezone', 'setupToken'] as const)
        this.form.controls[name].addValidators(Validators.required);
    if (this.mode !== 'login') this.form.controls.confirmation.addValidators(Validators.required);
    if (this.mode === 'invite') {
      this.form.controls.token.addValidators(Validators.required);
      this.form.controls.token.setValue(this.route.snapshot.queryParamMap.get('token') ?? '');
      this.location.replaceState('/invite');
    }
    for (const control of Object.values(this.form.controls)) control.updateValueAndValidity();
    void this.auth.ensure().catch((error: unknown) => this.error.set(apiMessage(error)));
  }
  async submit(): Promise<void> {
    this.error.set('');
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.error.set(
        'Complete the required fields with a valid email and a password of at least 12 characters.',
      );
      return;
    }
    const values = this.form.getRawValue();
    if (this.mode !== 'login' && values.password !== values.confirmation) {
      this.error.set('Passwords do not match.');
      return;
    }
    this.busy.set(true);
    this.form.disable({ emitEvent: false });
    try {
      if (this.mode === 'login') await this.auth.login(values.email, values.password);
      else if (this.mode === 'setup')
        await this.auth.setup({
          email: values.email,
          password: values.password,
          householdName: values.householdName,
          timezone: values.timezone,
          setupToken: values.setupToken,
        });
      else
        await this.auth.accept({
          email: values.email,
          password: values.password,
          name: values.name,
          token: invitationToken(values.token, window.location.origin),
        });
      this.form.reset();
      await this.router.navigateByUrl('/recipes', { replaceUrl: true });
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.form.enable({ emitEvent: false });
      this.busy.set(false);
    }
  }
  async openNative(): Promise<void> {
    try {
      const token = invitationToken(this.form.controls.token.value, window.location.origin);
      const link = new URL('/invite', window.location.origin);
      link.searchParams.set('token', token);
      await navigator.clipboard.writeText(link.href);
      // Only the origin enters the custom scheme. Paste remains an explicit action in iOS.
      const target = new URL('recipemanager://invite');
      target.searchParams.set('origin', window.location.origin);
      window.location.assign(target.href);
    } catch {
      this.error.set(
        'Copy the invitation link, open Recipe Manager on iOS, and use Join household → Paste.',
      );
    }
  }
  async signOut(): Promise<void> {
    try {
      await this.auth.logout(false);
    } catch (error) {
      this.error.set(apiMessage(error));
    }
  }
}
