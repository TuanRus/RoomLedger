import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../../core/api/api.config';
import { UnsavedChanges } from '../../../../core/api/unsaved-changes';
import { LandlordInvoicesStore, PAID_UNDO_SECONDS, uniqueInvoices } from './invoices.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

const line = (readingId: string, previous: number, current: number, rate: number) => ({ readingId, previous, current, usage: current - previous, rate, amount: (current - previous) * rate, photoUrl: null });
const summary = (invoiceId: string, roomCode: string, period: string, status: string, total = 3_000_000) => ({ invoiceId, roomCode, propertyName: 'Nhà trọ Hoa', period, status, total, sentAt: status === 'DRAFT' ? null : '2026-11-02T01:00:00Z', paidAt: null });
const detail = (invoiceId: string, roomCode: string, period: string, status: string, extra: Record<string, unknown> = {}) => ({
  ...summary(invoiceId, roomCode, period, status),
  roomRent: 2_500_000,
  electricity: line(`${invoiceId}-e`, 100, 200, 3500),
  water: line(`${invoiceId}-w`, 10, 20, 18000),
  serviceFee: 0,
  disputes: [],
  ...extra,
});

describe('LandlordInvoicesStore with the API', () => {
  let store: LandlordInvoicesStore;
  let http: HttpTestingController;

  const list = (params: Record<string, string>) =>
    http.expectOne((r) => r.url === `${BASE}/invoices` && Object.entries(params).every(([k, v]) => r.params.get(k) === v) && r.params.get('limit') === '100');

  function loadWithData() {
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', roomCount: 4 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(
      ok({
        items: [
          { roomId: 'r1', code: 'P.101', status: 'OCCUPIED', tenantName: 'Đặng Văn Long' },
          { roomId: 'r2', code: 'P.102', status: 'OCCUPIED', tenantName: 'Mai Thị Hồng' },
          { roomId: 'r3', code: 'P.103', status: 'OCCUPIED', tenantName: 'Cao Minh Đức' },
          { roomId: 'r4', code: 'P.104', status: 'VACANT', tenantName: null },
        ],
      }),
    );
    list({ period: '2026-10' }).flush(ok({ items: [summary('i1', 'P.101', '2026-10', 'DRAFT'), summary('i2', 'P.102', '2026-10', 'SENT')] }));
    list({ status: 'SENT' }).flush(ok({ items: [summary('i2', 'P.102', '2026-10', 'SENT'), summary('i0', 'P.103', '2026-09', 'SENT')] }));
    list({ status: 'DISPUTED' }).flush(ok({ items: [] }));
    list({ period: '2026-09' }).flush(ok({ items: [summary('i0', 'P.103', '2026-09', 'SENT'), summary('i00', 'P.101', '2026-09', 'PAID'), summary('i000', 'P.102', '2026-09', 'DRAFT')] }));
    // No invoice is disputed, so none is read in full yet.
  }

  /** A reload asks for the four lists again (the property is already known) and finds nothing. */
  function flushReload() {
    for (const params of <Record<string, string>[]>[{ period: '2026-10' }, { status: 'SENT' }, { status: 'DISPUTED' }, { period: '2026-09' }]) list(params).flush(ok({ items: [] }));
  }

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(LandlordInvoicesStore);
    http = TestBed.inject(HttpTestingController);
    loadWithData();
  });

  afterEach(() => {
    vi.useRealTimers();
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('loads this period, unpaid and previous-period invoices, and joins the tenant from the room list', () => {
    expect(store.sample).toBe(false);
    expect(store.status()).toBe('ready');
    expect(store.period).toBe('2026-10');
    expect(store.dueDate).toBeNull();
    expect(store.invoices().map((i) => [i.roomCode, i.tenantName, i.roomId])).toEqual([
      ['P.101', 'Đặng Văn Long', 'r1'],
      ['P.102', 'Mai Thị Hồng', 'r2'],
      ['P.103', 'Cao Minh Đức', 'r3'],
    ]);
    expect(store.counts()).toEqual({ draft: 1, unpaid: 1, paid: 0, overdue: 1, all: 2, sent: 1 });
    expect(store.previousStats()).toEqual({ period: '2026-09', paid: 1, total: 2 });
    // Without a due date nothing is "N days late"; the earlier unpaid invoice is overdue by period.
    expect(store.overdueDays(store.find('i0')!)).toBe(0);
    expect(store.groupOf(store.find('i0')!)).toBe('overdue');
    expect(store.draftTotal()).toBe(3_000_000);
  });

  it('reads only the disputed invoices in full at start-up, and the others when they are opened', () => {
    store.load();
    list({ period: '2026-10' }).flush(ok({ items: [summary('i1', 'P.101', '2026-10', 'DRAFT'), summary('i2', 'P.102', '2026-10', 'SENT')] }));
    list({ status: 'SENT' }).flush(ok({ items: [summary('i2', 'P.102', '2026-10', 'SENT')] }));
    list({ status: 'DISPUTED' }).flush(ok({ items: [summary('i3', 'P.103', '2026-10', 'DISPUTED')] }));
    list({ period: '2026-09' }).flush(ok({ items: [] }));
    http.expectOne(`${BASE}/invoices/i3`).flush(ok(detail('i3', 'P.103', '2026-10', 'DISPUTED', { disputes: [{ disputeId: 'd1', readingId: 'i3-e', reason: 'Sai', status: 'OPEN', createdAt: '2026-11-03T01:00:00Z' }] })));
    http.expectNone(`${BASE}/invoices/i1`);
    http.expectNone(`${BASE}/invoices/i2`);

    expect(store.find('i3')?.detailPending).toBe(false);
    expect(store.find('i3')?.disputes).toHaveLength(1);
    expect(store.find('i2')).toMatchObject({ detailPending: true, total: 3_000_000, status: 'SENT' });
    expect(store.detailStatus('i2')).toBe('loading');

    store.ensureDetail('i2');
    store.ensureDetail('i2');
    http.expectOne(`${BASE}/invoices/i2`).flush(ok(detail('i2', 'P.102', '2026-10', 'SENT')));
    expect(store.find('i2')).toMatchObject({ detailPending: false, roomRent: 2_500_000 });
    expect(store.find('i2')?.electricity.usage).toBe(100);
    expect(store.detailStatus('i2')).toBe('ready');
  });

  it('shows the message of a failed detail read and asks again only on retry', () => {
    store.ensureDetail('i2');
    http.expectOne(`${BASE}/invoices/i2`).flush({ success: false, message: 'Không tải được', errorCode: 'X', traceId: 't' }, { status: 500, statusText: 'Server Error' });
    expect(store.detailStatus('i2')).toBe('error');
    expect(store.detailError('i2')).toBe('Không tải được');
    store.ensureDetail('i2');
    store.ensureDetail('i2', true);
    http.expectOne(`${BASE}/invoices/i2`).flush(ok(detail('i2', 'P.102', '2026-10', 'SENT')));
    expect(store.detailStatus('i2')).toBe('ready');
  });

  it('still sends the payment when the list is reloaded inside the undo window', () => {
    expect(store.markPaid('i2')).toBe(true);
    // A reload brings the server's view: still SENT.
    store.load();
    list({ period: '2026-10' }).flush(ok({ items: [summary('i1', 'P.101', '2026-10', 'DRAFT'), summary('i2', 'P.102', '2026-10', 'SENT')] }));
    list({ status: 'SENT' }).flush(ok({ items: [summary('i2', 'P.102', '2026-10', 'SENT')] }));
    list({ status: 'DISPUTED' }).flush(ok({ items: [] }));
    list({ period: '2026-09' }).flush(ok({ items: [] }));
    // The payment is still shown, and can still be undone.
    expect(store.find('i2')?.status).toBe('PAID');
    http.expectNone(`${BASE}/invoices/i2/paid`);

    vi.advanceTimersByTime(PAID_UNDO_SECONDS * 1000);
    const req = http.expectOne(`${BASE}/invoices/i2/paid`);
    expect(req.request.method).toBe('POST');
    req.flush(ok({ ...summary('i2', 'P.102', '2026-10', 'PAID'), paidAt: '2026-11-25T03:00:00Z' }));
    expect(store.find('i2')).toMatchObject({ status: 'PAID', paidAt: '2026-11-25T03:00:00Z' });
  });

  it('keeps an invoice read earlier through a reload that shows no change, and drops it when it changed', () => {
    store.ensureDetail('i2');
    http.expectOne(`${BASE}/invoices/i2`).flush(ok(detail('i2', 'P.102', '2026-10', 'SENT')));
    store.load();
    list({ period: '2026-10' }).flush(ok({ items: [summary('i2', 'P.102', '2026-10', 'SENT')] }));
    list({ status: 'SENT' }).flush(ok({ items: [] }));
    list({ status: 'DISPUTED' }).flush(ok({ items: [] }));
    list({ period: '2026-09' }).flush(ok({ items: [] }));
    expect(store.find('i2')?.detailPending).toBe(false);

    store.load();
    list({ period: '2026-10' }).flush(ok({ items: [summary('i2', 'P.102', '2026-10', 'PAID')] }));
    list({ status: 'SENT' }).flush(ok({ items: [] }));
    list({ status: 'DISPUTED' }).flush(ok({ items: [] }));
    list({ period: '2026-09' }).flush(ok({ items: [] }));
    expect(store.find('i2')).toMatchObject({ detailPending: true, status: 'PAID' });
  });

  it('cancels a load that is still running when a new one starts', () => {
    store.load();
    store.load();
    const all = http.match((r) => r.url === `${BASE}/invoices`);
    expect(all.filter((r) => r.cancelled)).toHaveLength(4);
    all.filter((r) => !r.cancelled).forEach((r) => r.flush(ok({ items: [] })));
    expect(store.status()).toBe('ready');
  });

  it('keeps a payment shown, and not undoable, while its request is on the way', () => {
    store.markPaid('i2');
    vi.advanceTimersByTime(PAID_UNDO_SECONDS * 1000);
    const req = http.expectOne(`${BASE}/invoices/i2/paid`);
    store.load();
    list({ period: '2026-10' }).flush(ok({ items: [summary('i2', 'P.102', '2026-10', 'SENT')] }));
    list({ status: 'SENT' }).flush(ok({ items: [] }));
    list({ status: 'DISPUTED' }).flush(ok({ items: [] }));
    list({ period: '2026-09' }).flush(ok({ items: [] }));
    expect(store.find('i2')?.status).toBe('PAID');
    store.unmarkPaid('i2');
    expect(store.find('i2')?.status).toBe('PAID');
    req.flush(ok({ ...summary('i2', 'P.102', '2026-10', 'PAID'), paidAt: '2026-11-25T03:00:00Z' }));
    expect(store.find('i2')?.status).toBe('PAID');
  });

  it('shows an error with retry for a pending detail when the list load failed', () => {
    store.load();
    // The first list to fail fails the load; the others are cancelled.
    http.match((x) => x.url === `${BASE}/invoices`)[0].flush({ success: false, message: 'Mất mạng', errorCode: 'NETWORK', traceId: 't' }, { status: 500, statusText: 'x' });
    expect(store.status()).toBe('error');
    expect(store.detailStatus('i2')).toBe('error');
    expect(store.detailError('i2')).toBe('Mất mạng');
    store.retryDetail('i2');
    for (const r of http.match((x) => x.url === `${BASE}/invoices`)) if (!r.cancelled) r.flush(ok({ items: [] }));
    expect(store.status()).toBe('ready');
  });

  it('asks the browser to confirm leaving while a payment is held, and not afterwards', () => {
    const guard = TestBed.inject(UnsavedChanges);
    expect(guard.any()).toBe(false);
    store.markPaid('i2');
    expect(guard.any()).toBe(true);
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);

    vi.advanceTimersByTime(PAID_UNDO_SECONDS * 1000);
    http.expectOne(`${BASE}/invoices/i2/paid`).flush(ok(summary('i2', 'P.102', '2026-10', 'PAID')));
    expect(guard.any()).toBe(false);
    const later = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(later);
    expect(later.defaultPrevented).toBe(false);
  });

  it('lists occupied rooms that have no invoice this period', () => {
    expect(store.roomsWithoutInvoice()).toEqual(['P.103']);
  });

  it('sends the drafts one by one and remembers the batch', () => {
    let sent = -1;
    store.sendByApi().subscribe((n) => (sent = n));
    const req = http.expectOne(`${BASE}/invoices/i1/send`);
    expect(req.request.method).toBe('POST');
    req.flush(ok(summary('i1', 'P.101', '2026-10', 'SENT')));
    expect(sent).toBe(1);
    expect(store.find('i1')?.status).toBe('SENT');
    expect(store.lastBatch()?.count).toBe(1);
    expect(store.drafts()).toHaveLength(0);
  });

  it('keeps what was sent and reloads when one send fails', () => {
    store.sendByApi(['i1']).subscribe();
    http.expectOne(`${BASE}/invoices/i1/send`).flush({ success: false, message: 'Hóa đơn đã gửi', errorCode: 'INVALID_STATE', traceId: 't' }, { status: 409, statusText: 'Conflict' });
    expect(store.error()).toContain('Hóa đơn đã gửi');
    flushReload();
    expect(store.status()).toBe('ready');
    // The message stays on screen after the list was read again.
    expect(store.error()).toContain('Hóa đơn đã gửi');
  });

  it('marks paid on screen at once but sends only after the undo window', () => {
    expect(store.markPaid('i2')).toBe(true);
    expect(store.find('i2')?.status).toBe('PAID');
    http.expectNone(`${BASE}/invoices/i2/paid`);

    vi.advanceTimersByTime(PAID_UNDO_SECONDS * 1000);
    const req = http.expectOne(`${BASE}/invoices/i2/paid`);
    expect(req.request.method).toBe('POST');
    req.flush(ok({ ...summary('i2', 'P.102', '2026-10', 'PAID'), paidAt: '2026-11-25T03:00:00Z' }));
    expect(store.find('i2')).toMatchObject({ status: 'PAID', paidAt: '2026-11-25T03:00:00Z' });
  });

  it('sends nothing when the payment is undone inside the window, and cannot undo afterwards', () => {
    store.markPaid('i2');
    store.unmarkPaid('i2');
    expect(store.find('i2')?.status).toBe('SENT');
    vi.advanceTimersByTime(PAID_UNDO_SECONDS * 1000);
    http.expectNone(`${BASE}/invoices/i2/paid`);

    store.markPaid('i2');
    vi.advanceTimersByTime(PAID_UNDO_SECONDS * 1000);
    http.expectOne(`${BASE}/invoices/i2/paid`).flush(ok(summary('i2', 'P.102', '2026-10', 'PAID')));
    store.unmarkPaid('i2');
    expect(store.find('i2')?.status).toBe('PAID');
  });

  it('puts the invoice back and shows the message when saving the payment fails', () => {
    store.markPaid('i2');
    vi.advanceTimersByTime(PAID_UNDO_SECONDS * 1000);
    http.expectOne(`${BASE}/invoices/i2/paid`).flush({ success: false, message: 'Đang có khiếu nại', errorCode: 'INVALID_STATE', traceId: 't' }, { status: 409, statusText: 'Conflict' });
    expect(store.find('i2')?.status).toBe('SENT');
    expect(store.error()).toContain('Đang có khiếu nại');
  });

  it('does not mark a draft or a disputed invoice paid, and has no reminders', () => {
    expect(store.markPaid('i1')).toBe(false);
    expect(store.remind(['i2'])).toBe(0);
  });

  it('creates the drafts of the period and lists the rooms that were skipped', () => {
    store.generate().subscribe();
    const req = http.expectOne(`${BASE}/properties/p1/invoices:generate`);
    expect(req.request.body).toEqual({ period: '2026-10' });
    req.flush(ok({ created: [{ invoiceId: 'i9', roomCode: 'P.104', total: 1 }], skipped: [{ roomCode: 'P.103', reason: 'MISSING_READINGS' }, { roomCode: 'P.102', reason: 'ALREADY_INVOICED' }] }));
    expect(store.generated()).toEqual({ created: 1, skipped: [{ roomCode: 'P.103', reason: 'chưa duyệt đủ chỉ số' }] });
    flushReload();
  });

  it('reads one invoice again after a dispute was answered', () => {
    store.reloadInvoice('i2').subscribe();
    http.expectOne(`${BASE}/invoices/i2`).flush(ok(detail('i2', 'P.102', '2026-10', 'SENT', { total: 2_900_000 })));
    expect(store.find('i2')?.total).toBe(2_900_000);
  });

  it('removes duplicates between lists', () => {
    expect(uniqueInvoices([{ invoiceId: 'a' }, { invoiceId: 'b' }], [{ invoiceId: 'a' }]).map((i) => i.invoiceId)).toEqual(['a', 'b']);
  });
});
