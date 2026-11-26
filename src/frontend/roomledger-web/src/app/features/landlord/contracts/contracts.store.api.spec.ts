import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../core/api/api.config';
import { contractChip, hasNoTenancy, latest } from './contracts.model';
import { ContractsStore, emptyContract, toContract } from './contracts.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

const term = (value: unknown) => ({ value });
const file = (id: string) => ({ fileId: id, fileName: `${id}.jpg`, contentType: 'image/jpeg', size: 1000, status: 'READY', url: `https://s3.test/${id}?sig=1` });
const tenancy = (versions: unknown[], extra: Record<string, unknown> = {}) => ({
  tenancyId: 't1', roomId: 'r1', roomCode: 'P.101', propertyName: 'Nhà trọ Hoa', status: 'AWAITING_TENANT', tenantName: 'Đặng Văn Long', versions, ...extra,
});
const terms = (deposit: unknown) => ({
  monthlyRent: term(2_500_000), deposit: term(deposit), startDate: term('2026-06-01'), endDate: term('2027-05-31'), electricityRate: term(3500), waterRate: term(18000),
});

const V2 = { versionId: 'v2', number: 2, type: 'INITIAL', status: 'UPLOADED', terms: terms(2_500_000), files: [file('f2')], landlordConfirmedAt: null, tenantConfirmedAt: null, rejectReason: null };
const V1 = { versionId: 'v1', number: 1, type: 'INITIAL', status: 'REJECTED', terms: terms(3_000_000), files: [file('f1')], landlordConfirmedAt: '2026-11-18T01:00:00Z', tenantConfirmedAt: null, rejectReason: 'Tiền cọc sai' };

describe('ContractsStore with the API', () => {
  let store: ContractsStore;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(ContractsStore);
    http = TestBed.inject(HttpTestingController);
    // The rooms and tenancies are only read when the contracts page opens.
    store.refreshIfStale();
    http.expectOne(`${BASE}/me`).flush(ok({ fullName: 'Cô Lan', role: 'LANDLORD' }));
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', electricityRate: 3500, waterRate: 18000 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(
      ok({ items: [{ roomId: 'r2', code: 'P.102', monthlyRent: 2_400_000 }, { roomId: 'r1', code: 'P.101', monthlyRent: 2_500_000 }] }),
    );
    http.expectOne(`${BASE}/rooms/r1`).flush(ok({ roomId: 'r1', currentTenancy: { tenancyId: 't1' } }));
    http.expectOne(`${BASE}/rooms/r2`).flush(ok({ roomId: 'r2', currentTenancy: null }));
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([V2, V1])));
  });

  afterEach(() => {
    vi.useRealTimers();
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('reads each room: its tenancy, or a placeholder where a first scan can go', () => {
    expect(store.sample).toBe(false);
    expect(store.status()).toBe('ready');
    expect(store.contracts().map((c) => [c.roomCode, c.tenancyId])).toEqual([['P.101', 't1'], ['P.102', 'new:r2']]);
    expect(hasNoTenancy(store.byId('new:r2')!)).toBe(true);
    expect(contractChip(store.byId('new:r2')!).label).toBe('Chưa có bản scan');
  });

  it('shows the versions oldest first, with the dates the API does not have left empty', () => {
    const c = store.byId('t1')!;
    expect(c.versions.map((v) => [v.number, v.status])).toEqual([[1, 'REJECTED'], [2, 'UPLOADED']]);
    expect(c.versions[1]).toMatchObject({ note: 'sửa tiền cọc', uploadedAt: null, sentAt: null });
    expect(c.versions[0]).toMatchObject({ sentAt: '2026-11-18T01:00:00Z', rejectReason: 'Tiền cọc sai', tenantRespondedAt: null });
    expect(latest(c)?.files[0]).toMatchObject({ url: 'https://s3.test/f2?sig=1', local: false });
    expect(contractChip(c).label).toBe('Người thuê báo không đúng');
  });

  it('puts the property rates and the room rent in terms the server has no value for', () => {
    const c = toContract(tenancy([{ versionId: 'v', number: 1, type: 'INITIAL', status: 'UPLOADED', terms: {}, files: [] }]) as never, { monthlyRent: 2_500_000, electricityRate: 3500, waterRate: 18000 });
    expect(latest(c)?.terms).toEqual({ monthlyRent: 2_500_000, deposit: 0, startDate: '', endDate: '', electricityRate: 3500, waterRate: 18000 });
    expect(emptyContract('r9', 'P.109')).toMatchObject({ tenancyId: 'new:r9', roomId: 'r9', versions: [] });
  });

  it('uploads the picked files to the draft version, then reads the tenancy again', async () => {
    let done: { tenancyId: string } | null = null;
    const c = store.byId('t1')!;
    store.uploadScanByApi(c, [new File(['abc'], 'trang-1.jpg', { type: 'image/jpeg' })]).subscribe((fresh) => (done = fresh));
    expect(store.uploading()).toBe(true);
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([V2, V1])));

    // The newest version is a draft: no new version, the file goes to it. The hash is computed asynchronously.
    const upload = await vi.waitFor(() => http.expectOne(`${BASE}/uploads`));
    expect(upload.request.body).toEqual({
      targetType: 'TENANCY_VERSION', targetId: 'v2', fileName: 'trang-1.jpg', contentType: 'image/jpeg', size: 3, sha256: 'ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=',
    });
    upload.flush(ok({ fileId: 'f9', uploadUrl: 'https://s3.test/put?sig=2', method: 'PUT', headers: { 'Content-Type': 'image/jpeg' } }));
    http.expectOne('https://s3.test/put?sig=2').flush('');
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([{ ...V2, files: [file('f2'), file('f9')] }, V1])));
    expect(done).toMatchObject({ tenancyId: 't1' });
    expect(latest(store.byId('t1')!)?.files).toHaveLength(2);
    expect(store.uploading()).toBe(false);
  });

  it('starts a new version when the newest one is not a draft, and creates the tenancy for a room without one', async () => {
    const c = store.byId('new:r2')!;
    store.uploadScanByApi(c, [new File(['abc'], 'p.pdf', { type: 'application/pdf' })]).subscribe();
    http.expectOne(`${BASE}/rooms/r2/tenancies`).flush(ok({ ...tenancy([{ ...V2, versionId: 'n1', number: 1 }]), tenancyId: 't2', roomId: 'r2', roomCode: 'P.102' }));
    const upload = await vi.waitFor(() => http.expectOne(`${BASE}/uploads`));
    expect(upload.request.body).toMatchObject({ targetId: 'n1', contentType: 'application/pdf' });
    upload.flush(ok({ uploadUrl: 'https://s3.test/put2', method: 'PUT', headers: {} }));
    http.expectOne('https://s3.test/put2').flush('');
    http.expectOne(`${BASE}/tenancies/t2`).flush(ok({ ...tenancy([{ ...V2, versionId: 'n1', number: 1 }]), tenancyId: 't2', roomId: 'r2', roomCode: 'P.102' }));
    // The placeholder of the room is replaced by the real contract.
    expect(store.contracts().map((x) => x.tenancyId)).toEqual(['t1', 't2']);

    const locked = store.byId('t1')!;
    store.uploadScanByApi({ ...locked }, [new File(['abc'], 'a.jpg', { type: 'image/jpeg' })]).subscribe();
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([{ ...V1, status: 'LOCKED' }], { status: 'ACTIVE' })));
    const post = http.expectOne(`${BASE}/tenancies/t1/versions`);
    expect(post.request.body).toEqual({ type: 'ADDENDUM' });
    post.flush(ok({ ...V2, versionId: 'v3', number: 2 }));
    const second = await vi.waitFor(() => http.expectOne(`${BASE}/uploads`));
    expect(second.request.body).toMatchObject({ targetId: 'v3' });
    second.flush(ok({ uploadUrl: 'https://s3.test/put3', method: 'PUT', headers: {} }));
    http.expectOne('https://s3.test/put3').flush('');
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([V2, V1])));
  });

  it('refuses a file type the API does not take', () => {
    let message = '';
    store.uploadScanByApi(store.byId('t1')!, [new File(['x'], 'a.docx', { type: 'application/msword' })]).subscribe({ error: (e: Error) => (message = e.message) });
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([V2, V1])));
    expect(message).toContain('chỉ nhận PDF hoặc ảnh');
    expect(store.uploading()).toBe(false);
  });

  it('saves the terms of the draft and reads the tenancy again', () => {
    let status = '';
    const c = store.byId('t1')!;
    const t = { monthlyRent: 2_500_000, deposit: 2_500_000, startDate: '2026-06-01', endDate: '2027-05-31', electricityRate: 3500, waterRate: 18000 };
    store.sendByApi(c, t).subscribe((s) => (status = s));
    const patch = http.expectOne(`${BASE}/tenancies/t1/versions/2/terms`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual(t);
    patch.flush(ok({ ...V2, status: 'AWAITING_CONFIRM' }));
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([{ ...V2, status: 'AWAITING_CONFIRM' }, V1])));
    expect(status).toBe('AWAITING_CONFIRM');
    expect(latest(store.byId('t1')!)?.status).toBe('AWAITING_CONFIRM');
    expect(store.sending()).toBe(false);
  });

  it('reads again when the last load failed', () => {
    store.load();
    // The first room to fail fails the load; the other read is cancelled.
    http.match((r) => r.url.includes('/rooms/r'))[0].error(new ProgressEvent('error'));
    expect(store.status()).toBe('error');
    store.refreshIfStale();
    expect(http.match((r) => r.url.includes('/rooms/r')).length).toBeGreaterThan(0);
  });

  it('reads again only when the room list changed or the links are old', () => {
    store.refreshIfStale();
    http.expectNone(`${BASE}/rooms/r1`);
    store.refreshLinks('t1');
    http.expectNone(`${BASE}/tenancies/t1`);
    vi.advanceTimersByTime(5 * 60_000);
    store.refreshLinks('t1');
    http.expectOne(`${BASE}/tenancies/t1`).flush(ok(tenancy([V2, V1])));
  });
});
