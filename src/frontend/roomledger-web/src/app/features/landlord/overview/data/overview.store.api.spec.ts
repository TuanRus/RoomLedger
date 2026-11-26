import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../../core/api/api.config';
import { OverviewStore, contractsNeedingAction, overdueOf } from './overview.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

describe('OverviewStore with the API', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('shows no sample figures: the name and counts come from the other stores, the rest is empty', () => {
    const store = TestBed.inject(OverviewStore);
    expect(store.status()).toBe('loading');
    http.expectOne(`${BASE}/me`).flush(ok({ fullName: 'Cô Lan', role: 'LANDLORD' }));
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [] }));
    for (const r of http.match((x) => x.url === `${BASE}/invoices`)) r.flush(ok({ items: [] }));

    expect(store.status()).toBe('ready');
    const o = store.overview();
    expect(store.sample).toBe(false);
    expect(o.landlordName).toBe('Cô Lan');
    expect(o.period).toBe('2026-10');
    expect(o.captureWindow).toBeNull();
    expect(o.paymentDue).toBeNull();
    expect(o.capture).toEqual({ recorded: 0, total: 0, deadline: null });
    expect(o.overdue).toEqual([]);
    expect(o.summary.lastPeriod).toBeNull();
    expect(o.summary.rooms).toEqual({ total: 0, occupied: 0, vacant: 0 });
    expect(o.summary.contracts.needAction).toBe(0);
    expect(store.overdueTotal()).toBe(0);
    expect(store.capturePercent()).toBe(0);
  });

  it('lists unpaid invoices of earlier periods without days late', () => {
    const inv = (roomCode: string, period: string, status: string, total: number) => ({ roomCode, period, status, total }) as never;
    expect(overdueOf([inv('P.102', '2026-09', 'SENT', 3), inv('P.101', '2026-09', 'DISPUTED', 2), inv('P.103', '2026-10', 'SENT', 5), inv('P.104', '2026-09', 'PAID', 7)], '2026-10')).toEqual([
      { roomCode: 'P.101', period: '2026-09', amount: 2, daysLate: null },
      { roomCode: 'P.102', period: '2026-09', amount: 3, daysLate: null },
    ]);
  });

  it('counts the rooms whose contract waits for the tenant, from the room list alone', () => {
    const room = (code: string, displayStatus: string) => ({ code, displayStatus }) as never;
    expect(contractsNeedingAction([room('P.105', 'AWAITING_CONFIRM'), room('P.106', 'ACTIVE'), room('P.107', 'VACANT')])).toEqual({
      needAction: 1,
      note: 'P.105 chờ người thuê xác nhận',
    });
    expect(contractsNeedingAction([]).needAction).toBe(0);
  });

  it('does not read each room and tenancy for the contract count', () => {
    const store = TestBed.inject(OverviewStore);
    http.expectOne(`${BASE}/me`).flush(ok({ fullName: 'Cô Lan', role: 'LANDLORD' }));
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1' }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(
      ok({ items: [{ roomId: 'r1', code: 'P.101', status: 'OCCUPIED', displayStatus: 'AWAITING_CONFIRM' }, { roomId: 'r2', code: 'P.102', status: 'VACANT', displayStatus: 'VACANT' }] }),
    );
    for (const r of http.match((x) => x.url === `${BASE}/invoices` || x.url.includes('/meter-readings'))) r.flush(ok(r.request.url.includes('/meter-readings') ? [] : { items: [] }));
    http.expectNone((x) => x.url.includes('/rooms/r') || x.url.includes('/tenancies/'));
    expect(store.overview().summary.contracts).toEqual({ needAction: 1, note: 'P.101 chờ người thuê xác nhận' });
  });
});
