import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../api/api.config';
import { TenantHomeStore } from './tenant-home.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

function setup(source: 'sample' | 'api') {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: source }, { provide: API_BASE_URL, useValue: BASE }],
  });
  return { store: TestBed.inject(TenantHomeStore), http: TestBed.inject(HttpTestingController) };
}

describe('TenantHomeStore with the API', () => {
  it('starts empty, then shows the room from /tenant/home', () => {
    const { store, http } = setup('api');
    expect(store.status()).toBe('loading');
    expect(store.roomCode()).toBe('');
    expect(store.sample).toBe(false);

    http.expectOne(`${BASE}/tenant/home`).flush(ok({ tenancyId: 't1', roomCode: 'P.301', propertyName: 'Nhà trọ Hoa', status: 'ACTIVE' }));

    expect(store.status()).toBe('ready');
    expect(store.tenancyId()).toBe('t1');
    expect(store.roomCode()).toBe('P.301');
    expect(store.propertyName()).toBe('Nhà trọ Hoa');
    expect(store.movingOut()).toBe(false);
    http.verify();
  });

  it('has no tenancy for an unassigned account, and knows a move-out', () => {
    const { store, http } = setup('api');
    http.expectOne(`${BASE}/tenant/home`).flush(ok({ tenancyId: null }));
    expect(store.tenancyId()).toBeNull();

    store.reload();
    http.expectOne(`${BASE}/tenant/home`).flush(ok({ tenancyId: 't1', roomCode: 'P.1', status: 'MOVING_OUT' }));
    expect(store.movingOut()).toBe(true);
    http.verify();
  });

  it('shows the API error and retries', () => {
    const { store, http } = setup('api');
    http.expectOne(`${BASE}/tenant/home`).flush({ success: false, message: 'Máy chủ bận', errorCode: 'X', traceId: 't' }, { status: 500, statusText: 'Server Error' });
    expect(store.status()).toBe('error');
    expect(store.error()).toBe('Máy chủ bận');

    store.reload();
    expect(store.status()).toBe('loading');
    http.expectOne(`${BASE}/tenant/home`).flush(ok({ tenancyId: 't1', roomCode: 'P.1' }));
    expect(store.status()).toBe('ready');
    expect(store.error()).toBeNull();
    http.verify();
  });
});

describe('TenantHomeStore with sample data', () => {
  it('keeps the sample room and makes no request', () => {
    const { store, http } = setup('sample');
    expect(store.sample).toBe(true);
    expect(store.status()).toBe('ready');
    expect(store.roomCode()).toBe('P.204');
    http.verify();
  });
});
