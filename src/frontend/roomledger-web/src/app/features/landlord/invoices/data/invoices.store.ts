import { Service, computed, signal } from '@angular/core';

import { MeterKind } from '../../../../core/data/models';
import {
  INVOICE_DUE_DATE,
  INVOICE_PERIOD,
  LANDLORD_INVOICES,
  LandlordInvoice,
  ROOMS_WITHOUT_INVOICE,
  SAMPLE_PAID_AT,
  SAMPLE_REMINDED_AT,
  SAMPLE_SENT_AT,
  SAMPLE_TODAY,
} from './invoices.sample';

export type InvoiceGroup = 'draft' | 'unpaid' | 'paid' | 'overdue';

const DAY_MS = 86_400_000;

/** Whole days between two yyyy-MM-dd dates (b − a). */
function daysBetween(a: string, b: string): number {
  return Math.round((Date.parse(b) - Date.parse(a)) / DAY_MS);
}

/**
 * The landlord's invoices (LL-05). Backed by sample data until the API is connected:
 * GET /invoices?period=, POST /invoices/{id}/send, POST /invoices/{id}/paid.
 * Unmarking paid, reminders and the correction after a dispute have no endpoint yet.
 */
@Service()
export class LandlordInvoicesStore {
  readonly period = INVOICE_PERIOD;
  readonly dueDate = INVOICE_DUE_DATE;
  readonly roomsWithoutInvoice = ROOMS_WITHOUT_INVOICE;
  readonly today = SAMPLE_TODAY;

  readonly invoices = signal<LandlordInvoice[]>(LANDLORD_INVOICES);
  /** When the last batch of drafts was sent in this session, for the LL-05d banner. */
  readonly lastBatch = signal<{ count: number; at: string } | null>(null);

  /** Invoices of the current period, without the overdue ones from earlier periods. */
  readonly current = computed(() => this.invoices().filter((i) => i.period === this.period));
  readonly drafts = computed(() => this.current().filter((i) => i.status === 'DRAFT'));
  readonly draftTotal = computed(() => this.drafts().reduce((sum, i) => sum + i.total, 0));

  readonly counts = computed(() => {
    const c: Record<InvoiceGroup, number> = { draft: 0, unpaid: 0, paid: 0, overdue: 0 };
    for (const i of this.invoices()) c[this.groupOf(i)]++;
    return { ...c, all: this.current().length, sent: this.current().length - c.draft };
  });

  /** Unpaid invoices the landlord can remind about (sent or disputed, any period). */
  readonly unpaid = computed(() => this.invoices().filter((i) => i.status === 'SENT' || i.status === 'DISPUTED'));

  groupOf(inv: LandlordInvoice): InvoiceGroup {
    if (inv.period !== this.period && inv.status !== 'PAID') return 'overdue';
    if (inv.status === 'DRAFT') return 'draft';
    if (inv.status === 'PAID') return 'paid';
    return 'unpaid';
  }

  /** Days past the due date for an unpaid invoice, 0 if not overdue. */
  overdueDays(inv: LandlordInvoice): number {
    if (inv.status === 'PAID' || inv.status === 'DRAFT') return 0;
    return Math.max(0, daysBetween(inv.dueDate, this.today));
  }

  /** Send the given drafts (all drafts when omitted). Returns how many were sent. */
  send(ids?: string[]): number {
    const target = new Set(ids ?? this.drafts().map((i) => i.invoiceId));
    let count = 0;
    this.invoices.update((list) =>
      list.map((i) => {
        if (!target.has(i.invoiceId) || i.status !== 'DRAFT') return i;
        count++;
        return { ...i, status: 'SENT', sentAt: SAMPLE_SENT_AT };
      }),
    );
    if (count > 0 && !ids) this.lastBatch.set({ count, at: SAMPLE_SENT_AT });
    return count;
  }

  /** SENT → PAID. A disputed invoice cannot be marked paid (BR-24). Returns false when not allowed. */
  markPaid(id: string): boolean {
    const inv = this.find(id);
    if (!inv || inv.status !== 'SENT') return false;
    this.patch(id, { status: 'PAID', paidAt: SAMPLE_PAID_AT });
    return true;
  }

  /** PAID → SENT, for undo and "Bỏ đánh dấu đã thanh toán". */
  unmarkPaid(id: string): void {
    if (this.find(id)?.status === 'PAID') this.patch(id, { status: 'SENT', paidAt: null });
  }

  remind(ids: string[]): number {
    const target = new Set(ids);
    let count = 0;
    this.invoices.update((list) =>
      list.map((i) => {
        if (!target.has(i.invoiceId) || (i.status !== 'SENT' && i.status !== 'DISPUTED')) return i;
        count++;
        return { ...i, remindedAt: SAMPLE_REMINDED_AT };
      }),
    );
    return count;
  }

  /** The total if one reading of an invoice were changed to `value`. */
  totalWith(id: string, kind: MeterKind, value: number): number | null {
    const inv = this.find(id);
    if (!inv) return null;
    const line = kind === 'ELECTRICITY' ? inv.electricity : inv.water;
    const usage = Math.max(0, value - line.previous);
    return inv.total - line.amount + usage * line.rate;
  }

  /** Recalculate an invoice after a dispute corrected one reading; the dispute is closed so it goes back to SENT. */
  applyCorrection(id: string, kind: MeterKind, value: number, at: string): void {
    const inv = this.find(id);
    if (!inv) return;
    const key = kind === 'ELECTRICITY' ? 'electricity' : 'water';
    const line = inv[key];
    const usage = Math.max(0, value - line.previous);
    const next = { ...line, current: value, usage, amount: usage * line.rate };
    this.patch(id, {
      [key]: next,
      total: inv.total - line.amount + next.amount,
      status: inv.status === 'DISPUTED' ? 'SENT' : inv.status,
      correction: { kind, from: line.current, to: value, at },
    });
  }

  /** The dispute was answered without a change: the invoice is payable again. */
  closeDispute(id: string): void {
    if (this.find(id)?.status === 'DISPUTED') this.patch(id, { status: 'SENT' });
  }

  find(id: string): LandlordInvoice | undefined {
    return this.invoices().find((i) => i.invoiceId === id);
  }

  private patch(id: string, changes: Partial<LandlordInvoice>): void {
    this.invoices.update((list) => list.map((i) => (i.invoiceId === id ? { ...i, ...changes } : i)));
  }
}
