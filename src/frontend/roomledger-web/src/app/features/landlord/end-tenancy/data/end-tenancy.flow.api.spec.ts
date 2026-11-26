import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../../core/api/api.config';
import { EndTenancyApiStore, finalInvoiceOf, periodOf, readingPhase } from './end-tenancy.api.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

const END_BODY = {
  tenancyId: 't1',
  roomId: 'a',
  roomCode: 'P.101',
  tenantName: 'An',
  endDate: '2026-11-25',
  kind: 'LEAVE',
  finalPeriod: '2026-11',
  electricity: { readingId: 'e1', previous: 1000, value: 1120, status: 'CONFIRMED' },
  water: { readingId: 'w1', previous: 50, value: 56, status: 'CONFIRMED' },
  rates: { electricity: 3500, water: 20000 },
  unpaidPrevious: 4_150_000,
};

describe('EndTenancyApiStore', () => {
  let store: EndTenancyApiStore;
  let http: HttpTestingController;

  const flushList = (url: string, data: unknown) => http.match((r) => r.url === `${BASE}${url}`).forEach((r) => r.flush(ok(data)));

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(EndTenancyApiStore);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', serviceFee: 100_000 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(
      ok({
        items: [
          { roomId: 'a', code: 'P.101', monthlyRent: 3_500_000, status: 'OCCUPIED', displayStatus: 'ACTIVE' },
          { roomId: 'b', code: 'P.102', monthlyRent: 2_800_000, status: 'VACANT', displayStatus: 'VACANT' },
          { roomId: 'c', code: 'P.103', status: 'OCCUPIED', displayStatus: 'ACTIVE' },
        ],
      }),
    );
    flushList('/properties/p1/meter-readings', []);
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  const openActive = () => {
    store.open('a');
    http.expectOne(`${BASE}/rooms/a`).flush(ok({ currentTenancy: { tenancyId: 't1', status: 'ACTIVE', tenantName: 'An' } }));
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok({ tenancyId: 't1', versions: [{ number: 1, status: 'LOCKED', terms: { startDate: { value: '2026-11-01' } }, files: [] }] }));
  };

  it('opens an active tenancy: start date from the locked terms, no end state requested', () => {
    openActive();
    expect(store.status()).toBe('ready');
    expect(store.tenancyStart()).toBe('2026-11-01');
    expect(store.end()).toBeNull();
    expect(store.started()).toBe(false);
    expect(store.targets().map((r) => r.roomId)).toEqual(['b']);
  });

  it('a tenancy moving out through the old inspection has no end state (404) and is not ended here', () => {
    store.open('a');
    http.expectOne(`${BASE}/rooms/a`).flush(ok({ currentTenancy: { tenancyId: 't1', status: 'MOVING_OUT' } }));
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok({ tenancyId: 't1', versions: [] }));
    http.expectOne(`${BASE}/tenancies/t1/end`).flush({ success: false, message: 'Hợp đồng chưa được kết thúc', errorCode: 'NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(store.status()).toBe('ready');
    expect(store.end()).toBeNull();
    expect(store.tenancyStatus()).toBe('MOVING_OUT');
  });

  it('starts the end with the chosen date and kind, then reloads the rooms', () => {
    openActive();
    store.endDate.set('2026-11-25');
    store.start();
    const req = http.expectOne(`${BASE}/tenancies/t1/end`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ endDate: '2026-11-25', kind: 'LEAVE' });
    req.flush(ok({ ...END_BODY, electricity: {}, water: {}, unpaidPrevious: 0 }));
    expect(store.started()).toBe(true);
    expect(store.finalPeriod()).toBe('2026-11');
    expect(store.phases()).toEqual({ ELECTRICITY: 'missing', WATER: 'missing' });
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
  });

  it('shows the server message when starting fails', () => {
    openActive();
    store.endDate.set('2026-10-01');
    store.start();
    http.expectOne(`${BASE}/tenancies/t1/end`).flush({ success: false, message: 'Ngày kết thúc không được trước ngày bắt đầu hợp đồng', errorCode: 'VALIDATION_ERROR' }, { status: 400, statusText: 'Bad Request' });
    expect(store.actionError()).toBe('Ngày kết thúc không được trước ngày bắt đầu hợp đồng');
    expect(store.busy()).toBe(false);
  });

  it('approves a final reading with PATCH and reads the end state again', () => {
    openActive();
    store.end.set({ ...END_BODY, electricity: { readingId: 'e1', previous: 1000, value: 1120, status: 'PENDING_CONFIRM' } } as never);
    store.approve('ELECTRICITY', 900);
    expect(store.actionError()).toContain('không được nhỏ hơn');
    store.approve('ELECTRICITY', 1120);
    const patch = http.expectOne(`${BASE}/meter-readings/e1`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ value: 1120, confirm: true });
    patch.flush(ok({ readingId: 'e1', status: 'CONFIRMED' }));
    http.expectOne(`${BASE}/tenancies/t1/end`).flush(ok(END_BODY));
    expect(store.phases().ELECTRICITY).toBe('approved');
    expect(store.readingsApproved()).toBe(true);
  });

  it('blocks finishing until the rent (and, for a move, the new room readings) is typed, then finishes', () => {
    openActive();
    store.end.set(END_BODY as never);
    expect(store.blockers()).toEqual(['chưa nhập tiền phòng tháng cuối']);
    store.finish();
    http.expectNone(`${BASE}/tenancies/t1/end:finish`);

    store.lastRent.set(2_000_000);
    expect(store.blockers()).toEqual([]);
    store.finish();
    const req = http.expectOne(`${BASE}/tenancies/t1/end:finish`);
    expect(req.request.body).toEqual({ lastRent: 2_000_000 });
    req.flush(ok({ ...END_BODY, lastRent: 2_000_000, finalInvoiceId: 'i1', endedAt: '2026-11-25T03:00:00Z' }));
    expect(store.finished()).toBe(true);
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
    http.match((r) => r.url === `${BASE}/rooms/a`).forEach((r) => r.flush(ok({})));
  });

  it('a move also sends the starting readings of the new room', () => {
    openActive();
    store.end.set({ ...END_BODY, kind: 'MOVE', targetRoom: { roomId: 'b', code: 'P.102', monthlyRent: 2_800_000 } } as never);
    store.lastRent.set(1_000_000);
    expect(store.blockers()).toEqual(['chưa nhập chỉ số điện đầu ở phòng mới', 'chưa nhập chỉ số nước đầu ở phòng mới']);
    store.startElectricity.set(10);
    store.startWater.set(2);
    store.finish();
    const req = http.expectOne(`${BASE}/tenancies/t1/end:finish`);
    expect(req.request.body).toEqual({ lastRent: 1_000_000, startElectricity: 10, startWater: 2 });
    req.flush(ok({ ...END_BODY, kind: 'MOVE', endedAt: '2026-11-25T03:00:00Z', newTenancyId: 't2' }));
    http.match(() => true).forEach((r) => r.flush(ok({ items: [] })));
  });

  it('move targets leave out a vacant room that already has a draft tenancy', () => {
    openActive();
    expect(store.targets().map((r) => r.roomId)).toEqual(['b']);
    http.expectOne(`${BASE}/rooms/b`).flush(ok({ roomId: 'b', currentTenancy: { tenancyId: 'draft1', status: 'DRAFT' } }));
    expect(store.targets()).toEqual([]);
  });

  it('a meter marked as replaced is approved with meterReplaced and may read lower than before', () => {
    openActive();
    store.end.set({ ...END_BODY, electricity: { readingId: 'e1', previous: 1000, value: 5, status: 'PENDING_CONFIRM' } } as never);
    store.approve('ELECTRICITY', 5, true);
    const patch = http.expectOne(`${BASE}/meter-readings/e1`);
    expect(patch.request.body).toEqual({ value: 5, meterReplaced: true, confirm: true });
    patch.flush(ok({ readingId: 'e1', status: 'CONFIRMED' }));
    http.match(() => true).forEach((r) => r.flush(ok(END_BODY)));
  });

  it('a missing previous reading blocks finishing with a clear reason, unless the meter is replaced', () => {
    openActive();
    store.end.set({ ...END_BODY, electricity: { readingId: 'e1', previous: null, value: 1120, status: 'CONFIRMED' } } as never);
    store.lastRent.set(1);
    expect(store.missingPrevious('ELECTRICITY')).toBe(true);
    expect(store.blockers()[0]).toContain('chưa có chỉ số điện kỳ trước');
    store.replacedNow.set({ ELECTRICITY: true });
    expect(store.blockers()).toEqual([]);
  });

  it('drops the request of the previous room when another room is opened', () => {
    store.open('a');
    const first = http.expectOne(`${BASE}/rooms/a`);
    store.open('c');
    http.expectOne(`${BASE}/rooms/c`);
    expect(store.roomId()).toBe('c');
    expect(first.cancelled).toBe(true);
    expect(store.tenancyId()).toBeNull();
  });

  it('shows the deposit and what is returned or still owed: deposit − (final invoice + unpaid previous)', () => {
    const inv = finalInvoiceOf({ ...END_BODY, deposit: 7_000_000 } as never, 2_000_000, 100_000);
    expect(inv.deposit).toBe(7_000_000);
    expect(inv.balance).toBe(7_000_000 - 6_790_000);
    const owed = finalInvoiceOf({ ...END_BODY, deposit: 1_000_000 } as never, 2_000_000, 100_000);
    expect(owed.balance).toBe(-5_790_000);
    expect(finalInvoiceOf({ ...END_BODY, deposit: 1 } as never, null, 0).balance).toBeNull();
  });

  it('a replaced meter counts its previous reading as 0 in the preview', () => {
    const inv = finalInvoiceOf({ ...END_BODY, electricity: { readingId: 'e1', previous: 1000, value: 20, status: 'CONFIRMED', meterReplaced: true } } as never, 0, 0);
    expect(inv.electricity).toEqual({ usage: 20, rate: 3500, amount: 70_000 });
  });

  it('cancels with DELETE and opens the room again as an active tenancy', () => {
    openActive();
    store.end.set(END_BODY as never);
    store.cancel();
    const del = http.expectOne(`${BASE}/tenancies/t1/end`);
    expect(del.request.method).toBe('DELETE');
    del.flush(ok({ tenancyId: 't1', status: 'ACTIVE' }));
    expect(store.end()).toBeNull();
    http.match(() => true).forEach((r) => r.flush(ok({ items: [], currentTenancy: { tenancyId: 't1', status: 'ACTIVE' } })));
  });
});

describe('final invoice preview', () => {
  it('rent is what the landlord typed; unpaid previous stays apart and is added only to the amount to collect', () => {
    const inv = finalInvoiceOf(END_BODY as never, 2_000_000, 100_000);
    expect(inv.electricity).toEqual({ usage: 120, rate: 3500, amount: 420_000 });
    expect(inv.water).toEqual({ usage: 6, rate: 20000, amount: 120_000 });
    expect(inv.invoiceTotal).toBe(2_640_000);
    expect(inv.unpaidPrevious).toBe(4_150_000);
    expect(inv.toCollect).toBe(6_790_000);
  });

  it('has no total until the rent is typed and both readings are approved', () => {
    expect(finalInvoiceOf(END_BODY as never, null, 100_000).invoiceTotal).toBeNull();
    expect(finalInvoiceOf({ ...END_BODY, water: { readingId: 'w1', previous: 50, value: 56, status: 'PENDING_CONFIRM' } } as never, 1, 0).toCollect).toBeNull();
  });

  it('reading phases: missing, AI pending, waiting for approval, approved', () => {
    expect(readingPhase({}, undefined)).toBe('missing');
    expect(readingPhase({ readingId: 'r', status: 'PENDING_AI' }, undefined)).toBe('reading');
    expect(readingPhase({ readingId: 'r', status: 'PENDING_CONFIRM', value: 5 }, undefined)).toBe('pending');
    expect(readingPhase({ readingId: 'r', status: 'CONFIRMED', value: 5 }, undefined)).toBe('approved');
    expect(readingPhase({}, { phase: 'uploading', progress: 10, error: null, file: new File([], 'a.jpg'), aiTimedOut: false })).toBe('uploading');
  });

  it('period of a date', () => {
    expect(periodOf('2026-11-25')).toBe('2026-11');
  });
});
