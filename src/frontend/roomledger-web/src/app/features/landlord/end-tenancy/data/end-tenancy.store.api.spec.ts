import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../../core/api/api.config';
import { EndTenancyStore } from './end-tenancy.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

describe('EndTenancyStore with the API', () => {
  let store: EndTenancyStore;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(EndTenancyStore);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1' }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(
      ok({ items: [{ roomId: 'a', code: 'P.101', monthlyRent: 2_500_000, status: 'OCCUPIED', tenantName: 'An' }, { roomId: 'b', code: 'P.102', status: 'VACANT' }] }),
    );
    http.expectOne(`${BASE}/properties/p1/meter-readings?period=2026-10`).flush(ok([]));
  });

  afterEach(() => {
    vi.useRealTimers();
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('only knows the room and its tenant: no draft, no move targets, nothing can be finished', () => {
    expect(store.sample).toBe(false);
    expect(store.header('a')).toEqual({ roomId: 'a', roomCode: 'P.101', tenantName: 'An', tenancyStart: null, monthlyRent: 2_500_000 });
    expect(store.header('b')).toBeNull();
    expect(store.draft('a')).toBeNull();
    expect(store.targets()).toEqual([]);
    expect(store.target('r-302')).toBeNull();
    expect(store.finish('a')).toBe(false);
  });

  it('takes the start of the tenancy from the room detail', () => {
    store.loadHeader('a');
    http.expectOne(`${BASE}/rooms/a`).flush(ok({ currentTenancy: { tenancyId: 't1' } }));
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok({ tenancyId: 't1', versions: [{ number: 1, status: 'LOCKED', terms: { startDate: { value: '2026-03-01' } }, files: [] }] }));
    expect(store.header('a')?.tenancyStart).toBe('2026-03-01');
  });
});
