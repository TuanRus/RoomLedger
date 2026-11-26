import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../core/api/api.config';
import { TenantDisputeStore, viewOf } from './tenant-dispute.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

const line = (id: string, current: number) => ({ readingId: id, previous: 100, current, usage: current - 100, rate: 3500, amount: (current - 100) * 3500, photoUrl: null });
function invoice(id: string, period: string, status: string, disputes: unknown[], waterCurrent = 160) {
  return { invoiceId: id, tenancyId: 't', roomCode: 'P.204', propertyName: 'N', period, status, roomRent: 1, electricity: line(`${id}-e`, 180), water: line(`${id}-w`, waterCurrent), serviceFee: 0, total: 5000000, sentAt: null, paidAt: null, disputes };
}

describe('TenantDisputeStore with the API', () => {
  let store: TenantDisputeStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(TenantDisputeStore);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [{ invoiceId: 'a', period: '2026-09', status: 'DISPUTED', total: 5000000 }, { invoiceId: 'b', period: '2026-10', status: 'SENT', total: 5000000 }] }));
    http.expectOne(`${BASE}/invoices/a`).flush(ok(invoice('a', '2026-09', 'DISPUTED', [
      { disputeId: 'd1', readingId: 'a-w', reason: 'Nước sai', status: 'OPEN', createdAt: '2026-10-02T01:00:00Z' },
    ])));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(invoice('b', '2026-10', 'SENT', [
      { disputeId: 'd2', readingId: 'b-e', reason: 'Điện sai', status: 'RESOLVED', resolution: 'KEPT', response: 'Giữ', createdAt: '2026-11-02T01:00:00Z', resolvedAt: '2026-11-03T01:00:00Z' },
      { disputeId: 'd3', readingId: 'b-w', reason: 'Nước cao', status: 'RESOLVED', resolution: 'CORRECTED', response: 'Đã sửa', createdAt: '2026-11-01T01:00:00Z', resolvedAt: '2026-11-03T01:00:00Z' },
    ], 150)));
  });

  afterEach(() => http.verify());

  it('derives the disputes from the invoices, newest first, and marks what the API cannot tell', () => {
    expect(store.status()).toBe('ready');
    expect(store.sample).toBe(false);
    expect(store.disputes().map((d) => d.disputeId)).toEqual(['d2', 'd3', 'd1']);
    expect(store.waitingCount()).toBe(1);

    const kept = store.byId('d2')!;
    expect(viewOf(kept)).toBe('kept');
    expect(kept).toEqual(expect.objectContaining({ subject: 'ELECTRICITY', period: '2026-10', previousValue: 100, recordedValue: 180, totalBefore: 5000000, proposedValue: null, correctedValue: null }));

    const corrected = store.byId('d3')!;
    expect(viewOf(corrected)).toBe('corrected');
    expect(corrected).toEqual(expect.objectContaining({ subject: 'WATER', correctedValue: 150, totalAfter: 5000000, recordedValue: null, totalBefore: null }));
  });

  it('reads the older invoices, whose disputes are answered ones, when the screen asks', () => {
    // Another tenant history: c is old and paid, so only its list row is known until asked for.
    store.load();
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [
      { invoiceId: 'a', period: '2026-09', status: 'DISPUTED', total: 5000000 },
      { invoiceId: 'b', period: '2026-10', status: 'SENT', total: 5000000 },
      { invoiceId: 'c', period: '2026-08', status: 'PAID', total: 5000000 },
    ] }));
    // a and b were read before and have not changed: not read again.
    http.expectNone(`${BASE}/invoices/a`);
    http.expectNone(`${BASE}/invoices/b`);
    expect(store.status()).toBe('ready');
    store.ensureAll();
    // The list stays ready: only the older invoice is still coming.
    expect(store.status()).toBe('ready');
    expect(store.loadingMore()).toBe(true);
    http.expectOne(`${BASE}/invoices/c`).flush(ok(invoice('c', '2026-08', 'PAID', [{ disputeId: 'd0', readingId: 'c-e', reason: 'Điện', status: 'RESOLVED', resolution: 'KEPT', response: 'Giữ', createdAt: '2026-09-02T01:00:00Z', resolvedAt: '2026-09-03T01:00:00Z' }])));
    expect(store.loadingMore()).toBe(false);
    expect(store.disputes().map((d) => d.disputeId)).toEqual(['d2', 'd3', 'd1', 'd0']);
  });

  it('sends a dispute, then reloads the invoice and returns the new id', () => {
    let id = '';
    store.send('b', 'b-w', '  Nước cao quá  ').subscribe((v) => (id = v));
    const post = http.expectOne(`${BASE}/invoices/b/disputes`);
    expect(post.request.body).toEqual({ readingId: 'b-w', reason: 'Nước cao quá' });
    post.flush(ok({ disputeId: 'd9', readingId: 'b-w', reason: 'Nước cao quá', status: 'OPEN', createdAt: '2026-11-25T01:00:00Z' }));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(invoice('b', '2026-10', 'DISPUTED', [
      { disputeId: 'd9', readingId: 'b-w', reason: 'Nước cao quá', status: 'OPEN', createdAt: '2026-11-25T01:00:00Z' },
    ])));
    expect(id).toBe('d9');
    expect(store.disputes().map((d) => d.disputeId)).toEqual(['d9', 'd1']);
  });

  it('passes the API error of a failed dispute on', () => {
    let msg = '';
    store.send('b', 'b-w', 'Nước cao quá').subscribe({ error: (e: Error) => (msg = e.message) });
    http.expectOne(`${BASE}/invoices/b/disputes`).flush({ success: false, message: 'Đã có khiếu nại cho chỉ số này', errorCode: 'CONFLICT', traceId: 't' }, { status: 409, statusText: 'Conflict' });
    expect(msg).toBe('Đã có khiếu nại cho chỉ số này');
  });
});
