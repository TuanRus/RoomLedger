import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../api/api.config';
import { LandlordPropertyStore, periodBefore, reviewPeriod } from './landlord-property.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

function setup(source: 'sample' | 'api') {
  vi.useFakeTimers();
  vi.setSystemTime(new Date(2026, 10, 25));
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: source }, { provide: API_BASE_URL, useValue: BASE }],
  });
  return { store: TestBed.inject(LandlordPropertyStore), http: TestBed.inject(HttpTestingController) };
}

describe('LandlordPropertyStore with the API', () => {
  afterEach(() => vi.useRealTimers());

  it('loads the first property and all its rooms, and joins them by id and code', () => {
    const { store, http } = setup('api');
    expect(store.status()).toBe('loading');
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', name: 'Nhà trọ Hoa', electricityRate: 3500, waterRate: 18000, roomCount: 3 }] }));
    const first = http.expectOne(`${BASE}/properties/p1/rooms?limit=100`);
    first.flush(ok({ items: [{ roomId: 'a', code: 'P.101', status: 'OCCUPIED', tenantName: 'An' }], nextCursor: 'c2' }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100&cursor=c2`).flush(
      ok({ items: [{ roomId: 'b', code: 'P.102', status: 'VACANT', tenantName: null }, { roomId: 'c', code: 'P.103', status: 'OCCUPIED', tenantName: 'Bình' }], nextCursor: null }),
    );

    expect(store.status()).toBe('ready');
    expect(store.period()).toBe('2026-10');
    expect(store.property()?.name).toBe('Nhà trọ Hoa');
    expect(store.rooms()).toHaveLength(3);
    expect(store.occupiedCount()).toBe(2);
    expect(store.vacantCount()).toBe(1);
    expect(store.tenantNameByRoomId().get('c')).toBe('Bình');
    expect(store.tenantNameByRoomId().has('b')).toBe(false);
    expect(store.roomByCode().get('P.102')?.roomId).toBe('b');
    expect(store.missing()).toBe(false);
    http.verify();
  });

  it('shares one readings request and asks again after invalidation or an error', () => {
    const { store, http } = setup('api');
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1' }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
    const url = `${BASE}/properties/p1/meter-readings?period=2026-10`;
    const seen: unknown[] = [];
    store.loadedReadings().subscribe((r) => seen.push(r));
    store.loadedReadings().subscribe((r) => seen.push(r));
    http.expectOne(url).flush(ok([{ roomId: 'a' }]));
    expect(seen).toHaveLength(2);
    store.loadedReadings().subscribe();
    http.expectNone(url);

    store.invalidateReadings();
    store.loadedReadings().subscribe();
    http.expectOne(url).flush(ok([]));

    store.invalidateReadings();
    store.loadedReadings().subscribe({ error: () => undefined });
    http.expectOne(url).flush({ success: false, message: 'x', errorCode: 'X', traceId: 't' }, { status: 500, statusText: 'Server Error' });
    store.loadedReadings().subscribe();
    http.expectOne(url).flush(ok([]));
    http.verify();
  });

  it('works out the period again when it reloads, so a session over a month end moves on', () => {
    const { store, http } = setup('api');
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [] }));
    expect(store.period()).toBe('2026-10');
    vi.setSystemTime(new Date(2026, 11, 2));
    store.load();
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [] }));
    expect(store.period()).toBe('2026-11');
  });

  it('says there is no property when the landlord has none', () => {
    const { store, http } = setup('api');
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [] }));
    expect(store.status()).toBe('ready');
    expect(store.missing()).toBe(true);
    expect(store.rooms()).toEqual([]);
    http.verify();
  });

  it('shows the error and asks again after "Thử lại"', () => {
    const { store, http } = setup('api');
    http.expectOne(`${BASE}/properties?limit=1`).flush({ success: false, message: 'Máy chủ bận', errorCode: 'INTERNAL_ERROR', traceId: 't' }, { status: 500, statusText: 'Server Error' });
    expect(store.status()).toBe('error');
    expect(store.error()).toBe('Máy chủ bận');

    store.load();
    expect(store.status()).toBe('loading');
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [] }));
    expect(store.status()).toBe('ready');
    expect(store.error()).toBeNull();
  });

  it('shares one property request between its own load and other stores', () => {
    const { store, http } = setup('api');
    let seen = 0;
    store.loadedProperty().subscribe(() => seen++);
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1' }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
    expect(seen).toBe(1);
    http.verify();
  });

  it('reloads only the rooms and keeps the server version of an updated room', () => {
    const { store, http } = setup('api');
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1' }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [{ roomId: 'a', code: 'P.101', monthlyRent: 2_000_000, version: 1 }] }));
    store.reloadRooms();
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [{ roomId: 'a', code: 'P.101', monthlyRent: 2_100_000, version: 2 }] }));
    expect(store.rooms()[0].version).toBe(2);
    store.updateRoom({ roomId: 'a', monthlyRent: 2_200_000, version: 3 });
    expect(store.rooms()[0]).toMatchObject({ code: 'P.101', monthlyRent: 2_200_000, version: 3 });
    http.verify();
  });
});

describe('LandlordPropertyStore with sample data', () => {
  afterEach(() => vi.useRealTimers());

  it('stays empty and sends nothing', () => {
    const { store, http } = setup('sample');
    expect(store.sample).toBe(true);
    expect(store.property()).toBeNull();
    expect(store.rooms()).toEqual([]);
    expect(store.missing()).toBe(false);
    expect(store.period()).toBe('2026-11');
    http.verify();
  });
});

describe('periods', () => {
  it('reviews the month before today and counts back over a year end', () => {
    expect(reviewPeriod(new Date(2026, 11, 2))).toBe('2026-11');
    expect(reviewPeriod(new Date(2027, 0, 2))).toBe('2026-12');
    expect(periodBefore('2026-11')).toBe('2026-10');
    expect(periodBefore('2026-01')).toBe('2025-12');
  });
});
