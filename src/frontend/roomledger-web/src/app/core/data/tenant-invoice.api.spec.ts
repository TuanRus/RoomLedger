import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../api/api.config';
import { TenantInvoiceStore } from './tenant-invoice.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

function detail(id: string, period: string, status: string) {
  const line = { readingId: `${id}-r`, previous: 100, current: 160, usage: 60, rate: 3500, amount: 210000, photoUrl: null };
  return { invoiceId: id, tenancyId: 't', roomCode: 'P.204', propertyName: 'Nhà trọ', period, status, roomRent: 3500000, electricity: line, water: { ...line, rate: 20000, amount: 1200000 }, serviceFee: 100000, total: 5010000, sentAt: '2026-11-02T01:00:00Z', paidAt: null, disputes: [] };
}
const row = (id: string, period: string, status: string) => ({ invoiceId: id, period, status, total: 5010000, paidAt: null });

describe('TenantInvoiceStore with the API', () => {
  let store: TenantInvoiceStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(TenantInvoiceStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the list and reads only the newest invoice in full', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-09', 'PAID'), row('b', '2026-10', 'SENT'), row('c', '2026-08', 'PAID')] }));
    // One detail, not one per invoice.
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('b', '2026-10', 'SENT')));
    http.expectNone(`${BASE}/invoices/a`);
    http.expectNone(`${BASE}/invoices/c`);

    expect(store.status()).toBe('ready');
    expect(store.history().map((h) => h.period)).toEqual(['2026-10', '2026-09', '2026-08']);
    expect(store.unpaidCount()).toBe(1);
    expect(store.latest()?.invoiceId).toBe('b');
    expect(store.detail('b')?.electricity.usage).toBe(60);
    expect(store.detail('b')?.dueDate).toBeNull();
    expect(store.detail('a')).toBeNull();
    expect(store.payment()).toBeNull();
  });

  it('reads an older invoice once, when a screen asks for it', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-09', 'PAID'), row('b', '2026-10', 'SENT')] }));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('b', '2026-10', 'SENT')));
    expect(store.detailStatus('a')).toBe('loading');

    store.ensureDetail('a');
    store.ensureDetail('a');
    http.expectOne(`${BASE}/invoices/a`).flush(ok(detail('a', '2026-09', 'PAID')));
    expect(store.detail('a')?.period).toBe('2026-09');
    expect(store.detailStatus('a')).toBe('ready');

    // Already read: no request. Not this tenant's invoice: no request either.
    store.ensureDetail('a');
    store.ensureDetail('zzz');
    expect(store.detailStatus('zzz')).toBe('missing');
  });

  it('shows the message of a failed detail read and asks again on retry', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-09', 'PAID'), row('b', '2026-10', 'SENT')] }));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('b', '2026-10', 'SENT')));

    store.ensureDetail('a');
    http.expectOne(`${BASE}/invoices/a`).flush({ success: false, message: 'Không có quyền xem hóa đơn', errorCode: 'FORBIDDEN', traceId: 't' }, { status: 403, statusText: 'Forbidden' });
    expect(store.detailStatus('a')).toBe('error');
    expect(store.detailError('a')).toBe('Không có quyền xem hóa đơn');
    // No retry loop: another ensureDetail does nothing until the tenant asks again.
    store.ensureDetail('a');
    store.ensureDetail('a', true);
    http.expectOne(`${BASE}/invoices/a`).flush(ok(detail('a', '2026-09', 'PAID')));
    expect(store.detailStatus('a')).toBe('ready');
  });

  it('reads the disputed invoices at start-up, whatever their age', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-09', 'DISPUTED'), row('b', '2026-10', 'SENT'), row('c', '2026-08', 'PAID')] }));
    http.expectOne(`${BASE}/invoices/a`).flush(ok(detail('a', '2026-09', 'DISPUTED')));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('b', '2026-10', 'SENT')));
    expect(store.details().map((d) => d.invoiceId)).toEqual(['b', 'a']);
    expect(store.unpaidCount()).toBe(2);
  });

  it('keeps the screen usable while older periods load, and reports one that failed without failing the rest', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-09', 'PAID'), row('b', '2026-10', 'SENT')] }));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('b', '2026-10', 'SENT')));
    store.ensureAllDetails();
    // The list is ready all along; only the older periods are still coming.
    expect(store.status()).toBe('ready');
    expect(store.detailsLoading()).toBe(1);
    http.expectOne(`${BASE}/invoices/a`).flush({ success: false, message: 'Lỗi tạm', errorCode: 'X', traceId: 't' }, { status: 500, statusText: 'Server Error' });
    expect(store.status()).toBe('ready');
    expect(store.detailsLoading()).toBe(0);
    expect(store.detailsError()).toBe('Lỗi tạm');
    expect(store.details().map((d) => d.invoiceId)).toEqual(['b']);

    store.retryFailedDetails();
    http.expectOne(`${BASE}/invoices/a`).flush(ok(detail('a', '2026-09', 'PAID')));
    expect(store.detailsError()).toBeNull();
    expect(store.details().map((d) => d.invoiceId)).toEqual(['b', 'a']);
  });

  it('shows the API error message', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(
      { success: false, message: 'Bạn cần đăng nhập', errorCode: 'UNAUTHORIZED', traceId: 't' },
      { status: 401, statusText: 'Unauthorized' },
    );
    expect(store.status()).toBe('error');
    expect(store.error()).toBe('Bạn cần đăng nhập');
  });

  it('has no meter schedule in API mode: nextCapture is null and the next period has no window', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-12', 'PAID')] }));
    http.expectOne(`${BASE}/invoices/a`).flush(ok(detail('a', '2026-12', 'PAID')));
    expect(store.nextCapture).toBeNull();
    expect(store.sample).toBe(false);
    expect(store.nextAfter('a')).toEqual({ kind: 'schedule', period: '2027-01', window: null });
  });

  it('points to the next period invoice from the list alone, without reading it', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-09', 'PAID'), row('b', '2026-10', 'SENT')] }));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('b', '2026-10', 'SENT')));
    expect(store.nextAfter('a')).toEqual({ kind: 'invoice', invoice: expect.objectContaining({ invoiceId: 'b', period: '2026-10' }) });
  });

  it('reloads one invoice with its disputes', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-10', 'SENT')] }));
    http.expectOne(`${BASE}/invoices/a`).flush(ok(detail('a', '2026-10', 'SENT')));
    expect(store.detail('a')?.disputes).toEqual([]);

    store.reloadInvoice('a').subscribe();
    http.expectOne(`${BASE}/invoices/a`).flush(
      ok({ ...detail('a', '2026-10', 'DISPUTED'), disputes: [{ disputeId: 'd1', readingId: 'a-r', reason: 'Sai số', status: 'OPEN', createdAt: '2026-11-03T01:00:00Z' }] }),
    );
    expect(store.detail('a')?.status).toBe('DISPUTED');
    expect(store.detail('a')?.disputes?.[0]).toEqual(expect.objectContaining({ disputeId: 'd1', status: 'OPEN', resolution: null }));
    expect(store.history()[0].status).toBe('DISPUTED');
    expect(store.unpaidCount()).toBe(1);
  });

  it('cancels a load that is still running when a new one starts', () => {
    store.load();
    const [first, second] = http.match(`${BASE}/invoices?limit=24`);
    expect(first.cancelled).toBe(true);
    second.flush(ok({ items: [row('a', '2026-10', 'SENT')] }));
    http.expectOne(`${BASE}/invoices/a`).flush(ok(detail('a', '2026-10', 'SENT')));
    expect(store.status()).toBe('ready');
    expect(store.history().map((h) => h.invoiceId)).toEqual(['a']);
  });

  it('keeps an invoice read earlier when the list says it has not changed', () => {
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-09', 'PAID'), row('b', '2026-10', 'SENT')] }));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('b', '2026-10', 'SENT')));
    store.load();
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [row('a', '2026-09', 'PAID'), row('b', '2026-10', 'SENT')] }));
    http.expectNone(`${BASE}/invoices/b`);
    expect(store.detail('b')).not.toBeNull();
  });
});
