import { Service, computed, inject, signal } from '@angular/core';

import { LandlordInvoicesStore } from '../../invoices/data/invoices.store';
import { LANDLORD_DISPUTES, LandlordDispute, SAMPLE_RESOLVED_AT } from './disputes.sample';

export type ResolveResult = 'ok' | 'empty-reply' | 'not-open' | 'no-reading' | 'invalid-value';

/**
 * Tenant disputes the landlord answers (LL-08). Backed by sample data until the API is connected:
 * GET /disputes is not in the contract yet (disputes only come inside InvoiceDetailResponse);
 * resolving maps to PATCH /disputes/{id} with CORRECTED + value or KEPT + response.
 * Correcting a reading recalculates the invoice in LandlordInvoicesStore, like the backend does.
 */
@Service()
export class LandlordDisputesStore {
  private readonly invoices = inject(LandlordInvoicesStore);

  readonly disputes = signal<LandlordDispute[]>(LANDLORD_DISPUTES);

  /** Open first (oldest first), then answered (newest first). */
  readonly ordered = computed(() => {
    const list = this.disputes();
    const open = list.filter((d) => d.status === 'OPEN').sort((a, b) => a.createdAt.localeCompare(b.createdAt));
    const done = list.filter((d) => d.status === 'RESOLVED').sort((a, b) => (b.resolvedAt ?? '').localeCompare(a.resolvedAt ?? ''));
    return [...open, ...done];
  });

  readonly counts = computed(() => {
    const open = this.disputes().filter((d) => d.status === 'OPEN').length;
    return { open, resolved: this.disputes().length - open };
  });

  /** Invoice total now and after correcting the reading to `value`. */
  recalc(d: LandlordDispute, value: number): { before: number; after: number } | null {
    const inv = this.invoices.find(d.invoiceId);
    if (!inv || !d.reading) return null;
    const after = this.invoices.totalWith(d.invoiceId, d.reading.kind, value);
    return after == null ? null : { before: inv.total, after };
  }

  /** "Sửa chỉ số": CORRECTED with the new value; the invoice is recalculated and the tenant is told. */
  correct(id: string, value: number): ResolveResult {
    const d = this.find(id);
    if (!d || d.status !== 'OPEN') return 'not-open';
    if (!d.reading) return 'no-reading';
    if (!Number.isInteger(value) || value < d.reading.previous) return 'invalid-value';
    const totals = this.recalc(d, value);
    this.invoices.applyCorrection(d.invoiceId, d.reading.kind, value, SAMPLE_RESOLVED_AT);
    const unit = d.reading.kind === 'ELECTRICITY' ? 'kWh' : 'm³';
    const fmt = new Intl.NumberFormat('vi-VN');
    const response =
      `Đã sửa chỉ số ${fmt.format(d.reading.approved)} → ${fmt.format(value)} ${unit}.` +
      (totals ? ` Hóa đơn tính lại: ${fmt.format(totals.after)} đ.` : '');
    this.patch(id, {
      status: 'RESOLVED', resolution: 'CORRECTED', response, resolvedAt: SAMPLE_RESOLVED_AT,
      reading: { ...d.reading, corrected: value },
    });
    return 'ok';
  }

  /** "Giữ nguyên và trả lời": KEPT with a required reply. */
  keep(id: string, reply: string): ResolveResult {
    const d = this.find(id);
    if (!d || d.status !== 'OPEN') return 'not-open';
    const text = reply.trim();
    if (!text) return 'empty-reply';
    this.invoices.closeDispute(d.invoiceId);
    this.patch(id, { status: 'RESOLVED', resolution: 'KEPT', response: text, resolvedAt: SAMPLE_RESOLVED_AT });
    return 'ok';
  }

  find(id: string): LandlordDispute | undefined {
    return this.disputes().find((d) => d.disputeId === id);
  }

  private patch(id: string, changes: Partial<LandlordDispute>): void {
    this.disputes.update((list) => list.map((d) => (d.disputeId === id ? { ...d, ...changes } : d)));
  }
}
