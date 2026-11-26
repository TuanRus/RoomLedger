import { DOCUMENT } from '@angular/common';
import { Component, afterNextRender, inject, signal } from '@angular/core';
import { FormField, form, required, validate } from '@angular/forms/signals';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { environment } from '../../../../environments/environment';
import { Icon } from '../../../shared/ui/icon';
import { isEmail } from '../../auth/shared/auth-rules';
import { HowStage } from './how-stage';

/**
 * LP-01: the public page that explains RoomLedger and starts a landlord sign-up.
 * UI only: the sign-up form shows a "sent" state. TODO(Kiru): start the Cognito landlord sign-up here.
 */
@Component({
  selector: 'app-landing-page',
  imports: [FormField, RouterLink, Icon, HowStage],
  templateUrl: './landing.page.html',
  styleUrl: './landing.page.scss',
})
export class LandingPage {
  /** The /preview route only exists with sample data or in LocalDev (see app.routes.ts). */
  protected readonly showPreview = environment.localDev || environment.dataSource === 'sample';

  private readonly document = inject(DOCUMENT);

  protected readonly model = signal({ email: '' });
  protected readonly f = form(this.model, (p) => {
    required(p.email, { message: 'Nhập email để tạo tài khoản' });
    validate(p.email, ({ value }) => (value() && !isEmail(value()) ? { kind: 'email', message: 'Email chưa đúng dạng, ví dụ ten@vidu.vn' } : undefined));
  });
  protected readonly sentTo = signal<string | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    // /#dang-ky (e.g. from the sign-in page): the router does not scroll to fragments, so do it here.
    const fragment = inject(ActivatedRoute).snapshot.fragment;
    afterNextRender(() => {
      if (fragment) this.scrollTo(fragment, fragment === 'dang-ky');
    });
  }

  protected scrollTo(id: string, focusSignup = false, event?: Event): void {
    event?.preventDefault();
    this.document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    if (focusSignup) this.document.getElementById('signup-email')?.focus({ preventScroll: true });
  }

  protected async signup(event: Event): Promise<void> {
    event.preventDefault();
    this.f().markAsTouched();
    if (this.f().invalid()) return;
    this.busy.set(true);
    await new Promise((resolve) => setTimeout(resolve, 400));
    this.busy.set(false);
    this.sentTo.set(this.model().email.trim());
  }

  protected restart(): void {
    this.sentTo.set(null);
    this.model.set({ email: '' });
    this.f().reset();
  }
}
