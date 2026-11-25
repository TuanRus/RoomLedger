import { Component, computed, input } from '@angular/core';

import { Icon } from '../../../shared/ui/icon';
import { checkPassword } from './auth-rules';

/**
 * The live password checklist under a "new password" field (LL-09 / TN-14).
 * Rules turn green as they are met; unmet rules turn red only after the field was left or the form submitted.
 */
@Component({
  selector: 'app-password-rules',
  imports: [Icon],
  template: `
    <ul class="rules" [attr.id]="listId()" aria-label="Yêu cầu mật khẩu">
      @for (r of rules(); track r.text) {
        <li [class.ok]="r.met" [class.no]="!r.met && showErrors()">
          <app-icon [name]="r.met ? 'i-check' : showErrors() ? 'i-x' : 'i-check'" size="sm" />
          <span>{{ r.text }}<span class="sr">{{ r.met ? ' · đã đạt' : ' · chưa đạt' }}</span></span>
        </li>
      }
    </ul>
  `,
  host: { style: 'display:block' },
})
export class PasswordRules {
  readonly value = input.required<string>();
  readonly showErrors = input(false);
  readonly listId = input<string>();

  protected readonly rules = computed(() => {
    const c = checkPassword(this.value());
    return [
      { text: 'Ít nhất 8 ký tự', met: c.length },
      { text: 'Có chữ hoa và chữ thường', met: c.mixedCase },
      { text: 'Có ít nhất 1 số', met: c.digit },
    ];
  });
}
