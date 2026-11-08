import { Injectable, effect, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { CanActivateFn, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { SessionState, SessionUser } from '../../core/session/session-state';
import type { components } from '../../core/api/generated/schema';

@Injectable({ providedIn: 'root' })
export class AuthSession {
  private readonly api = inject(ApiClient);
  readonly state = inject(SessionState);
  private readonly router = inject(Router);
  readonly setupRequired = signal<boolean | null>(null);
  readonly notice = signal('');
  private initialized = false;
  private pending?: Promise<SessionUser | null>;
  private hadSession = false;

  constructor() {
    effect(() => {
      if (this.api.authenticationRequired() && this.hadSession) {
        this.hadSession = false;
        this.state.clear();
        this.notice.set('Your session ended. Sign in again.');
        void this.router.navigateByUrl('/login', { replaceUrl: true });
      }
    });
  }

  ensure(): Promise<SessionUser | null> {
    if (this.initialized) return Promise.resolve(this.state.user());
    return (this.pending ??= this.restore().finally(() => {
      this.pending = undefined;
    }));
  }
  private async restore(): Promise<SessionUser | null> {
    const setup = (
      await firstValueFrom(this.api.read<components['schemas']['SetupStatus']>('/api/v1/setup'))
    ).body;
    this.setupRequired.set(setup?.setupRequired ?? false);
    if (setup?.setupRequired) {
      this.initialized = true;
      return null;
    }
    try {
      const user = (await firstValueFrom(this.api.read<SessionUser>('/api/v1/auth/me'))).body;
      if (user) this.established(user);
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 401)) throw error;
    }
    this.initialized = true;
    return this.state.user();
  }

  async login(email: string, password: string): Promise<void> {
    const user = (
      await firstValueFrom(
        this.api.write<SessionUser>('POST', '/api/v1/auth/login', { email, password }),
      )
    ).body;
    if (user) this.established(user);
  }
  async setup(body: components['schemas']['SetupRequest']): Promise<void> {
    const user = (await firstValueFrom(this.api.write<SessionUser>('POST', '/api/v1/setup', body)))
      .body;
    if (user) this.established(user);
  }
  async accept(body: components['schemas']['AcceptInvitationRequest']): Promise<void> {
    const user = (
      await firstValueFrom(this.api.write<SessionUser>('POST', '/api/v1/invitations/accept', body))
    ).body;
    if (user) this.established(user);
  }
  private established(user: SessionUser): void {
    this.api.sessionEstablished();
    this.state.user.set(user);
    this.setupRequired.set(false);
    this.hadSession = true;
    this.initialized = true;
    this.notice.set('');
  }
  async logout(navigate = true): Promise<void> {
    await firstValueFrom(this.api.write('POST', '/api/v1/auth/logout'));
    await this.endSession('Signed out of all sessions.', navigate);
  }
  async endSession(message: string, navigate = true): Promise<void> {
    this.hadSession = false;
    this.state.clear();
    this.api.resetSession();
    this.initialized = true;
    this.notice.set(message);
    if (navigate) await this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}

export const memberGuard: CanActivateFn = async () => {
  const auth = inject(AuthSession);
  const router = inject(Router);
  try {
    if (await auth.ensure()) return true;
  } catch {
    auth.notice.set('Could not reach your server. Check the connection and try again.');
  }
  return router.createUrlTree([auth.setupRequired() ? '/setup' : '/login']);
};

export function invitationToken(input: string, origin: string): string {
  const trimmed = input.trim();
  if (trimmed.includes('?') || trimmed.includes('://')) {
    const url = new URL(trimmed, origin);
    if (url.origin !== origin)
      throw new Error('This invitation belongs to another server. Open its original link.');
    const token = url.searchParams.get('token');
    if (!token) throw new Error('The invitation link has no token.');
    return token;
  }
  return trimmed;
}

export function apiMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const details = error.error as { code?: string; errors?: { message: string }[] } | null;
    if (details?.errors?.length) return details.errors.map((entry) => entry.message).join(' ');
    const messages: Record<string, string> = {
      import_provider_failed:
        'The AI provider could not process this recipe. Your input is retained; try again or enter it manually.',
      import_provider_invalid_response:
        'The AI provider returned uncertain or invalid source data. Your input is retained; correct it manually or try again.',
      import_ai_disabled:
        'AI text importing is not configured. URL and manual recipe entry remain available.',
      import_provider_configuration:
        'Ask an administrator to check the household AI provider configuration.',
      import_timeout:
        'Recipe processing timed out. Your input is retained; retry or enter it manually.',
      import_unsafe_url: 'Choose a public HTTP or HTTPS recipe URL without embedded credentials.',
      import_fetch_failed:
        'The recipe website could not be reached. Your input is retained; retry or enter it manually.',
      import_no_recipe:
        'No structured recipe was found on this page. Try another recipe URL or enter it manually.',
      import_unsupported_content: 'This URL did not return a supported recipe page.',
      import_invalid_document:
        'The recipe page could not be read. Try another URL or enter it manually.',
      import_limit:
        'This input or response is too large to import. Use an individual recipe page or shorter text.',
      draft_expired:
        'This review draft expired or is unavailable. Keep your original input and start a new explicit import.',
      handoff_expired: 'This shared recipe capture expired. Share it again to start a new import.',
      handoff_already_committed:
        'This shared recipe was already saved. Check the original import result.',
      handoff_input_changed:
        'This shared capture is bound to its original input. Keep its original content.',
      committed_recipe_deleted:
        'The recipe saved by this import was deleted. This import will not recreate it automatically.',
      invalid_credentials: 'Email or password was not accepted.',
      setup_already_completed: 'Setup is already complete. Sign in with your account.',
      last_administrator: 'The household must retain an administrator.',
      sign_out_before_accepting: 'Sign out of your current account before using this invitation.',
      invitation_already_used: 'This invitation has already been used.',
      shopping_review_required:
        'Requirements changed. Preview them again before applying; your edits are retained.',
      stale_revision: 'This changed on another device. Reload before saving.',
      leftovers_overallocated:
        'These portions exceed the available leftovers. Review the related meals.',
      invalid_leftover_chronology: 'Schedule leftover meals after their source cooking event.',
      source_not_confirmed:
        'Confirm source cooking quantities before recording leftover consumption.',
      dependent_leftovers:
        'This cooking event has dependent leftover meals. Resolve them before removing it.',
      slot_archived: 'Choose an active meal slot for a new meal.',
      consumption_removal_confirmation:
        'Explicitly confirm removing recorded consumption before deleting this meal.',
      different_meal_type: 'Reload this meal before using a different type of editor.',
      name_conflict: 'This name is already in use. Choose another name.',
      ingredient_referenced:
        'This ingredient is used by a recipe, meal or shopping list. Correct its name or aliases instead of deleting it.',
      recipe_referenced:
        'This recipe is used by a meal or shopping list. Archive it instead of deleting it.',
      image_referenced: 'This image is still used by a recipe or cooking snapshot.',
      idempotency_payload_changed:
        'The original save may already have succeeded. Reload and check it before starting a changed save.',
      rate_limited: 'Too many attempts. Wait a minute and try again.',
      session_drain_pending: 'A connection is still closing. Try again shortly.',
    };
    return (
      messages[details?.code ?? ''] ??
      (error.status === 0
        ? 'Could not reach the server. Your change was not confirmed.'
        : 'Could not complete this request. Reload and try again.')
    );
  }
  return error instanceof Error ? error.message : 'Could not complete this request.';
}
