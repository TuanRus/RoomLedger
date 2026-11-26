import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { environment } from '../../../environments/environment';
import { DevSession } from '../../core/dev/dev-session';
import { DEV_LANDLORD, DEV_TENANT, DevUser } from '../../core/dev/dev-users';
import { Icon } from '../../shared/ui/icon';

/**
 * Temporary entry point while sign-in (Kiru, Cognito) is not merged: choose which role to preview.
 * Only routed with sample data or in LocalDev (see app.routes.ts), never in the production API build.
 * With pnpm start:local-api it also chooses which sample person (X-Dev-User) the API calls act as; the choice is
 * followed by a full page load, because the stores load once and would keep the previous person's data.
 * Replace with the real login route once auth lands.
 */
@Component({
  selector: 'app-role-picker-page',
  imports: [RouterLink, Icon],
  template: `
    <main class="picker">
      <div class="brand"><span class="logo"><app-icon name="i-meter" /></span>RoomLedger</div>
      <h1>Xem giao diện với vai trò</h1>
      <p class="muted">{{ localDev ? 'Bản chạy với API trên máy (LocalDev): dữ liệu từ script seed.' : 'Bản xây dựng tạm, dữ liệu mẫu.' }} Đăng nhập thật sẽ thay màn này.</p>
      <div class="choices">
        <a class="card" [routerLink]="localDev ? null : landlordPath" [attr.href]="localDev ? landlordPath : null" (click)="pick('landlord', landlordPath, $event)"><app-icon name="i-grid" /><b>Chủ nhà</b><span class="small muted">{{ landlordLabel }}</span></a>
        <a class="card" [routerLink]="localDev ? null : tenantPath" [attr.href]="localDev ? tenantPath : null" (click)="pick('tenant', tenantPath, $event)"><app-icon name="i-user" /><b>Người thuê</b><span class="small muted">{{ tenantLabel }}</span></a>
      </div>
    </main>
  `,
  styles: `
    .picker { max-width: 640px; margin: 0 auto; padding: 64px 20px; display: grid; gap: 16px; }
    .brand { padding: 0; margin-bottom: 24px; }
    h1 { font-size: var(--t-32); letter-spacing: -.03em; }
    .choices { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; margin-top: 8px; }
    .card { display: grid; gap: 6px; text-decoration: none; color: var(--ink); cursor: pointer; }
    .card:hover { border-color: var(--line-strong); }
    .card b { font-size: var(--t-20); font-weight: 500; }
  `,
})
export class RolePickerPage {
  protected readonly localDev = environment.localDev;
  protected readonly landlordPath = '/landlord/overview';
  protected readonly tenantPath = '/tenant/home';
  /** Sample names only with sample data; LocalDev shows the seeded people; no other build routes this page. */
  protected readonly landlordLabel = environment.localDev ? DEV_LANDLORD.name : environment.dataSource === 'sample' ? 'Nhà trọ Cô Lan · kỳ 11/2026' : '';
  protected readonly tenantLabel = environment.localDev ? DEV_TENANT.name : environment.dataSource === 'sample' ? 'Trần Minh Anh · P.204' : '';
  /** LocalDev: the API call headers act as the person picked here (see DevSession). Harmless with sample data. */
  private readonly session = inject(DevSession);

  /** LocalDev: pick the person, then load the app afresh at their home. Otherwise the router link navigates. */
  protected pick(role: DevUser['group'], path: string, event: Event): void {
    if (!this.localDev) {
      this.session.signInAs(role);
      return;
    }
    event.preventDefault();
    this.session.signInAs(role, path);
  }
}
