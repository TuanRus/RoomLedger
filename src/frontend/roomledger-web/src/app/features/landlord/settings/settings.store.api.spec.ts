import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../core/api/api.config';
import { SettingsStore, settingsFromApi } from './settings.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

describe('SettingsStore with the API', () => {
  let store: SettingsStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(SettingsStore);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', electricityRate: 3800, waterRate: 20000 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(
      ok({
        items: [
          { roomId: 'b', code: 'P.102', monthlyRent: 2_400_000, status: 'VACANT', version: 4 },
          { roomId: 'a', code: 'P.101', monthlyRent: 2_500_000, status: 'OCCUPIED', version: 7 },
        ],
      }),
    );
  });

  afterEach(() => {
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('reads prices and rents from the property and has no area or start period', () => {
    expect(store.sample).toBe(false);
    const s = store.settings();
    expect(s.electricityRate).toBe(3800);
    expect(s.waterRate).toBe(20000);
    expect(s.rents.map((r) => [r.roomCode, r.monthlyRent, r.areaM2, r.since])).toEqual([
      ['P.101', 2_500_000, null, null],
      ['P.102', 2_400_000, null, null],
    ]);
    expect(store.occupiedRooms()).toBe(1);
    expect(s.payment.enabled).toBe(false);
  });

  it('saves only the rents that changed, with the room version, and keeps the new version', () => {
    let saved = -1;
    const next = { ...store.settings(), rents: store.settings().rents.map((r) => (r.roomId === 'a' ? { ...r, monthlyRent: 2_600_000 } : r)) };
    store.saveRents(next).subscribe((n) => (saved = n));
    const req = http.expectOne(`${BASE}/rooms/a`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ monthlyRent: 2_600_000, version: 7 });
    req.flush(ok({ roomId: 'a', code: 'P.101', monthlyRent: 2_600_000, version: 8 }));
    expect(saved).toBe(1);
    expect(store.settings().rents[0].monthlyRent).toBe(2_600_000);
  });

  it('shows the server message and reads the rooms again when saving fails', () => {
    const next = { ...store.settings(), rents: store.settings().rents.map((r) => (r.roomId === 'a' ? { ...r, monthlyRent: 1_000_000 } : r)) };
    store.saveRents(next).subscribe({ error: () => undefined });
    http.expectOne(`${BASE}/rooms/a`).flush({ success: false, message: 'Phòng đã được sửa ở nơi khác', errorCode: 'VERSION_CONFLICT', traceId: 't' }, { status: 409, statusText: 'Conflict' });
    expect(store.saveError()).toBe('Phòng đã được sửa ở nơi khác');
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
  });

  it('builds empty settings without a property', () => {
    expect(settingsFromApi(null, [])).toMatchObject({ electricityRate: 0, rents: [] });
  });
});
