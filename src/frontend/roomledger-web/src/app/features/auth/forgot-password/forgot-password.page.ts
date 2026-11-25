import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormField, form, required, validate } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';

import { Icon } from '../../../shared/ui/icon';
import { AuthFrame } from '../shared/auth-frame';
import { isEmail, isStrongPassword, maskEmail } from '../shared/auth-rules';
import { PasswordRules } from '../shared/password-rules';

type Step = 'email' | 'code' | 'done';

export const RESEND_SECONDS = 60;

/**
 * LL-09 (computer) / TN-13 + TN-14 (phone): reset the password with an emailed 6-digit code.
 * UI only. TODO(Kiru): step 1 calls Cognito forgotPassword, step 2 confirmForgotPassword.
 * Sample states: code "000000" is rejected as wrong or expired; any other 6 digits succeed.
 */
@Component({
  selector: 'app-forgot-password-page',
  imports: [FormField, RouterLink, Icon, AuthFrame, PasswordRules],
  templateUrl: './forgot-password.page.html',
  styleUrl: './forgot-password.page.scss',
})
export class ForgotPasswordPage {
  protected readonly step = signal<Step>('email');
  protected readonly busy = signal(false);

  protected readonly emailModel = signal({ email: '' });
  protected readonly emailForm = form(this.emailModel, (p) => {
    required(p.email, { message: 'Nhập email của bạn' });
    validate(p.email, ({ value }) => (value() && !isEmail(value()) ? { kind: 'email', message: 'Email chưa đúng dạng, ví dụ ten@vidu.vn' } : undefined));
  });
  protected readonly maskedEmail = computed(() => maskEmail(this.emailModel().email));

  protected readonly resetModel = signal({ code: '', password: '', confirm: '' });
  protected readonly resetForm = form(this.resetModel, (p) => {
    validate(p.code, ({ value }) => (/^\d{6}$/.test(value()) ? undefined : { kind: 'code', message: 'Nhập đủ 6 số trong email' }));
    required(p.password, { message: 'Tạo mật khẩu mới' });
    validate(p.password, ({ value }) => (value() && !isStrongPassword(value()) ? { kind: 'weak', message: 'Mật khẩu chưa đủ các yêu cầu bên dưới' } : undefined));
    required(p.confirm, { message: 'Nhập lại mật khẩu mới' });
    validate(p.confirm, ({ value, valueOf }) => (value() && value() !== valueOf(p.password) ? { kind: 'mismatch', message: 'Hai mật khẩu chưa giống nhau' } : undefined));
  });

  /** The six boxes drawn over the single code input. */
  protected readonly codeBoxes = computed(() => {
    const code = this.resetModel().code;
    return Array.from({ length: 6 }, (_, i) => code[i] ?? '');
  });
  protected readonly codeFocused = signal(false);
  protected readonly codeError = signal<string | null>(null);

  protected readonly resendIn = signal(0);
  protected readonly resendLabel = computed(() => `${Math.floor(this.resendIn() / 60)}:${String(this.resendIn() % 60).padStart(2, '0')}`);
  protected readonly resent = signal(false);
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopTimer());
  }

  protected async sendCode(event: Event): Promise<void> {
    event.preventDefault();
    this.emailForm().markAsTouched();
    if (this.emailForm().invalid()) return;
    await this.fakeRequest();
    // Always moves on, even when the email has no account, so the screen does not reveal who has one.
    this.step.set('code');
    this.startCountdown();
  }

  protected async resend(): Promise<void> {
    if (this.resendIn() > 0) return;
    this.resent.set(true);
    this.startCountdown();
  }

  /** Keeps only digits (pasted "482 917" becomes "482917"). */
  protected onCodeInput(event: Event): void {
    this.codeError.set(null);
    const el = event.target as HTMLInputElement;
    const digits = el.value.replace(/\D/g, '').slice(0, 6);
    if (digits !== el.value) el.value = digits;
    this.resetModel.update((m) => ({ ...m, code: digits }));
  }

  protected async resetPassword(event: Event): Promise<void> {
    event.preventDefault();
    this.codeError.set(null);
    this.resetForm().markAsTouched();
    if (this.resetForm().invalid()) return;
    await this.fakeRequest();
    if (this.resetModel().code === '000000') {
      this.codeError.set('Mã chưa đúng hoặc đã hết hạn. Kiểm tra email mới nhất, hoặc gửi lại mã.');
      return;
    }
    this.stopTimer();
    this.step.set('done');
  }

  protected changeEmail(): void {
    this.stopTimer();
    this.resetModel.set({ code: '', password: '', confirm: '' });
    this.resetForm().reset();
    this.codeError.set(null);
    this.resent.set(false);
    this.step.set('email');
  }

  private async fakeRequest(): Promise<void> {
    this.busy.set(true);
    await new Promise((resolve) => setTimeout(resolve, 400));
    this.busy.set(false);
  }

  private startCountdown(): void {
    this.stopTimer();
    this.resendIn.set(RESEND_SECONDS);
    this.timer = setInterval(() => {
      this.resendIn.update((s) => s - 1);
      if (this.resendIn() <= 0) this.stopTimer();
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }
}
