import { Routes } from '@angular/router';

import { LANDLORD_SHELL, TENANT_SHELL } from './layout/shell-configs';

const t = (name: string) => `${name} · RoomLedger`;

export const routes: Routes = [
  // Public
  { path: '', pathMatch: 'full', title: 'RoomLedger', loadComponent: () => import('./features/public/landing/landing.page').then((m) => m.LandingPage) },
  { path: 'login', title: t('Đăng nhập'), loadComponent: () => import('./features/auth/login/login.page').then((m) => m.LoginPage) },
  { path: 'forgot-password', title: t('Quên mật khẩu'), loadComponent: () => import('./features/auth/forgot-password/forgot-password.page').then((m) => m.ForgotPasswordPage) },
  { path: 'invite/:token', title: t('Lời mời thuê phòng'), loadComponent: () => import('./features/auth/invite/invite.page').then((m) => m.InvitePage) },
  // Temporary: pick a role to preview while sign-in is not merged.
  { path: 'preview', title: t('Xem thử'), loadComponent: () => import('./features/dev/role-picker.page').then((m) => m.RolePickerPage) },

  {
    path: 'landlord',
    loadComponent: () => import('./layout/app-shell').then((m) => m.AppShell),
    data: { shell: LANDLORD_SHELL },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      { path: 'overview', title: t('Tổng quan'), loadComponent: () => import('./features/landlord/overview/overview.page').then((m) => m.LandlordOverviewPage) },
      { path: 'rooms', title: t('Phòng'), loadComponent: () => import('./features/landlord/rooms/rooms.page').then((m) => m.RoomsPage) },
      { path: 'rooms/:roomId/end-tenancy', title: t('Kết thúc thuê'), loadComponent: () => import('./features/landlord/end-tenancy/end-tenancy.page').then((m) => m.EndTenancyPage) },
      { path: 'meter-readings', title: t('Duyệt chỉ số'), loadComponent: () => import('./features/landlord/meter-review/meter-review.page').then((m) => m.MeterReviewPage) },
      { path: 'meter-capture', title: t('Ghi chỉ số'), loadComponent: () => import('./features/landlord/meter-capture/meter-capture.page').then((m) => m.MeterCapturePage) },
      { path: 'invoices', title: t('Hóa đơn'), loadComponent: () => import('./features/landlord/invoices/invoices.page').then((m) => m.LandlordInvoicesPage) },
      { path: 'contracts', title: t('Hợp đồng'), loadComponent: () => import('./features/landlord/contracts/contracts.page').then((m) => m.ContractsPage) },
      { path: 'disputes', title: t('Khiếu nại'), loadComponent: () => import('./features/landlord/disputes/disputes.page').then((m) => m.LandlordDisputesPage) },
      { path: 'settings', title: t('Đơn giá và kỳ thu'), loadComponent: () => import('./features/landlord/settings/settings.page').then((m) => m.SettingsPage) },
    ],
  },
  {
    path: 'tenant',
    loadComponent: () => import('./layout/app-shell').then((m) => m.AppShell),
    data: { shell: TENANT_SHELL },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
      { path: 'home', title: t('Trang chủ'), loadComponent: () => import('./features/tenant/home/home.page').then((m) => m.TenantHomePage) },
      { path: 'invoices', title: t('Hóa đơn'), loadComponent: () => import('./features/tenant/invoices-list/invoices-list.page').then((m) => m.TenantInvoicesListPage) },
      { path: 'invoices/:invoiceId', title: t('Hóa đơn'), loadComponent: () => import('./features/tenant/invoice/invoice.page').then((m) => m.TenantInvoicePage) },
      { path: 'meters', title: t('Chỉ số điện nước'), loadComponent: () => import('./features/tenant/meters/meters.page').then((m) => m.TenantMetersPage) },
      { path: 'contract', title: t('Hợp đồng'), loadComponent: () => import('./features/tenant/contract/contract.page').then((m) => m.TenantContractPage) },
      { path: 'disputes', title: t('Khiếu nại'), loadComponent: () => import('./features/tenant/disputes/disputes.page').then((m) => m.TenantDisputesPage) },
      { path: 'disputes/new', title: t('Gửi khiếu nại'), loadComponent: () => import('./features/tenant/dispute-new/dispute-new.page').then((m) => m.TenantDisputeNewPage) },
      { path: 'account', title: t('Tài khoản'), loadComponent: () => import('./features/tenant/account/account.page').then((m) => m.TenantAccountPage) },
    ],
  },
  { path: '**', redirectTo: '' },
];
