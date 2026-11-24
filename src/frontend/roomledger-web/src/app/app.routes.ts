import { Routes } from '@angular/router';

import { LANDLORD_SHELL, TENANT_SHELL } from './layout/shell-configs';

const placeholder = (title: string, frames: string) => ({
  loadComponent: () => import('./features/placeholder/placeholder.page').then((m) => m.PlaceholderPage),
  data: { title, frames },
  title: `${title} · RoomLedger`,
});

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'RoomLedger',
    loadComponent: () => import('./features/dev/role-picker.page').then((m) => m.RolePickerPage),
  },
  {
    path: 'landlord',
    loadComponent: () => import('./layout/app-shell').then((m) => m.AppShell),
    data: { shell: LANDLORD_SHELL },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'meter-readings' },
      {
        path: 'meter-readings',
        title: 'Duyệt chỉ số · RoomLedger',
        loadComponent: () => import('./features/landlord/meter-review/meter-review.page').then((m) => m.MeterReviewPage),
      },
      { path: 'overview', ...placeholder('Tổng quan', 'LL-02') },
      { path: 'rooms', ...placeholder('Phòng', 'LL-03') },
      { path: 'meter-capture', ...placeholder('Ghi chỉ số', 'LM-04 đến LM-10') },
      { path: 'invoices', ...placeholder('Hóa đơn', 'LL-05, LL-05b/c/d') },
      { path: 'contracts', ...placeholder('Hợp đồng', 'LL-07') },
      { path: 'disputes', ...placeholder('Khiếu nại', 'LL-08') },
      { path: 'settings', ...placeholder('Đơn giá và kỳ thu', 'LL-06') },
    ],
  },
  {
    path: 'tenant',
    loadComponent: () => import('./layout/app-shell').then((m) => m.AppShell),
    data: { shell: TENANT_SHELL },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
      { path: 'invoices', pathMatch: 'full', redirectTo: 'invoices/inv-204-2026-11' },
      {
        path: 'invoices/:invoiceId',
        title: 'Hóa đơn · RoomLedger',
        loadComponent: () => import('./features/tenant/invoice/invoice.page').then((m) => m.TenantInvoicePage),
      },
      { path: 'home', ...placeholder('Trang chủ', 'TD-01 / TN-02') },
      { path: 'meters', ...placeholder('Chỉ số điện nước', 'TD-03 / TN-03, TN-04') },
      { path: 'contract', ...placeholder('Hợp đồng', 'TN-10') },
      { path: 'disputes', ...placeholder('Khiếu nại', 'TD-04 / TN-11, TN-20') },
      { path: 'account', ...placeholder('Tài khoản', 'TN-12') },
    ],
  },
  { path: '**', redirectTo: '' },
];
