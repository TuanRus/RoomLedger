import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Icon } from '../../../shared/ui/icon';

/**
 * Layout for the signed-out screens (LL-01, LL-09, TN-01, TN-13/14): on a computer the form sits left and
 * an illustration (projected with [art]) right; on a phone it is one column and the illustration is hidden.
 */
@Component({
  selector: 'app-auth-frame',
  imports: [RouterLink, Icon],
  template: `
    <div class="auth">
      <main class="f">
        <div class="head">
          @if (backTo(); as back) {
            <a class="iconbtn back" [routerLink]="back" [attr.aria-label]="backLabel()"><app-icon name="i-back" /></a>
          }
          <a class="brand" routerLink="/" aria-label="RoomLedger, về trang giới thiệu"><span class="logo"><app-icon name="i-meter" /></span>RoomLedger</a>
        </div>
        <div class="body"><ng-content /></div>
        <ng-content select="[foot]" />
      </main>
      <div class="art" aria-hidden="true" inert><ng-content select="[art]" /></div>
    </div>
  `,
  styles: `
    :host { display: block; }
    .auth { display: grid; grid-template-columns: 1fr 1fr; min-height: 100vh; min-height: 100dvh; }
    .f { padding: 56px clamp(32px, 6vw, 96px); display: flex; flex-direction: column; gap: 24px; min-width: 0; }
    .head { display: flex; align-items: center; gap: 4px; }
    .brand { padding: 0; text-decoration: none; color: var(--ink); min-height: 44px; }
    .back { display: none; margin-left: -12px; }
    .body { margin: auto 0; width: 100%; max-width: 420px; }
    .art { background: var(--soft); display: grid; place-items: center; padding: 56px; }
    @media (max-width: 767px) {
      .auth { grid-template-columns: 1fr; }
      .art { display: none; }
      .f { padding: 12px 20px 28px; gap: 16px; }
      .back { display: grid; }
      .body { margin: 0; max-width: none; flex: 1; display: flex; flex-direction: column; }
    }
  `,
})
export class AuthFrame {
  /** On a phone, shows a back button to this route. */
  readonly backTo = input<string | null>(null);
  readonly backLabel = input('Quay lại');
}
