import { Signal, computed, inject, signal } from '@angular/core';

import { injectApi } from '../core/api/api-client';
import { LandlordPropertyStore } from '../core/data/landlord-property.store';
import { MeStore } from '../core/data/me.store';
import { MeterReviewStore } from '../core/data/meter-review.store';
import { LandlordDisputesStore } from '../features/landlord/disputes/data/disputes.store';
import { LandlordInvoicesStore } from '../features/landlord/invoices/data/invoices.store';
import { NavBadgeCounts, ShellIdentity } from './shell-types';

/** Landlord: readings to decide, draft invoices not sent yet, disputes waiting for an answer. Call in an injection context. */
export function landlordBadges(): NavBadgeCounts {
  const meters = inject(MeterReviewStore);
  const invoices = inject(LandlordInvoicesStore);
  const disputes = inject(LandlordDisputesStore);
  return computed(() => ({
    'meter-readings': meters.attentionAll(),
    invoices: invoices.drafts().length,
    disputes: disputes.counts().open,
  }));
}

const SAMPLE_IDENTITY: ShellIdentity = {
  place: { title: 'Nhà trọ Cô Lan', sub: '36 phòng · 32 đang thuê' },
  person: { initials: 'LL', name: 'Lê Thị Lan', role: 'Chủ nhà' },
};

/** Sidebar place and person for the landlord. In API mode: /properties, its rooms and /me, '—' / empty until loaded. Call in an injection context. */
export function landlordIdentity(): Signal<ShellIdentity> {
  if (!injectApi()) return signal(SAMPLE_IDENTITY);
  const me = inject(MeStore);
  const store = inject(LandlordPropertyStore);
  return computed(() => {
    const p = store.property();
    const rooms = `${p?.roomCount ?? 0} phòng`;
    const place = p
      ? { title: p.name ?? '—', sub: store.roomsLoaded() ? `${rooms} · ${store.occupiedCount()} đang thuê` : rooms }
      : store.missing()
        ? { title: 'Chưa có nhà trọ', sub: '' }
        : { title: '—', sub: '' };
    return { place, person: { initials: me.initials(), name: me.me()?.fullName ?? '', role: 'Chủ nhà' } };
  });
}
