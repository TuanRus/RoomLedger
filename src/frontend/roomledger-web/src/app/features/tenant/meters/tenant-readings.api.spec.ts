import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../core/api/api.config';
import { TenantReadingsStore } from './tenant-readings';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });
const line = (id: string, previous: number, current: number) => ({ readingId: id, previous, current, usage: current - previous, rate: 3500, amount: 1, photoUrl: null });

describe('TenantReadingsStore with the API', () => {
  it('derives readings from the invoices and invents no capture time, crop or approval time', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    const store = TestBed.inject(TenantReadingsStore);
    const http = TestBed.inject(HttpTestingController);
    expect(store.nextCapture).toBeNull();
    expect(store.list('WATER')).toEqual([]);

    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [{ invoiceId: 'a' }] }));
    http.expectOne(`${BASE}/invoices/a`).flush(ok({ invoiceId: 'a', tenancyId: 't', roomCode: 'P.1', propertyName: 'N', period: '2026-11', status: 'SENT', roomRent: 1, electricity: line('e', 100, 180), water: line('w', 10, 16), serviceFee: 0, total: 1, disputes: [] }));

    expect(store.status()).toBe('ready');
    const [water] = store.list('WATER');
    expect(water).toEqual(expect.objectContaining({ value: 16, previousValue: 10, usage: 6, status: 'CONFIRMED', photoUrl: null, digitCropUrl: null, capturedAt: null, confirmedAt: null }));
    expect(store.latest().map((r) => r.kind)).toEqual(['ELECTRICITY', 'WATER']);
    http.verify();
  });
});
