import { Component, computed, inject, input, signal } from '@angular/core';
import { FormField, form, required, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';

import { TENANT_INVOICE } from '../../../core/data/sample-ledger';
import { Icon } from '../../../shared/ui/icon';
import { Receipt } from '../../../shared/ui/receipt';
import { AuthFrame } from '../shared/auth-frame';
import { isStrongPassword } from '../shared/auth-rules';
import { PasswordRules } from '../shared/password-rules';
import { InviteState, dmy, previewInvite } from './invite-sample';

/**
 * TN-01: a tenant opens the landlord's invite link (valid 72 hours) and creates a password.
 * UI only. TODO(Kiru): GET /invites/{token} for the preview, then Cognito sign-up + POST /invites/{token}/accept.
 * Sample states: token "expired" shows the expired invite, "invalid" a link that does not exist.
 */
@Component({
  selector: 'app-invite-page',
  imports: [FormField, RouterLink, Icon, Receipt, AuthFrame, PasswordRules],
  templateUrl: './invite.page.html',
  styleUrl: './invite.page.scss',
})
export class InvitePage {
  /** Route parameter :token (component input binding). */
  readonly token = input.required<string>();

  private readonly router = inject(Router);

  protected readonly state = computed<InviteState>(() => previewInvite(this.token()));
  protected readonly sampleInvoice = TENANT_INVOICE;
  protected readonly dmy = dmy;

  /** "25/02/2026, 14:00" (the expiry is shown in the landlord's local time, +07:00). */
  protected readonly expiresLabel = computed(() => {
    const s = this.state();
    if (s.kind === 'invalid') return '';
    const at = s.invite.expiresAt;
    return `${dmy(at)}, ${at.slice(11, 16)}`;
  });

  protected readonly model = signal({ password: '' });
  protected readonly f = form(this.model, (p) => {
    required(p.password, { message: 'Tạo mật khẩu để nhận lời mời' });
    validate(p.password, ({ value }) => (value() && !isStrongPassword(value()) ? { kind: 'weak', message: 'Mật khẩu chưa đủ các yêu cầu bên dưới' } : undefined));
  });
  protected readonly showPassword = signal(false);
  protected readonly busy = signal(false);

  protected async accept(event: Event): Promise<void> {
    event.preventDefault();
    this.f().markAsTouched();
    if (this.f().invalid()) return;
    this.busy.set(true);
    await new Promise((resolve) => setTimeout(resolve, 500));
    this.busy.set(false);
    await this.router.navigateByUrl('/tenant/home');
  }
}
