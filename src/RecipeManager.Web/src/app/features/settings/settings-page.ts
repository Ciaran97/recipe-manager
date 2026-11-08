import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { HouseholdSync } from '../../core/sync/household-sync';
import { ApiClient } from '../../core/api/api-client';
import type { components } from '../../core/api/generated/schema';
import { AuthSession, apiMessage } from '../auth/auth-session';

type Profile = components['schemas']['ProfileRead'];
type Settings = components['schemas']['HouseholdSettingsRead'];
type Member = components['schemas']['MemberRead'];
type Invitation = components['schemas']['InvitationRead'];

@Component({
  selector: 'app-settings-page',
  imports: [ReactiveFormsModule, DatePipe, RouterLink],
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  readonly auth = inject(AuthSession);
  private readonly api = inject(ApiClient);
  private readonly fb = inject(FormBuilder);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly remoteChanged = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly profile = signal<Profile | null>(null);
  readonly settings = signal<Settings | null>(null);
  readonly members = signal<Member[]>([]);
  readonly sortedMembers = computed(() =>
    [...this.members()].sort((a, b) => (a.name || a.email).localeCompare(b.name || b.email)),
  );
  readonly invitations = signal<Invitation[]>([]);
  readonly createdLink = signal('');
  readonly memberCursor = signal<string | null>(null);
  readonly invitationCursor = signal<string | null>(null);
  private profileEtag = '';
  private settingsEtag = '';
  private invitationKey?: string;
  readonly profileForm = this.fb.nonNullable.group({ name: ['', Validators.maxLength(200)] });
  readonly passwordForm = this.fb.nonNullable.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(256)]],
    confirmation: ['', Validators.required],
  });
  readonly householdForm = this.fb.nonNullable.group({
    name: ['', Validators.required],
    timezone: ['', Validators.required],
    enabled: [false],
    model: [''],
    baseUrl: [''],
    apiKey: [''],
    clearApiKey: [false],
  });

  constructor() {
    const sync = inject(HouseholdSync);
    effect(() => {
      const change = sync.change();
      if (
        !change ||
        !(
          change.personal ||
          change.domains.includes('settingsRevision') ||
          change.domains.includes('membershipRevision')
        )
      )
        return;
      untracked(() => {
        if (
          this.loading() ||
          this.busy() ||
          this.profileForm.dirty ||
          this.householdForm.dirty ||
          this.passwordForm.dirty
        )
          this.remoteChanged.set(true);
        else void this.reload();
      });
    });
    void this.reload();
  }

  async reload(): Promise<void> {
    this.remoteChanged.set(false);
    this.loading.set(true);
    this.error.set('');
    try {
      const [profile, settings] = await Promise.all([
        firstValueFrom(this.api.read<Profile>('/api/v1/profile')),
        firstValueFrom(this.api.read<Settings>('/api/v1/household/settings')),
      ]);
      if (profile.body) {
        this.profile.set(profile.body);
        this.profileEtag = profile.headers.get('ETag') ?? '';
        this.profileForm.reset({ name: profile.body.name });
      }
      if (settings.body) {
        this.settings.set(settings.body);
        this.settingsEtag = settings.headers.get('ETag') ?? '';
        this.householdForm.reset({
          name: settings.body.name,
          timezone: settings.body.timezone,
          enabled: settings.body.ai.enabled,
          model: settings.body.ai.model,
          baseUrl: settings.body.ai.baseUrl,
          apiKey: '',
          clearApiKey: false,
        });
      }
      await this.loadMembers(false);
      if (this.auth.state.canManageHousehold()) await this.loadInvitations(false);
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.loading.set(false);
    }
  }
  async saveProfile(): Promise<void> {
    const current = this.profile();
    if (!current || this.profileForm.invalid) return;
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.write<Profile>(
          'PUT',
          '/api/v1/profile',
          { name: this.profileForm.getRawValue().name, preferences: current.preferences },
          this.profileEtag,
        ),
      );
      if (response.body) {
        this.profile.set(response.body);
        this.profileEtag = response.headers.get('ETag') ?? '';
        this.profileForm.markAsPristine();
      }
      this.notice.set('Profile saved.');
    });
  }
  async saveHousehold(): Promise<void> {
    if (!this.auth.state.canManageHousehold()) return;
    this.householdForm.markAllAsTouched();
    if (this.householdForm.invalid) {
      this.error.set('Complete the household name and time zone.');
      return;
    }
    const form = this.householdForm.getRawValue();
    if (form.clearApiKey && form.apiKey) {
      this.error.set('Choose either replacing or clearing the API key.');
      return;
    }
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.write<Settings>(
          'PUT',
          '/api/v1/household/settings',
          {
            name: form.name,
            timezone: form.timezone,
            ai: {
              enabled: form.enabled,
              provider: 'OpenAICompatible',
              model: form.model,
              baseUrl: form.baseUrl,
              ...(form.apiKey.length ? { apiKey: form.apiKey } : {}),
              clearApiKey: form.clearApiKey,
            },
          },
          this.settingsEtag,
        ),
      );
      if (response.body) {
        this.settings.set(response.body);
        this.settingsEtag = response.headers.get('ETag') ?? '';
      }
      this.householdForm.controls.apiKey.setValue('');
      this.householdForm.controls.clearApiKey.setValue(false);
      this.householdForm.markAsPristine();
      this.notice.set('Household settings saved.');
    });
  }
  async changePassword(): Promise<void> {
    this.passwordForm.markAllAsTouched();
    const form = this.passwordForm.getRawValue();
    if (this.passwordForm.invalid || form.newPassword !== form.confirmation) {
      this.error.set(
        'Enter your current password and matching new passwords of at least 12 characters.',
      );
      return;
    }
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write(
          'POST',
          '/api/v1/profile/password',
          { currentPassword: form.currentPassword, newPassword: form.newPassword },
          this.profileEtag,
        ),
      );
      this.passwordForm.reset();
      await this.auth.endSession('Password changed. Sign in with your new password.');
    });
  }
  async loadMembers(append: boolean): Promise<void> {
    const cursor = append ? this.memberCursor() : null;
    try {
      const response = await firstValueFrom(
        this.api.read<components['schemas']['MemberPage']>(
          `/api/v1/household/members${cursor ? '?cursor=' + encodeURIComponent(cursor) : ''}`,
        ),
      );
      if (response.body) {
        this.members.set(
          append ? [...this.members(), ...response.body.items] : response.body.items,
        );
        this.memberCursor.set(response.body.nextCursor ?? null);
      }
    } catch (error) {
      this.error.set(apiMessage(error));
    }
  }
  async loadInvitations(append: boolean): Promise<void> {
    if (!this.auth.state.canManageHousehold()) return;
    const cursor = append ? this.invitationCursor() : null;
    try {
      const response = await firstValueFrom(
        this.api.read<components['schemas']['InvitationPage']>(
          `/api/v1/household/invitations${cursor ? '?cursor=' + encodeURIComponent(cursor) : ''}`,
        ),
      );
      if (response.body) {
        this.invitations.set(
          append ? [...this.invitations(), ...response.body.items] : response.body.items,
        );
        this.invitationCursor.set(response.body.nextCursor ?? null);
      }
    } catch (error) {
      this.error.set(apiMessage(error));
    }
  }
  async createInvitation(): Promise<void> {
    this.invitationKey ??= crypto.randomUUID();
    await this.perform(async () => {
      const response = await firstValueFrom(
        this.api.write<components['schemas']['InvitationCreated']>(
          'POST',
          '/api/v1/household/invitations',
          {},
          undefined,
          this.invitationKey,
        ),
      );
      if (response.body)
        this.createdLink.set(new URL(response.body.link, window.location.origin).href);
      this.invitationKey = undefined;
      await this.loadInvitations(false);
      this.notice.set(
        'Invitation created. Copy the link; it is available here only until you leave this page.',
      );
    });
  }
  async copyInvitation(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.createdLink());
      this.notice.set('Invitation link copied.');
    } catch {
      this.notice.set('Select and copy the invitation link below.');
    }
  }
  async revoke(invitation: Invitation): Promise<void> {
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write(
          'DELETE',
          `/api/v1/household/invitations/${invitation.id}`,
          undefined,
          invitation.etag,
        ),
      );
      await this.loadInvitations(false);
      this.createdLink.set('');
      this.notice.set('Invitation revoked.');
    });
  }
  async remove(member: Member): Promise<void> {
    await this.perform(async () => {
      await firstValueFrom(
        this.api.write('DELETE', `/api/v1/household/members/${member.id}`, undefined, member.etag),
      );
      await this.loadMembers(false);
      this.notice.set('Member access revoked. Shared household data is retained.');
    });
  }
  async logout(): Promise<void> {
    await this.perform(() => this.auth.logout());
  }
  private async perform(operation: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.profileForm.disable({ emitEvent: false });
    this.passwordForm.disable({ emitEvent: false });
    this.householdForm.disable({ emitEvent: false });
    this.error.set('');
    this.notice.set('');
    try {
      await operation();
    } catch (error) {
      this.error.set(apiMessage(error));
    } finally {
      this.profileForm.enable({ emitEvent: false });
      this.passwordForm.enable({ emitEvent: false });
      this.householdForm.enable({ emitEvent: false });
      this.busy.set(false);
    }
  }
}
