import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { environment } from '../../../../environments/environment';
import { DevSession } from '../../../core/dev/dev-session';
import { MeStore } from '../../../core/data/me.store';
import { TenantHomeStore } from '../../../core/data/tenant-home.store';
import { Viewport } from '../../../core/layout/viewport';
import { Icon } from '../../../shared/ui/icon';

/**
 * TN-12: profile, room, landlord, the "new reading" email preference, password change and sign-out.
 * With the API: name and email from GET /me, room and property from GET /tenant/home. The landlord's name and
 * email and the notification preference are not in the API, so they show only with sample data.
 */
@Component({
  selector: 'app-tenant-account-page',
  imports: [RouterLink, Icon],
  template: `
    @if (loading()) {
      <p class="muted" role="status">Đang tải…</p>
    } @else if (failed()) {
      <div class="banner err" role="alert"><app-icon name="i-alert" /><span>{{ me.error() ?? home.error() }}</span><button type="button" class="btn sec sm" (click)="retry()">Thử lại</button></div>
    } @else {
    @if (!viewport.isPhone()) {
      <div class="top"><div><h1>Tài khoản</h1><p class="sub">Thông tin của bạn và cách RoomLedger báo tin.</p></div></div>
    } @else {
      <div class="m-head"><h1>Tài khoản</h1></div>
    }
    <div class="wrap">
      <div class="sheet row who">
        <span class="av" aria-hidden="true">{{ profile().initials }}</span>
        <div><b>{{ profile().name }}</b><p class="small muted">{{ profile().email }}</p></div>
      </div>
      <ul class="list">
        <li class="list-item">
          <app-icon name="i-door" />
          @if (profile().roomCode) {
            <div class="grow">Phòng {{ profile().roomCode }}<p class="small muted">{{ profile().propertyName }}</p></div>
          } @else {
            <div class="grow">Chưa gắn phòng</div>
          }
        </li>
        @if (home.sample) {
          <li class="list-item"><app-icon name="i-user" /><div class="grow">Chủ nhà: Lê Thị Lan<p class="small muted">lan.chunha@vidu.vn</p></div></li>
        }
        @if (home.sample) {
        <li class="list-item">
          <app-icon name="i-bell" />
          <div class="grow"><span id="notify-label">Báo khi có chỉ số mới</span><p class="small muted" id="notify-help">Email khi chủ nhà duyệt chỉ số</p></div>
          <button type="button" class="sw" role="switch" [attr.aria-checked]="notify()" aria-labelledby="notify-label" aria-describedby="notify-help" (click)="notify.set(!notify())">
            <span class="toggle" [class.on]="notify()"></span>
          </button>
        </li>
        }
        <li><a class="list-item" routerLink="/forgot-password"><app-icon name="i-lock" /><span class="grow">Đổi mật khẩu</span><app-icon name="i-chev" /></a></li>
      </ul>
      @if (localDev) {
        <button type="button" class="btn danger block" (click)="logout()"><app-icon name="i-logout" />Đăng xuất</button>
      } @else {
        <a class="btn danger block" routerLink="/"><app-icon name="i-logout" />Đăng xuất</a>
      }
    </div>
    }
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
  /** LocalDev: forget the sample person and load the app afresh at the role picker (the stores would keep their data otherwise). */
  protected logout(): void {
    this.devSession.signOut();
  }

  protected readonly viewport = inject(Viewport);
  protected readonly localDev = environment.localDev;
  private readonly devSession = inject(DevSession);
  protected readonly me = inject(MeStore);
  protected readonly home = inject(TenantHomeStore);

  protected readonly profile = computed(() =>
    this.home.sample
      ? { initials: 'MA', name: 'Trần Minh Anh', email: 'minhanh@vidu.vn', roomCode: 'P.204', propertyName: 'Nhà trọ Cô Lan' }
      : {
          initials: this.me.initials(),
          name: this.me.me()?.fullName ?? '',
          email: this.me.me()?.email ?? '',
          roomCode: this.home.roomCode(),
          propertyName: this.home.propertyName(),
        },
  );
  protected readonly loading = computed(() => !this.home.sample && [this.me.status(), this.home.status()].some((s) => s === 'idle' || s === 'loading'));
  protected readonly failed = computed(() => !this.home.sample && [this.me.status(), this.home.status()].includes('error'));
  /** Sample data only: the notification preference is not in the API. */
  protected readonly notify = signal(true);

  protected retry(): void {
    this.me.load();
    this.home.load();
  }
}
