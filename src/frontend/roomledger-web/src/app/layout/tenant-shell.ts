import { Signal, computed, inject, signal } from '@angular/core';

import { injectApi } from '../core/api/api-client';
import { MeStore } from '../core/data/me.store';
import { TenantHomeStore } from '../core/data/tenant-home.store';
import { TenantInvoiceStore } from '../core/data/tenant-invoice.store';
import { TenantContractStore } from '../features/tenant/contract/tenant-contract.store';
import { NavBadgeCounts, ShellIdentity } from './shell-types';

/** Tenant: invoices still to pay, and a contract waiting for their confirmation. Call in an injection context. */
export function tenantBadges(): NavBadgeCounts {
  const invoices = inject(TenantInvoiceStore);
  const contract = inject(TenantContractStore);
  return computed(() => ({ invoices: invoices.unpaidCount(), contract: contract.decision().status === 'AWAITING_CONFIRM' ? 1 : 0 }));
}

const SAMPLE_IDENTITY: ShellIdentity = {
  place: { title: 'Phòng P.204', sub: 'Nhà trọ Cô Lan' },
  person: { initials: 'MA', name: 'Trần Minh Anh', role: 'Người thuê' },
};

/** Sidebar place and person for the tenant. In API mode: /tenant/home and /me, '—' / empty until loaded. Call in an injection context. */
export function tenantIdentity(): Signal<ShellIdentity> {
  if (!injectApi()) return signal(SAMPLE_IDENTITY);
  const me = inject(MeStore);
  const store = inject(TenantHomeStore);
  return computed(() => {
    const home = store.home();
    const place = !home
      ? { title: '—', sub: '' }
      : home.tenancyId == null
        ? { title: 'Chưa gắn phòng', sub: '' }
        : { title: `Phòng ${home.roomCode ?? ''}`, sub: home.propertyName ?? '' };
    return { place, person: { initials: me.initials(), name: me.me()?.fullName ?? '', role: 'Người thuê' } };
  });
}
