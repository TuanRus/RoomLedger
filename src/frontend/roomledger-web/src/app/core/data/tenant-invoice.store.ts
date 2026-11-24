import { Service, computed, signal } from '@angular/core';

import { InvoiceDetail, InvoiceSummary, PaymentInfo } from './models';
import { LANDLORD_PAYMENT_INFO, TENANT_INVOICES, TENANT_NEXT_CAPTURE } from './sample-ledger';

/** What comes after an invoice: the next period's invoice if it exists, otherwise when the landlord reads the meters. */
export type NextAfter =
  | { kind: 'invoice'; invoice: InvoiceSummary }
  | { kind: 'schedule'; period: string; window: string };

/** "2026-11" -> "2026-12", "2026-12" -> "2027-01". */
export function nextPeriod(period: string): string {
  const [y, m] = period.split('-').map(Number);
  return m === 12 ? `${y + 1}-01` : `${y}-${String(m + 1).padStart(2, '0')}`;
}

function summaryOf({ invoiceId, period, status, total, paidAt }: InvoiceDetail): InvoiceSummary {
  return { invoiceId, period, status, total, paidAt };
}

/**
 * The signed-in tenant's invoices. Backed by sample data until the API is connected:
 * replace with GET /invoices and GET /invoices/{id}; payment details are not in the contract yet.
 */
@Service()
export class TenantInvoiceStore {
  private readonly invoices = signal<InvoiceDetail[]>(TENANT_INVOICES);

  /** Newest first. */
  readonly history = computed<InvoiceSummary[]>(() => this.invoices().map(summaryOf));
  readonly unpaidCount = computed(() => this.invoices().filter((i) => i.status === 'SENT' || i.status === 'DISPUTED').length);
  readonly latest = computed<InvoiceDetail | null>(() => this.invoices()[0] ?? null);
  readonly payment = signal<PaymentInfo>(LANDLORD_PAYMENT_INFO);
  readonly nextCapture = TENANT_NEXT_CAPTURE;

  detail(invoiceId: string): InvoiceDetail | null {
    return this.invoices().find((i) => i.invoiceId === invoiceId) ?? null;
  }

  nextAfter(invoiceId: string): NextAfter | null {
    const inv = this.detail(invoiceId);
    if (!inv) return null;
    const period = nextPeriod(inv.period);
    const next = this.invoices().find((i) => i.period === period);
    if (next) return { kind: 'invoice', invoice: summaryOf(next) };
    return { kind: 'schedule', period, window: period === this.nextCapture.period ? this.nextCapture.window : `01–03/${nextPeriod(period).slice(5)}/${nextPeriod(period).slice(0, 4)}` };
  }
}
