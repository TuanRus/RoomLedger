import { Signal, computed, inject } from '@angular/core';

import { MeterReviewStore } from '../core/data/meter-review.store';
import { TenantInvoiceStore } from '../core/data/tenant-invoice.store';
import { LandlordDisputesStore } from '../features/landlord/disputes/data/disputes.store';
import { LandlordInvoicesStore } from '../features/landlord/invoices/data/invoices.store';
import { TenantContractStore } from '../features/tenant/contract/tenant-contract.store';

/** Nav badge counts keyed by nav path: only things that are waiting on this person. Call in an injection context. */
export type NavBadgeCounts = Signal<Record<string, number>>;

/** Landlord: readings to decide, draft invoices not sent yet, disputes waiting for an answer. */
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

/** Tenant: invoices still to pay, and a contract waiting for their confirmation. */
export function tenantBadges(): NavBadgeCounts {
  const invoices = inject(TenantInvoiceStore);
  const contract = inject(TenantContractStore);
  return computed(() => ({ invoices: invoices.unpaidCount(), contract: contract.decision().status === 'AWAITING_CONFIRM' ? 1 : 0 }));
}
