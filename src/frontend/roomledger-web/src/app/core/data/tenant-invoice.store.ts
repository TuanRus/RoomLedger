import { DestroyRef, Service, computed, inject, signal } from '@angular/core';
import { Observable, Subscription, finalize, forkJoin, map, of, switchMap } from 'rxjs';

import type { components } from '../../generated/api-types';
import { PagedResult } from '../../shared/models/api-response.model';
import { injectApi } from '../api/api-client';
import { InvoiceDetailResponse, toInvoiceDetail } from './invoice-mapping';
import { InvoiceDetail, InvoiceSummary, PaymentInfo } from './models';
import { LANDLORD_PAYMENT_INFO, TENANT_INVOICES, TENANT_NEXT_CAPTURE } from './sample-ledger';

type InvoiceResponse = components['schemas']['InvoiceResponse'];

/** What comes after an invoice: the next period's invoice if it exists, otherwise when the landlord reads the meters. */
export type NextAfter =
  | { kind: 'invoice'; invoice: InvoiceSummary }
  | { kind: 'schedule'; period: string; window: string | null };

/** "2026-11" -> "2026-12", "2026-12" -> "2027-01". */
export function nextPeriod(period: string): string {
  const [y, m] = period.split('-').map(Number);
  return m === 12 ? `${y + 1}-01` : `${y}-${String(m + 1).padStart(2, '0')}`;
}

function summaryOf({ invoiceId, period, status, total, paidAt }: InvoiceDetail | InvoiceSummary): InvoiceSummary {
  return { invoiceId, period, status, total, paidAt };
}

/** A list row (GET /invoices) as the screens' summary. */
function rowOf(i: InvoiceResponse): InvoiceSummary {
  return { invoiceId: i.invoiceId ?? '', period: i.period ?? '', status: i.status ?? 'SENT', total: i.total ?? 0, paidAt: i.paidAt ?? null };
}

export { toInvoiceDetail } from './invoice-mapping';

/** How many invoices the tenant screens load: two years of monthly invoices. */
const HISTORY_LIMIT = 24;

/**
 * The signed-in tenant's invoices: sample data by default; with environment.dataSource 'api' GET /invoices (the list) and
 * GET /invoices/{id} for the newest invoice and the disputed ones. Any other invoice is read when a screen needs it
 * (ensureDetail / ensureAllDetails), so opening the app is not one request per invoice.
 * Payment details and the meter-reading schedule are not in the API yet, so in API mode payment and nextCapture are
 * null (the screens print the period without a window).
 */
@Service()
export class TenantInvoiceStore {
  private readonly api = injectApi();
  /** Newest first. With the API: the list rows. */
  private readonly listed = signal<InvoiceSummary[]>(this.api ? [] : TENANT_INVOICES.map(summaryOf));
  /** The invoices read in full, by id. */
  private readonly loaded = signal<ReadonlyMap<string, InvoiceDetail>>(new Map(this.api ? [] : TENANT_INVOICES.map((i) => [i.invoiceId, i])));
  /** Detail reads under way (null) or failed (message), by invoice id. */
  private readonly issues = signal<ReadonlyMap<string, string | null>>(new Map());
  private readonly detailSubs = new Map<string, Subscription>();
  private loading: Subscription | null = null;

  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>(this.api ? 'idle' : 'ready');
  readonly error = signal<string | null>(null);

  /** Newest first. */
  readonly history = this.listed.asReadonly();
  readonly unpaidCount = computed(() => this.listed().filter((i) => i.status === 'SENT' || i.status === 'DISPUTED').length);
  /** The newest invoice in full; null until it has been read. */
  readonly latest = computed<InvoiceDetail | null>(() => {
    const first = this.listed()[0];
    return first ? (this.loaded().get(first.invoiceId) ?? null) : null;
  });
  readonly payment = signal<PaymentInfo | null>(this.api ? null : LANDLORD_PAYMENT_INFO);
  /** When the landlord reads the meters next: not in the API, so null in API mode. */
  readonly nextCapture: { readonly period: string; readonly window: string } | null = this.api ? null : TENANT_NEXT_CAPTURE;
  /** True with sample data: demo controls (?sample=, "Báo tôi") may show. */
  readonly sample = !this.api;
  /** Every invoice read in full so far, newest first. */
  readonly details = computed(() => this.listed().flatMap((h) => this.loaded().get(h.invoiceId) ?? []));
  /** How many invoice details are being read now. */
  readonly detailsLoading = computed(() => [...this.issues().values()].filter((v) => v === null).length);
  /** The message of the first on-demand detail read that failed, or null. */
  readonly detailsError = computed(() => [...this.issues().values()].find((v): v is string => typeof v === 'string') ?? null);
  /** True once the list is read (the details of older invoices may still be on their way). */
  readonly listReady = computed(() => this.status() === 'ready');

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.loading?.unsubscribe();
      this.detailSubs.forEach((sub) => sub.unsubscribe());
    });
    this.load();
  }

  /** (Re)loads the invoices from the API; does nothing with sample data. A load still running is cancelled. */
  load(): void {
    const api = this.api;
    if (!api) return;
    this.loading?.unsubscribe();
    this.detailSubs.forEach((sub) => sub.unsubscribe());
    this.detailSubs.clear();
    this.issues.set(new Map());
    this.status.set('loading');
    this.error.set(null);
    this.loading = api
      .get<PagedResult<InvoiceResponse>>('/invoices', { limit: HISTORY_LIMIT })
      .pipe(
        switchMap((page) => {
          const rows = page.items.map(rowOf).sort((a, b) => b.period.localeCompare(a.period));
          // The newest invoice is what the home page shows; disputed ones carry the disputes the badges count. Others on demand.
          const wanted = rows.filter((r, i) => i === 0 || r.status === 'DISPUTED');
          const known = this.loaded();
          const toRead = wanted.filter((r) => !this.isFresh(known.get(r.invoiceId), r));
          const details$ = toRead.length ? forkJoin(toRead.map((r) => api.get<InvoiceDetailResponse>(`/invoices/${r.invoiceId}`))) : of<InvoiceDetailResponse[]>([]);
          return details$.pipe(map((details) => ({ rows, known, details })));
        }),
      )
      .subscribe({
        next: ({ rows, known, details }) => {
          const read = new Map(details.map((d) => [d.invoiceId ?? '', toInvoiceDetail(d)]));
          const next = new Map<string, InvoiceDetail>();
          for (const r of rows) {
            const d = read.get(r.invoiceId) ?? (this.isFresh(known.get(r.invoiceId), r) ? known.get(r.invoiceId) : undefined);
            if (d) next.set(r.invoiceId, d);
          }
          this.listed.set(rows);
          this.loaded.set(next);
          this.status.set('ready');
        },
        error: (err: unknown) => {
          this.error.set(err instanceof Error ? err.message : 'Không tải được hóa đơn.');
          this.status.set('error');
        },
      });
  }

  /** Fetches one invoice again (after a dispute changes it) and replaces it in the list. */
  reloadInvoice(invoiceId: string): Observable<void> {
    const api = this.api;
    if (!api) return of(undefined);
    return api.get<InvoiceDetailResponse>(`/invoices/${invoiceId}`).pipe(
      map((d) => {
        const fresh = toInvoiceDetail(d);
        this.listed.update((list) => list.map((i) => (i.invoiceId === invoiceId ? summaryOf(fresh) : i)));
        this.loaded.update((m) => new Map(m).set(invoiceId, fresh));
      }),
    );
  }

  /** The invoice in full, or null when it has not been read (yet). */
  detail(invoiceId: string): InvoiceDetail | null {
    return this.loaded().get(invoiceId) ?? null;
  }

  /**
   * Where the invoice stands: 'ready' (read), 'loading', 'error', or 'missing' (not one of this tenant's invoices).
   * With sample data every invoice is ready.
   */
  detailStatus(invoiceId: string): 'ready' | 'loading' | 'error' | 'missing' {
    if (this.loaded().has(invoiceId)) return 'ready';
    if (!this.listed().some((i) => i.invoiceId === invoiceId)) return this.status() === 'ready' ? 'missing' : 'loading';
    return typeof this.issues().get(invoiceId) === 'string' ? 'error' : 'loading';
  }

  /** The message of a failed detail read. */
  detailError(invoiceId: string): string | null {
    return this.issues().get(invoiceId) ?? null;
  }

  /** API: reads GET /invoices/{id} for an invoice only known from the list, once. `retry` asks again after a failure. */
  ensureDetail(invoiceId: string, retry = false): void {
    const api = this.api;
    if (!api || this.loaded().has(invoiceId) || this.detailSubs.has(invoiceId) || !this.listed().some((i) => i.invoiceId === invoiceId)) return;
    if (!retry && typeof this.issues().get(invoiceId) === 'string') return;
    this.setIssue(invoiceId, null);
    const sub = api
      .get<InvoiceDetailResponse>(`/invoices/${invoiceId}`)
      .pipe(finalize(() => this.detailSubs.delete(invoiceId)))
      .subscribe({
        next: (d) => {
          this.loaded.update((m) => new Map(m).set(invoiceId, toInvoiceDetail(d)));
          this.setIssue(invoiceId, undefined);
        },
        error: (err: unknown) => this.setIssue(invoiceId, err instanceof Error ? err.message : 'Không tải được hóa đơn.'),
      });
    if (!sub.closed) this.detailSubs.set(invoiceId, sub);
  }

  /** Asks again for the older invoices whose read failed. */
  retryFailedDetails(): void {
    for (const [id, issue] of this.issues()) if (typeof issue === 'string') this.ensureDetail(id, true);
  }

  /** API: reads every invoice that only has its list row (the meters and disputes screens show all periods). */
  ensureAllDetails(): void {
    for (const h of this.listed()) this.ensureDetail(h.invoiceId);
  }

  nextAfter(invoiceId: string): NextAfter | null {
    const inv = this.listed().find((i) => i.invoiceId === invoiceId);
    if (!inv) return null;
    const period = nextPeriod(inv.period);
    const next = this.listed().find((i) => i.period === period);
    if (next) return { kind: 'invoice', invoice: next };
    const known = this.nextCapture;
    return { kind: 'schedule', period, window: known && period === known.period ? known.window : null };
  }

  /** The list row and the detail read earlier agree, so the detail need not be read again. */
  private isFresh(prev: InvoiceDetail | undefined, row: InvoiceSummary): boolean {
    return !!prev && prev.status === row.status && prev.total === row.total && prev.paidAt === row.paidAt;
  }

  private setIssue(id: string, issue: string | null | undefined): void {
    this.issues.update((m) => {
      const next = new Map(m);
      if (issue === undefined) next.delete(id);
      else next.set(id, issue);
      return next;
    });
  }
}
