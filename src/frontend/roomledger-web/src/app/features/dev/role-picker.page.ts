import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Icon } from '../../shared/ui/icon';

/**
 * Temporary entry point while sign-in (Kiru, Cognito) is not merged: choose which role to preview.
 * Replace with the real login route once auth lands.
 */
@Component({
  selector: 'app-role-picker-page',
  imports: [RouterLink, Icon],
  template: `
    <main class="picker">
      <div class="brand"><span class="logo"><app-icon name="i-meter" /></span>RoomLedger</div>
      <h1>Xem giao diện với vai trò</h1>
      <p class="muted">Bản xây dựng tạm, dữ liệu mẫu. Đăng nhập thật sẽ thay màn này.</p>
      <div class="choices">
        <a class="card" routerLink="/landlord/meter-readings"><app-icon name="i-grid" /><b>Chủ nhà</b><span class="small muted">Duyệt chỉ số kỳ 11/2026</span></a>
        <a class="card" routerLink="/tenant/invoices/inv-204-2026-11"><app-icon name="i-user" /><b>Người thuê</b><span class="small muted">Hóa đơn P.204 kỳ 11/2026</span></a>
      </div>
    </main>
  `,
  styles: `
    .picker { max-width: 640px; margin: 0 auto; padding: 64px 20px; display: grid; gap: 16px; }
    .brand { padding: 0; margin-bottom: 24px; }
    h1 { font-size: var(--t-32); letter-spacing: -.03em; }
    .choices { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; margin-top: 8px; }
    .card { display: grid; gap: 6px; text-decoration: none; color: var(--ink); }
    .card:hover { border-color: var(--line-strong); }
    .card b { font-size: var(--t-20); font-weight: 500; }
  `,
})
export class RolePickerPage {}
