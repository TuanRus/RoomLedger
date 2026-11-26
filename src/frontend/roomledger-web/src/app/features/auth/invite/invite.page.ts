import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormField, form, required, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';

import { ApiError, injectApi } from '../../../core/api/api-client';
import { TENANT_INVOICE } from '../../../core/data/sample-ledger';
import type { components } from '../../../generated/api-types';
import { Icon } from '../../../shared/ui/icon';
import { Receipt } from '../../../shared/ui/receipt';
import { AuthFrame } from '../shared/auth-frame';
import { isStrongPassword } from '../shared/auth-rules';
import { PasswordRules } from '../shared/password-rules';
import { dmy, previewInvite } from './invite-sample';

type InvitePreviewResponse = components['schemas']['InvitePreviewResponse'];

/** What the invite screen shows. The API only has the first four; sample data fills in the rest. */
export interface InviteView {
  propertyName: string;
  roomCode: string;
  emailMasked: string;
  expiresAt: string;
  email?: string;
  landlordName?: string;
  landlordShortName?: string;
  landlordEmail?: string;
  startDate?: string;
  sentAt?: string;
}

export type InvitePageState =
  | { kind: 'loading' }
  | { kind: 'failed'; message: string }
  | { kind: 'valid'; invite: InviteView }
  | { kind: 'expired'; invite: InviteView }
  | { kind: 'invalid'; message: string | null };

/** InvitePreviewResponse -> the screen model. */
export function toInviteView(r: InvitePreviewResponse): InviteView {
  return { propertyName: r.propertyName ?? '', roomCode: r.roomCode ?? '', emailMasked: r.emailMasked ?? '', expiresAt: r.expiresAt ?? '' };
}

/** ISO date-time -> "25/02/2026, 14:00" in the viewer's own time zone. */
export function expiryLabel(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()}, ${p(d.getHours())}:${p(d.getMinutes())}`;
}

/**
 * TN-01: a tenant opens the landlord's invite link (valid 72 hours) and creates a password.
 * The preview is GET /invites/{token} (anonymous); 404 and 410 both mean the link no longer works (the API does not say
 * whether it expired, was used or was revoked). Landlord name, start date and send time are not in the API, so they
 * show only with sample data. TODO(Kiru, PR #31): Cognito sign-up + POST /invites/{token}/accept; until then accepting is disabled with the API and
 * only simulated with sample data.
 * Sample data: token "expired" shows the expired invite, "invalid" a link that does not exist.
 */
@Component({
  selector: 'app-invite-page',
  imports: [FormField, RouterLink, Icon, Receipt, AuthFrame, PasswordRules],
  templateUrl: './invite.page.html',
  styleUrl: './invite.page.scss',
})
export class InvitePage implements OnInit {
  /** Route parameter :token (component input binding). */
  readonly token = input.required<string>();

  private readonly router = inject(Router);
  private readonly api = injectApi();

  private readonly loaded = signal<InvitePageState>({ kind: 'loading' });
  protected readonly state = computed<InvitePageState>(() => {
    if (this.api) return this.loaded();
    const s = previewInvite(this.token());
    return s.kind === 'invalid' ? { kind: 'invalid', message: null } : s;
  });
  protected readonly sampleInvoice = TENANT_INVOICE;
  /** API: signing up with Cognito and POST /invites/{token}/accept are not wired yet, so accepting is not possible. */
  protected readonly canAccept = !this.api;
  protected readonly cannotAcceptText = 'Chưa nhận lời mời được trên hệ thống: đăng nhập thật đang chờ hoàn thiện.';
  private loading: Subscription | null = null;
  protected readonly dmy = dmy;

  /** "25/02/2026, 14:00" (the expiry is shown in the landlord's local time, +07:00). */
  protected readonly expiresLabel = computed(() => {
    const s = this.state();
    if (s.kind !== 'valid' && s.kind !== 'expired') return '';
    const at = s.invite.expiresAt;
    return this.api ? expiryLabel(at) : `${dmy(at)}, ${at.slice(11, 16)}`;
  });

  protected readonly model = signal({ password: '' });
  protected readonly f = form(this.model, (p) => {
    required(p.password, { message: 'Tạo mật khẩu để nhận lời mời' });
    validate(p.password, ({ value }) => (value() && !isStrongPassword(value()) ? { kind: 'weak', message: 'Mật khẩu chưa đủ các yêu cầu bên dưới' } : undefined));
  });
  protected readonly showPassword = signal(false);
  protected readonly busy = signal(false);

  ngOnInit(): void {
    this.load();
  }

  /** (Re)loads the invite preview; with sample data the state is computed from the token. */
  protected load(): void {
    const api = this.api;
    if (!api) return;
    this.loading?.unsubscribe();
    this.loaded.set({ kind: 'loading' });
    this.loading = api.get<InvitePreviewResponse>(`/invites/${encodeURIComponent(this.token())}`).subscribe({
      next: (r) => this.loaded.set({ kind: 'valid', invite: toInviteView(r) }),
      error: (err: unknown) =>
        this.loaded.set(
          err instanceof ApiError && (err.status === 404 || err.status === 410)
            ? { kind: 'invalid', message: err.message }
            : { kind: 'failed', message: err instanceof Error ? err.message : 'Không mở được lời mời, thử lại sau.' },
        ),
    });
  }

  protected async accept(event: Event): Promise<void> {
    event.preventDefault();
    // Not a fake success: with the API there is nothing to accept with yet (see canAccept).
    if (!this.canAccept) return;
    this.f().markAsTouched();
    if (this.f().invalid()) return;
    this.busy.set(true);
    await new Promise((resolve) => setTimeout(resolve, 500));
    this.busy.set(false);
    await this.router.navigateByUrl('/tenant/home');
  }
}
