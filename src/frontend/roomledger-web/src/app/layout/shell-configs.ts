import { ShellConfig } from './app-shell';

/**
 * Sidebar order is array order; `phone` is the position in the phone bottom bar. Items without one
 * go in the phone "Thêm" sheet when the bar has a free slot (landlord), otherwise they are hidden there.
 */
export const LANDLORD_SHELL: ShellConfig = {
  role: 'landlord',
  navLabel: 'Chủ nhà',
  place: { title: 'Nhà trọ Cô Lan', sub: '36 phòng · 32 đang thuê' },
  person: { initials: 'LL', name: 'Lê Thị Lan', role: 'Chủ nhà' },
  nav: [
    { path: 'overview', icon: 'i-grid', label: 'Tổng quan', phone: 1 },
    { path: 'meter-capture', icon: 'i-camera', label: 'Ghi chỉ số', phone: 3, fab: true },
    { path: 'meter-readings', icon: 'i-meter', label: 'Duyệt chỉ số', short: 'Duyệt', phone: 2 },
    { path: 'invoices', icon: 'i-bill', label: 'Hóa đơn', phone: 4 },
    // Not in the phone bar: reached from the "Thêm" sheet.
    { path: 'disputes', icon: 'i-talk', label: 'Khiếu nại' },
    { path: 'rooms', icon: 'i-door', label: 'Phòng' },
    { path: 'contracts', icon: 'i-doc', label: 'Hợp đồng' },
    { path: 'settings', icon: 'i-settings', label: 'Đơn giá và kỳ thu' },
  ],
};

export const TENANT_SHELL: ShellConfig = {
  role: 'tenant',
  navLabel: 'Người thuê',
  place: { title: 'Phòng P.204', sub: 'Nhà trọ Cô Lan' },
  person: { initials: 'MA', name: 'Trần Minh Anh', role: 'Người thuê' },
  nav: [
    { path: 'home', icon: 'i-home', label: 'Trang chủ', phone: 1 },
    { path: 'invoices', icon: 'i-bill', label: 'Hóa đơn', badge: '1', phone: 2 },
    { path: 'meters', icon: 'i-meter', label: 'Chỉ số điện nước', short: 'Chỉ số', phone: 3 },
    { path: 'contract', icon: 'i-doc', label: 'Hợp đồng', phone: 4 },
    { path: 'disputes', icon: 'i-talk', label: 'Khiếu nại' },
    { path: 'account', icon: 'i-user', label: 'Tài khoản', phone: 5 },
  ],
};
