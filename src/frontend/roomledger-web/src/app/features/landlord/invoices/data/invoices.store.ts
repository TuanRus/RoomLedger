import { DestroyRef, Service, computed, inject, signal } from '@angular/core';
import { Observable, Subscription, catchError, finalize, forkJoin, map, of, switchMap, tap } from 'rxjs';

import type { components } from '../../../../generated/api-types';
import { injectApi } from '../../../../core/api/api-client';
import { UnsavedChanges } from '../../../../core/api/unsaved-changes';
import { LandlordPropertyStore, periodBefore } from '../../../../core/data/landlord-property.store';
import { lineOf, toInvoiceDetail } from '../../../../core/data/invoice-mapping';
import { MeterKind } from '../../../../core/data/models';
import {
  INVOICE_DUE_DATE,
  LANDLORD_INVOICES,
  LandlordInvoice,
  ROOMS_WITHOUT_INVOICE,
  SAMPLE_PAID_AT,
  SAMPLE_REMINDED_AT,
  SAMPLE_SENT_AT,
  SAMPLE_TODAY,
} from './invoices.sample';

type InvoiceResponse = components['schemas']['InvoiceResponse'];
type InvoiceDetailResponse = components['schemas']['InvoiceDetailResponse'];
type GenerateInvoicesResponse = components['schemas']['GenerateInvoicesResponse'];

export type InvoiceGroup = 'draft' | 'unpaid' | 'paid' | 'overdue';

/** Seconds "đã thanh toán" can be undone. With the API the payment is only sent once this window closes. */
export const PAID_UNDO_SECONDS = 10;

const DAY_MS = 86_400_000;

/** Why a room got no draft invoice (GenerateInvoicesResponse.skipped[].reason), in the landlord's words. */
export const SKIP_REASONS: Record<string, string> = {
  MISSING_READINGS: 'chưa duyệt đủ chỉ số',
  MISSING_PREVIOUS_READING: 'thiếu chỉ số kỳ trước',
  NO_LOCKED_CONTRACT: 'hợp đồng chưa xác nhận',
};

/** Whole days between two yyyy-MM-dd dates (b − a). */
function daysBetween(a: string, b: string): number {
  return Math.round((Date.parse(b) - Date.parse(a)) / DAY_MS);
}

/** Local calendar date "YYYY-MM-DD". */
function localDay(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** InvoiceDetailResponse -> the landlord's invoice. Tenant name and room id are joined later from the room list. */
export function toLandlordInvoice(d: InvoiceDetailResponse): LandlordInvoice {
  return { ...toInvoiceDetail(d), roomId: null, tenantName: null, remindedAt: null, correction: null, detailPending: false };
}

/**
 * A list row (GET /invoices) -> the landlord's invoice before its detail is read. Rent, lines and disputes are
 * not in the list: they stay empty and `detailPending` tells the screens not to show them until GET /invoices/{id} arrives.
 */
export function toSummaryInvoice(i: InvoiceResponse): LandlordInvoice {
  return {
    invoiceId: i.invoiceId ?? '',
    roomCode: i.roomCode ?? '',
    propertyName: i.propertyName ?? '',
    period: i.period ?? '',
    status: i.status ?? 'SENT',
    roomRent: 0,
    electricity: lineOf(undefined),
    water: lineOf(undefined),
    serviceFee: 0,
    total: i.total ?? 0,
    sentAt: i.sentAt ?? null,
    paidAt: i.paidAt ?? null,
    dueDate: null,
    disputes: [],
    roomId: null,
    tenantName: null,
    remindedAt: null,
    correction: null,
    detailPending: true,
  };
}

/** The invoices the landlord works on: this period, plus every unpaid one (sent or disputed) from any period. */
export function uniqueInvoices(...lists: InvoiceResponse[][]): InvoiceResponse[] {
  const byId = new Map<string, InvoiceResponse>();
  for (const i of lists.flat()) if (i.invoiceId && !byId.has(i.invoiceId)) byId.set(i.invoiceId, i);
  return [...byId.values()];
}

/**
 * The landlord's invoices (LL-05). Sample data by default; with environment.dataSource 'api' it reads
 * GET /invoices (this period, unpaid and disputed ones) and GET /invoices/{id} for the disputed ones; the detail of
 * any other invoice is read when the screen needs it (ensureDetail), so opening the app is not one request per invoice. Sends drafts with
 * POST /invoices/{id}/send, creates drafts with POST /properties/{id}/invoices:generate and marks paid with
 * POST /invoices/{id}/paid after the undo window. Unmarking paid after that, reminders, due dates and the
 * correction record have no endpoint (API-GAPS.md).
 */
@Service()
export class LandlordInvoicesStore {
  private readonly api = injectApi();
  private readonly property = inject(LandlordPropertyStore);

  /** True with sample data: demo-only parts of the screen may show. */
  readonly sample = !this.api;
  /** The working period; follows the property store, which recomputes it whenever it reloads. */
  get period(): string {
    return this.property.period();
  }
  /** The period before, whose paid count the overview shows. */
  get previousPeriod(): string {
    return periodBefore(this.period);
  }
  /** Not in the API: null there. */
  readonly dueDate: string | null = this.api ? null : INVOICE_DUE_DATE;
  readonly today = this.api ? localDay(new Date()) : SAMPLE_TODAY;

  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>(this.api ? 'idle' : 'ready');
  readonly error = signal<string | null>(null);

  private readonly base = signal<LandlordInvoice[]>(this.api ? [] : LANDLORD_INVOICES);
  private readonly previousList = signal<InvoiceResponse[]>([]);
  /** Payments marked on screen and held for the undo window: the source of truth until they are sent, whatever a reload brings. */
  private readonly pendingPaid = new Map<string, { timer: ReturnType<typeof setTimeout>; paidAt: string; committing: boolean }>();
  /** Detail reads under way (null) or failed (message), by invoice id. */
  private readonly detailIssues = signal<ReadonlyMap<string, string | null>>(new Map());
  private readonly detailSubs = new Map<string, Subscription>();
  private loading: Subscription | null = null;

  /** With the API the room id and tenant name come from the room list (the room's current tenant). */
  readonly invoices = computed<LandlordInvoice[]>(() => {
    const list = this.base();
    if (!this.api) return list;
    const rooms = this.property.roomByCode();
    return list.map((i) => {
      const room = rooms.get(i.roomCode);
      return { ...i, roomId: room?.roomId ?? null, tenantName: room?.tenantName ?? null };
    });
  });

  /** When the last batch of drafts was sent in this session, for the LL-05d banner. */
  readonly lastBatch = signal<{ count: number; at: string } | null>(null);
  /** Result of the last "Lập hóa đơn nháp": how many drafts were created and which rooms were skipped. */
  readonly generated = signal<{ created: number; skipped: { roomCode: string; reason: string }[] } | null>(null);
  readonly generating = signal(false);
  readonly sending = signal(false);

  /** Invoices of the current period, without the overdue ones from earlier periods. */
  readonly current = computed(() => this.invoices().filter((i) => i.period === this.period));
  readonly drafts = computed(() => this.current().filter((i) => i.status === 'DRAFT'));
  readonly draftTotal = computed(() => this.drafts().reduce((sum, i) => sum + i.total, 0));

  /** Occupied rooms that have no invoice this period yet (readings not approved, or drafts not created). */
  readonly roomsWithoutInvoice = computed<string[]>(() => {
    if (!this.api) return ROOMS_WITHOUT_INVOICE;
    const invoiced = new Set(this.current().map((i) => i.roomCode));
    return this.property
      .rooms()
      .filter((r) => r.status === 'OCCUPIED' && r.code && !invoiced.has(r.code))
      .map((r) => r.code!)
      .sort((a, b) => a.localeCompare(b, 'vi', { numeric: true }));
  });

  readonly counts = computed(() => {
    const c: Record<InvoiceGroup, number> = { draft: 0, unpaid: 0, paid: 0, overdue: 0 };
    for (const i of this.invoices()) c[this.groupOf(i)]++;
    return { ...c, all: this.current().length, sent: this.current().length - c.draft };
  });

  /** Unpaid invoices the landlord can remind about (sent or disputed, any period). */
  readonly unpaid = computed(() => this.invoices().filter((i) => i.status === 'SENT' || i.status === 'DISPUTED'));

  /** How many invoices of the previous period were sent and how many of those are paid (API only). */
  readonly previousStats = computed(() => {
    const sent = this.previousList().filter((i) => i.status !== 'DRAFT');
    return { period: this.previousPeriod, paid: sent.filter((i) => i.status === 'PAID').length, total: sent.length };
  });

  constructor() {
    // Closing or reloading the tab inside the undo window would lose the payment (a root service is only
    // destroyed with the whole app, never then), so the browser asks before leaving while one is held.
    inject(UnsavedChanges).register(() => this.pendingPaid.size > 0);
    // The app itself ending (tests, hot reload) still saves them.
    inject(DestroyRef).onDestroy(() => {
      this.loading?.unsubscribe();
      this.detailSubs.forEach((sub) => sub.unsubscribe());
      this.commitAllPaid();
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
    this.detailIssues.set(new Map());
    this.status.set('loading');
    this.error.set(null);
    this.loading = this.property
      .loadedProperty()
      .pipe(
        switchMap(() =>
          forkJoin({
            current: api.getAll<InvoiceResponse>('/invoices', { period: this.period }),
            sent: api.getAll<InvoiceResponse>('/invoices', { status: 'SENT' }),
            disputed: api.getAll<InvoiceResponse>('/invoices', { status: 'DISPUTED' }),
            previous: api.getAll<InvoiceResponse>('/invoices', { period: this.previousPeriod }),
          }),
        ),
        switchMap(({ current, sent, disputed, previous }) => {
          this.previousList.set(previous);
          const wanted = uniqueInvoices(current, sent, disputed);
          const known = new Map(this.base().map((i) => [i.invoiceId, i]));
          // Disputed invoices are read now: their disputes are what the dispute screens and badge count. Others on demand.
          const toRead = wanted.filter((i) => i.status === 'DISPUTED' && !this.isFresh(known.get(i.invoiceId ?? ''), i));
          const details$ = toRead.length ? forkJoin(toRead.map((i) => api.get<InvoiceDetailResponse>(`/invoices/${i.invoiceId}`))) : of<InvoiceDetailResponse[]>([]);
          return details$.pipe(map((details) => ({ wanted, known, details })));
        }),
      )
      .subscribe({
        next: ({ wanted, known, details }) => {
          const read = new Map(details.map((d) => [d.invoiceId ?? '', toLandlordInvoice(d)]));
          const list = wanted.map((i) => {
            const id = i.invoiceId ?? '';
            const prev = known.get(id);
            return read.get(id) ?? (this.isFresh(prev, i) ? prev! : toSummaryInvoice(i));
          });
          this.base.set(this.withPendingPaid(list));
          this.status.set('ready');
        },
        error: (err: unknown) => {
          this.error.set(err instanceof Error ? err.message : 'Không tải được hóa đơn.');
          this.status.set('error');
        },
      });
  }

  groupOf(inv: LandlordInvoice): InvoiceGroup {
    if (inv.period !== this.period && inv.status !== 'PAID') return 'overdue';
    if (inv.status === 'DRAFT') return 'draft';
    if (inv.status === 'PAID') return 'paid';
    return 'unpaid';
  }

  /** Days past the due date for an unpaid invoice, 0 if not overdue (always 0 with the API: no due date). */
  overdueDays(inv: LandlordInvoice): number {
    if (inv.status === 'PAID' || inv.status === 'DRAFT' || !inv.dueDate) return 0;
    return Math.max(0, daysBetween(inv.dueDate, this.today));
  }

  /** Sample data: send the given drafts (all drafts when omitted). Returns how many were sent. */
  send(ids?: string[]): number {
    const target = new Set(ids ?? this.drafts().map((i) => i.invoiceId));
    let count = 0;
    this.base.update((list) =>
      list.map((i) => {
        if (!target.has(i.invoiceId) || i.status !== 'DRAFT') return i;
        count++;
        return { ...i, status: 'SENT', sentAt: SAMPLE_SENT_AT };
      }),
    );
    if (count > 0 && !ids) this.lastBatch.set({ count, at: SAMPLE_SENT_AT });
    return count;
  }

  /**
   * API: POST /invoices/{id}/send for each draft (all drafts when omitted). Emits how many were sent.
   * When some fail the rest stays sent, the list is reloaded and the error shows on the page.
   */
  sendByApi(ids?: string[]): Observable<number> {
    const api = this.api;
    if (!api) return of(0);
    const target = ids ?? this.drafts().map((i) => i.invoiceId);
    if (!target.length) return of(0);
    this.sending.set(true);
    return forkJoin(
      target.map((id) =>
        api.post<InvoiceResponse>(`/invoices/${id}/send`).pipe(
          map((sent) => ({ id, sent, failure: null as unknown })),
          catchError((failure: unknown) => of({ id, sent: null, failure })),
        ),
      ),
    ).pipe(
      tap((results) => {
        this.sending.set(false);
        const ok = results.filter((r) => r.sent);
        const byId = new Map(ok.map((r) => [r.id, r.sent!]));
        this.base.update((list) =>
          list.map((i) => {
            const r = byId.get(i.invoiceId);
            return r ? { ...i, status: r.status ?? 'SENT', sentAt: r.sentAt ?? null } : i;
          }),
        );
        if (!ids && ok.length) {
          const at = ok.map((r) => r.sent!.sentAt ?? '').sort().pop() ?? '';
          this.lastBatch.set({ count: ok.length, at });
        }
        const failed = results.find((r) => !r.sent);
        if (failed) {
          const reason = failed.failure instanceof Error ? failed.failure.message : 'lỗi không rõ';
          // Reload first: it clears the error, and the message has to stay on screen.
          this.load();
          this.error.set(`Gửi được ${ok.length}/${results.length} hóa đơn: ${reason}`);
        }
      }),
      map((results) => results.filter((r) => r.sent).length),
    );
  }

  /** API: POST /properties/{id}/invoices:generate for the current period, then reloads the list. */
  generate(): Observable<GenerateInvoicesResponse> {
    const api = this.api;
    const id = this.property.propertyId();
    if (!api || !id) return of({});
    this.generating.set(true);
    return api.post<GenerateInvoicesResponse>(`/properties/${id}/invoices:generate`, { period: this.period }).pipe(
      tap({
        next: (res) => {
          this.generating.set(false);
          this.generated.set({
            created: res.created?.length ?? 0,
            skipped: (res.skipped ?? [])
              .filter((s) => s.reason !== 'ALREADY_INVOICED')
              .map((s) => ({ roomCode: s.roomCode ?? '', reason: SKIP_REASONS[s.reason ?? ''] ?? 'chưa đủ dữ liệu' })),
          });
          this.load();
        },
        error: () => this.generating.set(false),
      }),
    );
  }

  /**
   * SENT → PAID. A disputed invoice cannot be marked paid (BR-24). Returns false when not allowed.
   * With the API the invoice shows as paid at once and POST /invoices/{id}/paid goes out after the undo window.
   */
  markPaid(id: string): boolean {
    const inv = this.find(id);
    if (!inv || inv.status !== 'SENT') return false;
    if (!this.api) {
      this.patch(id, { status: 'PAID', paidAt: SAMPLE_PAID_AT });
      return true;
    }
    const paidAt = new Date().toISOString();
    this.patch(id, { status: 'PAID', paidAt });
    this.pendingPaid.set(id, { timer: setTimeout(() => this.commitPaid(id), PAID_UNDO_SECONDS * 1000), paidAt, committing: false });
    return true;
  }

  /** PAID → SENT, for undo. With the API this only works inside the undo window (nothing was sent yet). */
  unmarkPaid(id: string): void {
    if (this.find(id)?.status !== 'PAID') return;
    if (this.api) {
      const pending = this.pendingPaid.get(id);
      // Once the request is on its way it cannot be taken back.
      if (!pending || pending.committing) return;
      clearTimeout(pending.timer);
      this.pendingPaid.delete(id);
    }
    this.patch(id, { status: 'SENT', paidAt: null });
  }

  /** Sample data only: the API has no reminder endpoint. */
  remind(ids: string[]): number {
    if (this.api) return 0;
    const target = new Set(ids);
    let count = 0;
    this.base.update((list) =>
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

  /** Sample data: recalculate an invoice after a dispute corrected one reading; the dispute is closed so it goes back to SENT. */
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

  /** Sample data: the dispute was answered without a change: the invoice is payable again. */
  closeDispute(id: string): void {
    if (this.find(id)?.status === 'DISPUTED') this.patch(id, { status: 'SENT' });
  }

  /** API: GET /invoices/{id}. After a dispute is answered the server has recalculated the invoice and returned it to SENT. */
  reloadInvoice(id: string): Observable<void> {
    const api = this.api;
    if (!api) return of(undefined);
    return api.get<InvoiceDetailResponse>(`/invoices/${id}`).pipe(
      tap((d) => this.replace(toLandlordInvoice(d))),
      map(() => undefined),
    );
  }

  /** Where the detail of an invoice stands: 'ready' (always, with sample data), still being read, or failed. */
  detailStatus(id: string): 'ready' | 'loading' | 'error' {
    if (!this.api || !this.base().find((i) => i.invoiceId === id)?.detailPending) return 'ready';
    // A failed list load leaves the row without detail and without a read to wait for.
    return typeof this.detailIssues().get(id) === 'string' || this.status() === 'error' ? 'error' : 'loading';
  }

  /** The message of a failed detail read, for the banner. */
  detailError(id: string): string | null {
    return this.detailIssues().get(id) ?? (this.status() === 'error' ? this.error() : null);
  }

  /** "Thử lại" on a detail that did not load: the list again when that is what failed, else just this invoice. */
  retryDetail(id: string): void {
    if (this.status() === 'error') this.load();
    else this.ensureDetail(id, true);
  }

  /** API: reads GET /invoices/{id} for an invoice only known from the list, once. `retry` asks again after a failure. */
  ensureDetail(id: string, retry = false): void {
    const api = this.api;
    if (!api || !this.base().find((i) => i.invoiceId === id)?.detailPending || this.detailSubs.has(id)) return;
    if (!retry && typeof this.detailIssues().get(id) === 'string') return;
    this.setIssue(id, null);
    const sub = api
      .get<InvoiceDetailResponse>(`/invoices/${id}`)
      .pipe(finalize(() => this.detailSubs.delete(id)))
      .subscribe({
        next: (d) => {
          this.replace(toLandlordInvoice(d));
          this.setIssue(id, undefined);
        },
        error: (err: unknown) => this.setIssue(id, err instanceof Error ? err.message : 'Không tải được hóa đơn.'),
      });
    // A read that finished at once has already cleaned up after itself.
    if (!sub.closed) this.detailSubs.set(id, sub);
  }

  /** API: reads the detail of every invoice that only has its list row (the disputes screen needs the answered ones too). */
  ensureAllDetails(): void {
    for (const i of this.base()) if (i.detailPending) this.ensureDetail(i.invoiceId);
  }

  /** How many invoice details are being read now. */
  readonly detailsLoading = computed(() => [...this.detailIssues().values()].filter((v) => v === null).length);

  find(id: string): LandlordInvoice | undefined {
    return this.invoices().find((i) => i.invoiceId === id);
  }

  private commitPaid(id: string): void {
    const api = this.api;
    const pending = this.pendingPaid.get(id);
    if (!pending || pending.committing) return;
    clearTimeout(pending.timer);
    if (!api) {
      this.pendingPaid.delete(id);
      return;
    }
    // Stays in pendingPaid until the answer is in, so a reload landing meanwhile still shows it paid.
    pending.committing = true;
    api.post<InvoiceResponse>(`/invoices/${id}/paid`).subscribe({
      next: (r) => {
        this.pendingPaid.delete(id);
        this.patch(id, { status: r.status ?? 'PAID', paidAt: r.paidAt ?? null });
      },
      error: (err: unknown) => {
        this.pendingPaid.delete(id);
        this.patch(id, { status: 'SENT', paidAt: null });
        const code = this.find(id)?.roomCode ?? '';
        this.error.set(`Chưa lưu được thanh toán ${code}: ${err instanceof Error ? err.message : 'lỗi không rõ'}`);
      },
    });
  }

  private commitAllPaid(): void {
    for (const id of [...this.pendingPaid.keys()]) this.commitPaid(id);
  }

  /** The list row and the detail read earlier agree, so the detail need not be read again. */
  private isFresh(prev: LandlordInvoice | undefined, row: InvoiceResponse): boolean {
    if (!prev || prev.detailPending || prev.total !== (row.total ?? 0)) return false;
    // A payment held for its undo window shows PAID here while the server still says SENT.
    return this.pendingPaid.has(prev.invoiceId) || (prev.status === row.status && prev.paidAt === (row.paidAt ?? null));
  }

  /** Puts payments still held in their undo window over invoices just read from the server, which do not know them yet. */
  private withPendingPaid(list: LandlordInvoice[]): LandlordInvoice[] {
    if (!this.pendingPaid.size) return list;
    return list.map((i) => {
      const pending = this.pendingPaid.get(i.invoiceId);
      return pending && i.status === 'SENT' ? { ...i, status: 'PAID', paidAt: pending.paidAt } : i;
    });
  }

  /** Puts a freshly read invoice in the list (appending it when it is new), keeping a payment held for undo. */
  private replace(fresh: LandlordInvoice): void {
    const [next] = this.withPendingPaid([fresh]);
    this.base.update((list) => (list.some((i) => i.invoiceId === next.invoiceId) ? list.map((i) => (i.invoiceId === next.invoiceId ? next : i)) : [...list, next]));
  }

  private setIssue(id: string, issue: string | null | undefined): void {
    this.detailIssues.update((m) => {
      const next = new Map(m);
      if (issue === undefined) next.delete(id);
      else next.set(id, issue);
      return next;
    });
  }

  /** Undo of remind() (sample data): puts each invoice's previous "last reminded" time back. */
  restoreReminders(previous: Record<string, string | null>): void {
    if (this.api) return;
    this.base.update((list) => list.map((i) => (i.invoiceId in previous ? { ...i, remindedAt: previous[i.invoiceId] } : i)));
  }

  /** Puts an invoice back exactly as it was, e.g. when the landlord undoes a dispute resolution (sample data). */
  restore(inv: LandlordInvoice): void {
    if (this.api) return;
    this.base.update((list) => list.map((i) => (i.invoiceId === inv.invoiceId ? inv : i)));
  }

  private patch(id: string, changes: Partial<LandlordInvoice>): void {
    this.base.update((list) => list.map((i) => (i.invoiceId === id ? { ...i, ...changes } : i)));
  }
}
