import { NgOptimizedImage } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormField, form, required, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';

import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { AuthFrame } from '../shared/auth-frame';
import { isEmail, stubRoleFor } from '../shared/auth-rules';

/**
 * LL-01: sign-in for landlords and tenants (one Cognito users pool, told apart by role).
 * UI only: submit is a TEMPORARY stub (see stubRoleFor). TODO(Kiru): call the Cognito sign-in here.
 */
@Component({
  selector: 'app-login-page',
  imports: [NgOptimizedImage, FormField, RouterLink, Icon, MeterDigits, AuthFrame],
  templateUrl: './login.page.html',
  styleUrl: './login.page.scss',
})
export class LoginPage {
  private readonly router = inject(Router);

  protected readonly model = signal({ email: '', password: '' });
  protected readonly f = form(this.model, (p) => {
    required(p.email, { message: 'Nhập email của bạn' });
    validate(p.email, ({ value }) => (value() && !isEmail(value()) ? { kind: 'email', message: 'Email chưa đúng dạng, ví dụ ten@vidu.vn' } : undefined));
    required(p.password, { message: 'Nhập mật khẩu' });
  });

  protected readonly showPassword = signal(false);
  protected readonly busy = signal(false);
  /** Error from the sign-in service (wrong email or password). */
  protected readonly authError = signal<string | null>(null);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.authError.set(null);
    this.f().markAsTouched();
    if (this.f().invalid()) return;

    this.busy.set(true);
    // TEMPORARY stub: simulate the network round trip. A password containing "sai" shows the wrong-credentials state.
    await new Promise((resolve) => setTimeout(resolve, 500));
    this.busy.set(false);
    const { email, password } = this.model();
    if (password.toLowerCase().includes('sai')) {
      this.authError.set('Email hoặc mật khẩu chưa đúng. Kiểm tra lại, hoặc chọn “Quên mật khẩu?”.');
      return;
    }
    await this.router.navigateByUrl(stubRoleFor(email) === 'landlord' ? '/landlord' : '/tenant');
  }
}
