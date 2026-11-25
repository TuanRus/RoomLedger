import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Viewport } from '../../../core/layout/viewport';
import { Icon } from '../../../shared/ui/icon';

/**
 * TN-12: profile, room, landlord, the "new reading" email preference, password change and sign-out.
 * Sample data: name and email come from GET /me; room and landlord are not in MeResponse / TenantHomeResponse
 * (landlord name and email are missing); the notification preference has no API yet.
 */
@Component({
  selector: 'app-tenant-account-page',
  imports: [RouterLink, Icon],
  template: `
    @if (!viewport.isPhone()) {
      <div class="top"><div><h1>Tài khoản</h1><p class="sub">Thông tin của bạn và cách RoomLedger báo tin.</p></div></div>
    } @else {
      <div class="m-head"><h1>Tài khoản</h1></div>
    }
    <div class="wrap">
      <div class="sheet row who">
        <span class="av" aria-hidden="true">{{ profile.initials }}</span>
        <div><b>{{ profile.name }}</b><p class="small muted">{{ profile.email }}</p></div>
      </div>
      <ul class="list">
        <li class="list-item"><app-icon name="i-door" /><div class="grow">Phòng {{ profile.roomCode }}<p class="small muted">{{ profile.propertyName }}</p></div></li>
        <li class="list-item"><app-icon name="i-user" /><div class="grow">Chủ nhà: {{ profile.landlordName }}<p class="small muted">{{ profile.landlordEmail }}</p></div></li>
        <li class="list-item">
          <app-icon name="i-bell" />
          <div class="grow"><span id="notify-label">Báo khi có chỉ số mới</span><p class="small muted" id="notify-help">Email khi chủ nhà duyệt chỉ số</p></div>
          <button type="button" class="sw" role="switch" [attr.aria-checked]="notify()" aria-labelledby="notify-label" aria-describedby="notify-help" (click)="notify.set(!notify())">
            <span class="toggle" [class.on]="notify()"></span>
          </button>
        </li>
        <li><a class="list-item" routerLink="/forgot-password"><app-icon name="i-lock" /><span class="grow">Đổi mật khẩu</span><app-icon name="i-chev" /></a></li>
      </ul>
      <a class="btn danger block" routerLink="/"><app-icon name="i-logout" />Đăng xuất</a>
    </div>
  `,
  styles: `
    :host { display: block; }
    .m-head { display: flex; align-items: center; min-height: 56px; margin-bottom: 8px; }
    .m-head h1 { font-size: var(--t-24); letter-spacing: -.025em; }
    .wrap { display: grid; gap: 14px; max-width: 560px; }
    .who b { font-weight: 500; font-size: var(--t-18); }
    .who .av { width: 52px; height: 52px; background: var(--warn); }
    .list { list-style: none; margin: 0; padding: 0; }
    .list > li:not(.list-item) { border-bottom: 1px solid var(--line); }
    .list > li:last-child { border-bottom: 0; }
    .list app-icon { color: var(--ink-3); }
    a.list-item { color: inherit; text-decoration: none; border-bottom: 0; }
    a.list-item:hover { background: var(--soft); }
    .grow { flex: 1; min-width: 0; }
    .sw { border: 0; background: none; padding: 9px 0; cursor: pointer; display: grid; border-radius: 999px; }
  `,
})
export class TenantAccountPage {
  protected readonly viewport = inject(Viewport);
  protected readonly profile = {
    initials: 'MA',
    name: 'Trần Minh Anh',
    email: 'minhanh@vidu.vn',
    roomCode: 'P.204',
    propertyName: 'Nhà trọ Cô Lan',
    landlordName: 'Lê Thị Lan',
    landlordEmail: 'lan.chunha@vidu.vn',
  };
  protected readonly notify = signal(true);
}
