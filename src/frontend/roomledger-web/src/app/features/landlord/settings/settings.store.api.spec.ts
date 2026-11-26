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
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', electricityRate: 3800, waterRate: 20000, serviceFee: 100000 }] }));
    http.expectOne(`${BASE}/properties/p1/payment-info`).flush(
      ok({ bankName: 'Vietcombank', accountNumber: '0123456789', accountHolder: 'LE THI LAN', transferNoteTemplate: '{room} T{month}', qrUrl: 'https://s3.test/qr.png', qrStatus: 'RECEIVED' }),
    );
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
    expect(s.payment).toMatchObject({ bankName: 'Vietcombank', accountNumber: '0123456789', accountHolder: 'LE THI LAN', qrImageUrl: 'https://s3.test/qr.png' });
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

  it('saves the prices with PATCH, then the bank details with PUT, then reloads the property', () => {
    const cur = store.settings();
    const next = { ...cur, electricityRate: 4000, payment: { ...cur.payment, accountNumber: '0123 456 000', transferNoteTemplate: '{room} {period}' } };
    let done = false;
    store.saveApi(next).subscribe(() => (done = true));
    const patch = http.expectOne(`${BASE}/properties/p1`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ electricityRate: 4000, waterRate: 20000, serviceFee: 100000 });
    patch.flush(ok({ propertyId: 'p1', electricityRate: 4000, waterRate: 20000, serviceFee: 100000 }));
    const put = http.expectOne(`${BASE}/properties/p1/payment-info`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ bankName: 'Vietcombank', accountNumber: '0123 456 000', accountHolder: 'LE THI LAN', transferNoteTemplate: '{room} {period}' });
    put.flush(ok({ bankName: 'Vietcombank', accountNumber: '0123456000', accountHolder: 'LE THI LAN', transferNoteTemplate: '{room} {period}', qrUrl: null }));
    expect(done).toBe(true);
    expect(store.settings().electricityRate).toBe(4000);
    expect(store.settings().payment.accountNumber).toBe('0123456000');
    // The property store reads the property and rooms again.
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', electricityRate: 4000, waterRate: 20000, serviceFee: 100000 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
  });

  it('sends the service fee in the same PATCH', () => {
    store.saveApi({ ...store.settings(), serviceFee: 250000 }).subscribe();
    const patch = http.expectOne(`${BASE}/properties/p1`);
    expect(patch.request.body).toEqual({ electricityRate: 3800, waterRate: 20000, serviceFee: 250000 });
    patch.flush(ok({ propertyId: 'p1', electricityRate: 3800, waterRate: 20000, serviceFee: 250000 }));
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', electricityRate: 3800, waterRate: 20000, serviceFee: 250000 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
    expect(store.settings().serviceFee).toBe(250000);
  });

  it('clears the bank details with DELETE when all three fields are emptied (and not with PUT)', () => {
    const cur = store.settings();
    const next = { ...cur, payment: { ...cur.payment, bankName: '', accountNumber: '', accountHolder: '' } };
    expect(store.clearsBank(next)).toBe(true);
    expect(store.clearsBank(cur)).toBe(false);
    store.saveApi(next).subscribe();
    const del = http.expectOne(`${BASE}/properties/p1/payment-info`);
    expect(del.request.method).toBe('DELETE');
    del.flush(ok({ bankName: null, accountNumber: null, accountHolder: null, transferNoteTemplate: '{room} T{month}', qrUrl: null, qrStatus: null }));
    expect(store.settings().payment).toMatchObject({ bankName: '', accountNumber: '', qrImageUrl: null });
    expect(store.clearsBank(next)).toBe(false);   // nothing left to clear
  });

  it('shows the server message when the payment info is refused', () => {
    const cur = store.settings();
    store.saveApi({ ...cur, payment: { ...cur.payment, accountNumber: '123' } }).subscribe({ error: () => undefined });
    http
      .expectOne(`${BASE}/properties/p1/payment-info`)
      .flush({ success: false, message: 'Số tài khoản chỉ gồm chữ số', errorCode: 'VALIDATION_ERROR', traceId: 't' }, { status: 400, statusText: 'Bad Request' });
    expect(store.saveError()).toBe('Số tài khoản chỉ gồm chữ số');
  });

  it('uploads a QR: POST /uploads, PUT to the presigned URL, then confirm', async () => {
    const file = new File(['qr'], 'qr.png', { type: 'image/png' });
    const progress: number[] = [];
    store.uploadQr(file).subscribe((p) => progress.push(p));
    const upload = await vi.waitFor(() => http.expectOne(`${BASE}/uploads`));
    expect(upload.request.body).toMatchObject({ targetType: 'PROPERTY_QR', targetId: 'p1', fileName: 'qr.png', contentType: 'image/png', size: 2 });
    expect(upload.request.body.sha256).toHaveLength(44);
    upload.flush(ok({ fileId: 'f', uploadUrl: 'https://s3.test/in/key', method: 'PUT', headers: { 'Content-Type': 'image/png' } }));
    http.expectOne('https://s3.test/in/key').flush('');
    http.expectOne(`${BASE}/properties/p1/payment-info/qr:confirm`).flush(ok({ bankName: 'Vietcombank', qrUrl: 'https://s3.test/new.png', qrStatus: 'RECEIVED' }));
    expect(progress.at(-1)).toBe(100);
    expect(store.settings().payment.qrImageUrl).toBe('https://s3.test/new.png');
    expect(store.qrProgress()).toBeNull();
  });

  it('refuses a QR of the wrong type or over 2 MB without calling the API', () => {
    let msg = '';
    store.uploadQr(new File(['x'], 'a.gif', { type: 'image/gif' })).subscribe({ error: (e: Error) => (msg = e.message) });
    expect(msg).toContain('PNG');
    store.uploadQr(new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'a.png', { type: 'image/png' })).subscribe({ error: (e: Error) => (msg = e.message) });
    expect(msg).toContain('2 MB');
  });

  it('removes the QR with DELETE', () => {
    store.removeQr().subscribe();
    const req = http.expectOne(`${BASE}/properties/p1/payment-info/qr`);
    expect(req.request.method).toBe('DELETE');
    req.flush(ok({ bankName: 'Vietcombank', qrUrl: null, qrStatus: null }));
    expect(store.settings().payment.qrImageUrl).toBeNull();
  });

  it('builds empty settings without a property', () => {
    expect(settingsFromApi(null, [])).toMatchObject({ electricityRate: 0, rents: [] });
  });
});
