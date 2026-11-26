import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../core/api/api.config';
import type { components } from '../../../generated/api-types';
import { TenantContractStore, toTenantContract } from './tenant-contract.store';

type TenancyDetailResponse = components['schemas']['TenancyDetailResponse'];
const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

const terms = {
  monthlyRent: { value: 3500000 },
  deposit: { value: 7000000 },
  startDate: { value: '2026-08-01' },
  endDate: { value: '2027-08-01' },
  electricityRate: { value: 3500 },
  waterRate: { value: 18000 },
};
const awaiting = {
  versionId: 'v2', number: 2, type: 'ADDENDUM', status: 'AWAITING_CONFIRM', terms,
  files: [
    { fileId: 'f1', fileName: 'p1.jpg', contentType: 'image/jpeg', size: 1, url: 'https://files.test/p1.jpg' },
    { fileId: 'f2', fileName: 'p2.pdf', contentType: 'application/pdf', size: 1, url: 'https://files.test/p2.pdf' },
  ],
};
const locked = { versionId: 'v1', number: 1, type: 'INITIAL', status: 'LOCKED', terms, tenantConfirmedAt: '2026-08-01T03:00:00+00:00', files: [] };
const tenancy = (versions: unknown[]) => ({ tenancyId: 't1', roomCode: 'P.204', propertyName: 'Nhà trọ', status: 'ACTIVE', versions }) as TenancyDetailResponse;

describe('toTenantContract', () => {
  it('shows the version waiting for the tenant, with terms, dates and the first image', () => {
    const r = toTenantContract(tenancy([awaiting, locked]));
    expect(r?.versionNumber).toBe(2);
    expect(r?.decision).toEqual({ status: 'AWAITING_CONFIRM' });
    expect(r?.contract).toEqual({
      roomCode: 'P.204', propertyName: 'Nhà trọ', scanUrl: 'https://files.test/p1.jpg', pages: 2,
      monthlyRent: 3500000, deposit: 7000000, startDate: '01/08/2026', endDate: '01/08/2027', electricityRate: 3500, waterRate: 18000,
    });
  });

  it('falls back to the locked version, and reads a rejection', () => {
    expect(toTenantContract(tenancy([locked]))?.decision).toEqual({ status: 'CONFIRMED', at: '01/08/2026' });
    expect(toTenantContract(tenancy([{ ...awaiting, status: 'REJECTED', rejectReason: 'Sai cọc' }]))?.decision).toEqual({ status: 'REJECTED', at: null, reason: 'Sai cọc' });
    expect(toTenantContract(tenancy([]))).toBeNull();
  });
});

describe('TenantContractStore with the API', () => {
  function setup(home: unknown) {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    const store = TestBed.inject(TenantContractStore);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/tenant/home`).flush(ok(home));
    TestBed.tick();
    return { store, http };
  }

  it('loads the tenancy of the room and starts with no contract text', () => {
    const { store, http } = setup({ tenancyId: 't1' });
    expect(store.status()).toBe('loading');
    expect(store.contract()).toBeNull();
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([awaiting])));
    expect(store.status()).toBe('ready');
    expect(store.contract()?.monthlyRent).toBe(3500000);
    expect(store.sample).toBe(false);
    http.verify();
  });

  it('has no contract for an account without a room', () => {
    const { store, http } = setup({ tenancyId: null });
    expect(store.status()).toBe('ready');
    expect(store.contract()).toBeNull();
    expect(store.decision().status).toBe('NONE');
    http.verify();
  });

  it('confirms through the API and takes the answer from the response', () => {
    const { store, http } = setup({ tenancyId: 't1' });
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([awaiting])));
    store.confirm();
    expect(store.busy()).toBe(true);
    const req = http.expectOne(`${BASE}/tenancies/t1/versions/2/confirm`);
    expect(req.request.body).toEqual({ decision: 'CONFIRM' });
    req.flush(ok({ ...awaiting, status: 'LOCKED', tenantConfirmedAt: '2026-11-25T01:00:00+00:00' }));
    expect(store.decision()).toEqual({ status: 'CONFIRMED', at: '25/11/2026' });
    expect(store.busy()).toBe(false);
    http.verify();
  });

  it('sends a rejection with its reason, refuses a short one, and shows a failure', () => {
    const { store, http } = setup({ tenancyId: 't1' });
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([awaiting])));
    expect(store.reject('ngắn')).toBe(false);
    http.expectNone(`${BASE}/tenancies/t1/versions/2/confirm`);

    expect(store.reject('  Tiền cọc sai số  ')).toBe(true);
    const req = http.expectOne(`${BASE}/tenancies/t1/versions/2/confirm`);
    expect(req.request.body).toEqual({ decision: 'REJECT', reason: 'Tiền cọc sai số' });
    req.flush({ success: false, message: 'Hợp đồng đã khóa', errorCode: 'RECORD_LOCKED', traceId: 't' }, { status: 409, statusText: 'Conflict' });
    expect(store.actionError()).toBe('Hợp đồng đã khóa');
    expect(store.decision().status).toBe('AWAITING_CONFIRM');
    http.verify();
  });

  it('shows the error when the tenancy does not load, and retries', () => {
    const { store, http } = setup({ tenancyId: 't1' });
    http.expectOne(`${BASE}/tenancies/t1`).flush({ success: false, message: 'Không tìm thấy', errorCode: 'NOT_FOUND', traceId: 't' }, { status: 404, statusText: 'Not Found' });
    expect(store.status()).toBe('error');
    expect(store.error()).toBe('Không tìm thấy');
    store.load();
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([locked])));
    expect(store.status()).toBe('ready');
    http.verify();
  });
});
