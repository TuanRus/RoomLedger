import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../../core/api/api.config';
import { LandlordDisputesStore } from './disputes.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

const line = (readingId: string, previous: number, current: number, rate: number, photoUrl: string | null = null) => ({ readingId, previous, current, usage: current - previous, rate, amount: (current - previous) * rate, photoUrl });
const dispute = (disputeId: string, readingId: string, extra: Record<string, unknown> = {}) => ({
  disputeId, invoiceId: 'i1', readingId, reason: 'Nước ghi 67, đồng hồ chỉ 65', status: 'OPEN', resolution: null, response: null, createdAt: '2026-11-20T01:00:00Z', resolvedAt: null, ...extra,
});
const invoice = (disputes: unknown[], total = 3_209_000 + 36_000, status = 'DISPUTED') => ({
  invoiceId: 'i1', roomCode: 'P.204', propertyName: 'Nhà trọ Hoa', period: '2026-10', status, total, sentAt: '2026-11-02T01:00:00Z', paidAt: null,
  roomRent: 2_500_000, electricity: line('e1', 100, 150, 3500, 'https://photo/e1'), water: line('w1', 61, 67, 18000, 'https://photo/w1'), serviceFee: 0, disputes,
});

describe('LandlordDisputesStore with the API', () => {
  let store: LandlordDisputesStore;
  let http: HttpTestingController;

  const list = (params: Record<string, string>) =>
    http.expectOne((r) => r.url === `${BASE}/invoices` && Object.entries(params).every(([k, v]) => r.params.get(k) === v));

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(LandlordDisputesStore);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1' }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [{ roomId: 'r1', code: 'P.204', status: 'OCCUPIED', tenantName: 'Trần Minh Anh' }] }));
    const summary = { invoiceId: 'i1', roomCode: 'P.204', period: '2026-10', status: 'DISPUTED', total: 1 };
    list({ period: '2026-10' }).flush(ok({ items: [summary] }));
    list({ status: 'SENT' }).flush(ok({ items: [] }));
    list({ status: 'DISPUTED' }).flush(ok({ items: [summary] }));
    list({ period: '2026-09' }).flush(ok({ items: [] }));
    http.expectOne(`${BASE}/invoices/i1`).flush(ok(invoice([dispute('d1', 'w1'), dispute('d0', 'e1', { status: 'RESOLVED', resolution: 'KEPT', response: 'Giữ nguyên', resolvedAt: '2026-11-21T01:00:00Z', createdAt: '2026-11-10T01:00:00Z' })])));
  });

  afterEach(() => {
    vi.useRealTimers();
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('builds the disputes from the invoice lines and leaves out what the API does not carry', () => {
    expect(store.sample).toBe(false);
    expect(store.counts()).toEqual({ open: 1, resolved: 1 });
    expect(store.ordered().map((d) => d.disputeId)).toEqual(['d1', 'd0']);
    const d = store.find('d1')!;
    expect(d).toMatchObject({ roomCode: 'P.204', tenantName: 'Trần Minh Anh', period: '2026-10', invoiceId: 'i1' });
    expect(d.reading).toEqual({
      kind: 'WATER', previous: 61, aiValue: null, approved: 67, approvedAt: null, tenantValue: null, rate: 18000, photoUrl: 'https://photo/w1', capturedAt: null, corrected: null,
    });
    expect(store.find('d0')!.reading?.kind).toBe('ELECTRICITY');
  });

  it('previews the new total from the invoice lines', () => {
    expect(store.recalc(store.find('d1')!, 65)).toEqual({ before: 3_245_000, after: 3_209_000 });
  });

  it('corrects: PATCH with the value and a reply, then reads the recalculated invoice', () => {
    let result = '';
    store.correctByApi('d1', 65).subscribe((r) => (result = r));
    const patch = http.expectOne(`${BASE}/disputes/d1`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ resolution: 'CORRECTED', value: 65, response: 'Đã sửa chỉ số 67 → 65 m³. Hóa đơn tính lại: 3.209.000 đ.' });
    patch.flush(ok(dispute('d1', 'w1', { status: 'RESOLVED', resolution: 'CORRECTED' })));
    http.expectOne(`${BASE}/invoices/i1`).flush(
      ok({ ...invoice([dispute('d1', 'w1', { status: 'RESOLVED', resolution: 'CORRECTED', response: 'ok', resolvedAt: '2026-11-25T01:00:00Z' })], 3_209_000, 'SENT'), water: line('w1', 61, 65, 18000) }),
    );
    expect(result).toBe('ok');
    expect(store.resolving()).toBe(false);
    const d = store.find('d1')!;
    expect(d.status).toBe('RESOLVED');
    // After a correction the invoice shows the corrected number; the number before it is not known any more.
    expect(d.reading).toMatchObject({ approved: null, corrected: 65 });
  });

  it('keeps the reading with a required reply', () => {
    let first = '';
    store.keepByApi('d1', '   ').subscribe((r) => (first = r));
    expect(first).toBe('empty-reply');
    http.expectNone(`${BASE}/disputes/d1`);

    store.keepByApi('d1', ' Ảnh rõ số 67 ').subscribe();
    const patch = http.expectOne(`${BASE}/disputes/d1`);
    expect(patch.request.body).toEqual({ resolution: 'KEPT', response: 'Ảnh rõ số 67' });
    patch.flush(ok(dispute('d1', 'w1', { status: 'RESOLVED', resolution: 'KEPT' })));
    http.expectOne(`${BASE}/invoices/i1`).flush(ok(invoice([dispute('d1', 'w1', { status: 'RESOLVED', resolution: 'KEPT', response: 'Ảnh rõ số 67', resolvedAt: '2026-11-25T01:00:00Z' })], 3_245_000, 'SENT')));
    expect(store.counts()).toEqual({ open: 0, resolved: 1 });
    expect(store.find('d1')).toMatchObject({ status: 'RESOLVED', resolution: 'KEPT' });
  });

  it('refuses a value below the previous reading without asking the server', () => {
    let result = '';
    store.correctByApi('d1', 50).subscribe((r) => (result = r));
    expect(result).toBe('invalid-value');
  });

  it('shows the server message when answering fails', () => {
    let result = '';
    store.keepByApi('d1', 'Giữ nguyên').subscribe((r) => (result = r));
    http.expectOne(`${BASE}/disputes/d1`).flush({ success: false, message: 'Khiếu nại đã được trả lời', errorCode: 'INVALID_STATE', traceId: 't' }, { status: 409, statusText: 'Conflict' });
    expect(result).toBe('failed');
    expect(store.error()).toBe('Khiếu nại đã được trả lời');
    expect(store.resolving()).toBe(false);
  });
});
