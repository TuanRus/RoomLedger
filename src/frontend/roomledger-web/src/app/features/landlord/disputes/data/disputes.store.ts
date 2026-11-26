import { Service, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, map, of, switchMap } from 'rxjs';

import type { components } from '../../../../generated/api-types';
import { injectApi } from '../../../../core/api/api-client';
import { LandlordInvoice } from '../../invoices/data/invoices.sample';
import { LandlordInvoicesStore } from '../../invoices/data/invoices.store';
import { LANDLORD_DISPUTES, LandlordDispute, SAMPLE_RESOLVED_AT } from './disputes.sample';

type DisputeResponse = components['schemas']['DisputeResponse'];

export type ResolveResult = 'ok' | 'empty-reply' | 'not-open' | 'no-reading' | 'invalid-value' | 'failed';

/**
 * The disputes inside the invoices (InvoiceDetailResponse.disputes), as the landlord handles them.
 * The API has no dispute list, so they come from the loaded invoices; what the API does not carry
 * (AI value, approval time, the number the tenant reports, capture time) stays null.
 */
export function disputesOf(invoices: LandlordInvoice[]): LandlordDispute[] {
  return invoices.flatMap((inv) =>
    (inv.disputes ?? []).map((d): LandlordDispute => {
      const kind = d.readingId && d.readingId === inv.electricity.readingId ? 'ELECTRICITY' : d.readingId && d.readingId === inv.water.readingId ? 'WATER' : null;
      const line = kind === 'ELECTRICITY' ? inv.electricity : kind === 'WATER' ? inv.water : null;
      const corrected = d.resolution === 'CORRECTED';
      return {
        disputeId: d.disputeId,
        invoiceId: inv.invoiceId,
        readingId: d.readingId,
        roomCode: inv.roomCode,
        tenantName: inv.tenantName,
        period: inv.period,
        reason: d.reason,
        status: d.status,
        resolution: d.resolution,
        response: d.response,
        createdAt: d.createdAt,
        resolvedAt: d.resolvedAt,
        reading:
          kind && line
            ? {
                kind,
                previous: line.previous,
                aiValue: null,
                approved: corrected ? null : line.current,
                approvedAt: null,
                tenantValue: null,
                rate: line.rate,
                photoUrl: line.photoUrl,
                capturedAt: null,
                corrected: corrected ? line.current : null,
              }
            : null,
      };
    }),
  );
}

/**
 * Tenant disputes the landlord answers (LL-08). Sample data by default; with environment.dataSource 'api' they are
 * read from the invoices (GET /disputes does not exist) and answered with PATCH /disputes/{id}:
 * CORRECTED + value (the server recalculates the invoice) or KEPT + response, then the invoice is read again.
 */
@Service()
export class LandlordDisputesStore {
  private readonly api = injectApi();
  private readonly invoices = inject(LandlordInvoicesStore);

  /** True with sample data. */
  readonly sample = !this.api;
  private readonly sampleData = signal<LandlordDispute[]>(this.api ? [] : LANDLORD_DISPUTES);
  private readonly actionError = signal<string | null>(null);
  /** The dispute and invoice as they were before the last resolution (sample data), for undo. */
  private lastResolved: { dispute: LandlordDispute; invoice: LandlordInvoice | undefined } | null = null;

  readonly disputes = computed<LandlordDispute[]>(() => (this.api ? disputesOf(this.invoices.invoices()) : this.sampleData()));
  /** Loading state of the invoices the disputes come from. */
  readonly status = this.invoices.status;
  /** Failed answer, or the invoices' load error. */
  readonly error = computed(() => this.actionError() ?? this.invoices.error());
  readonly resolving = signal(false);
  /** Answered disputes sit in invoices whose detail is read on demand: true while those are still arriving. */
  readonly loadingMore = computed(() => this.invoices.detailsLoading() > 0);

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

  /** (Re)loads the invoices the disputes come from. */
  load(): void {
    this.actionError.set(null);
    this.invoices.load();
  }

  /** API: reads the invoices that only have their list row, so answered disputes show too. Call once the list is loaded. */
  loadAnswered(): void {
    this.invoices.ensureAllDetails();
  }

  /** Invoice total now and after correcting the reading to `value`. */
  recalc(d: LandlordDispute, value: number): { before: number; after: number } | null {
    const inv = this.invoices.find(d.invoiceId);
    if (!inv || !d.reading) return null;
    const after = this.invoices.totalWith(d.invoiceId, d.reading.kind, value);
    return after == null ? null : { before: inv.total, after };
  }

  /** "Sửa chỉ số" (sample data): CORRECTED with the new value; the invoice is recalculated and the tenant is told. */
  correct(id: string, value: number): ResolveResult {
    const check = this.checkCorrection(id, value);
    if (check !== 'ok') return check;
    const d = this.find(id)!;
    const response = this.correctionText(d, value);
    this.remember(d);
    this.invoices.applyCorrection(d.invoiceId, d.reading!.kind, value, SAMPLE_RESOLVED_AT);
    this.patch(id, {
      status: 'RESOLVED', resolution: 'CORRECTED', response, resolvedAt: SAMPLE_RESOLVED_AT,
      reading: { ...d.reading!, corrected: value },
    });
    return 'ok';
  }

  /** "Giữ nguyên và trả lời" (sample data): KEPT with a required reply. */
  keep(id: string, reply: string): ResolveResult {
    const d = this.find(id);
    if (!d || d.status !== 'OPEN') return 'not-open';
    const text = reply.trim();
    if (!text) return 'empty-reply';
    this.remember(d);
    this.invoices.closeDispute(d.invoiceId);
    this.patch(id, { status: 'RESOLVED', resolution: 'KEPT', response: text, resolvedAt: SAMPLE_RESOLVED_AT });
    return 'ok';
  }

  /** API: PATCH /disputes/{id} CORRECTED + value, then reads the recalculated invoice. */
  correctByApi(id: string, value: number): Observable<ResolveResult> {
    const check = this.checkCorrection(id, value);
    if (check !== 'ok') return of(check);
    const d = this.find(id)!;
    return this.resolve(d, { resolution: 'CORRECTED', value, response: this.correctionText(d, value) });
  }

  /** API: PATCH /disputes/{id} KEPT + response, then reads the invoice (back to SENT). */
  keepByApi(id: string, reply: string): Observable<ResolveResult> {
    const d = this.find(id);
    if (!d || d.status !== 'OPEN') return of<ResolveResult>('not-open');
    const text = reply.trim();
    if (!text) return of<ResolveResult>('empty-reply');
    return this.resolve(d, { resolution: 'KEPT', response: text });
  }

  /**
   * Sample data: reverts the last correct() or keep() while its undo toast is showing: the dispute is open again
   * and the invoice is back to what it was. With the API there is no undo (the answer is already sent).
   */
  undoResolve(): string | null {
    if (!this.sample) return null;
    const last = this.lastResolved;
    if (!last) return null;
    this.sampleData.update((list) => list.map((d) => (d.disputeId === last.dispute.disputeId ? last.dispute : d)));
    if (last.invoice) this.invoices.restore(last.invoice);
    this.lastResolved = null;
    return last.dispute.disputeId;
  }

  private remember(d: LandlordDispute): void {
    this.lastResolved = { dispute: d, invoice: this.invoices.find(d.invoiceId) };
  }

  find(id: string): LandlordDispute | undefined {
    return this.disputes().find((d) => d.disputeId === id);
  }

  private resolve(d: LandlordDispute, body: { resolution: 'CORRECTED' | 'KEPT'; value?: number; response: string }): Observable<ResolveResult> {
    const api = this.api;
    if (!api) return of<ResolveResult>('failed');
    this.actionError.set(null);
    this.resolving.set(true);
    return api.patch<DisputeResponse>(`/disputes/${d.disputeId}`, body).pipe(
      switchMap(() => this.invoices.reloadInvoice(d.invoiceId)),
      map((): ResolveResult => 'ok'),
      catchError((err: unknown) => {
        this.actionError.set(err instanceof Error ? err.message : 'Chưa trả lời được khiếu nại, thử lại sau.');
        return of<ResolveResult>('failed');
      }),
      finalize(() => this.resolving.set(false)),
    );
  }

  private checkCorrection(id: string, value: number): ResolveResult {
    const d = this.find(id);
    if (!d || d.status !== 'OPEN') return 'not-open';
    if (!d.reading) return 'no-reading';
    if (!Number.isInteger(value) || value < d.reading.previous) return 'invalid-value';
    return 'ok';
  }

  /** The reply the tenant reads after a correction: old value, new value, new total. */
  private correctionText(d: LandlordDispute, value: number): string {
    const r = d.reading!;
    const unit = r.kind === 'ELECTRICITY' ? 'kWh' : 'm³';
    const fmt = new Intl.NumberFormat('vi-VN');
    const totals = this.recalc(d, value);
    const from = r.approved != null ? `${fmt.format(r.approved)} → ` : '';
    return `Đã sửa chỉ số ${from}${fmt.format(value)} ${unit}.` + (totals ? ` Hóa đơn tính lại: ${fmt.format(totals.after)} đ.` : '');
  }

  private patch(id: string, changes: Partial<LandlordDispute>): void {
    this.sampleData.update((list) => list.map((d) => (d.disputeId === id ? { ...d, ...changes } : d)));
  }
}
