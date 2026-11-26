import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../../core/api/api.config';
import { RoomsStore, contractFromDisplay, toRoomDetail } from './rooms.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

function reading(id: string, kind: string, extra: Record<string, unknown> = {}) {
  return { readingId: id, roomId: 'r1', kind, period: '2026-10', status: 'PENDING_CONFIRM', value: 120, aiValue: 120, aiConfidence: 0.9, previousValue: 100, needsReview: false, reviewReason: null, editedByLandlord: false, confirmedAt: null, photoUrl: null, ...extra };
}

describe('RoomsStore with the API', () => {
  let store: RoomsStore;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(RoomsStore);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', roomCount: 3 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(
      ok({
        items: [
          { roomId: 'r2', code: 'P.102', monthlyRent: 2_400_000, status: 'VACANT', displayStatus: 'VACANT', tenantName: null, version: 3 },
          { roomId: 'r1', code: 'P.101', monthlyRent: 2_500_000, status: 'OCCUPIED', displayStatus: 'ACTIVE', tenantName: 'Đặng Văn Long', version: 1 },
          { roomId: 'r3', code: 'P.103', monthlyRent: 2_600_000, status: 'VACANT', displayStatus: 'AWAITING_CONFIRM', tenantName: null, version: 2 },
        ],
      }),
    );
    http.expectOne(`${BASE}/properties/p1/meter-readings?period=2026-10`).flush(
      ok([{ roomId: 'r1', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY'), water: reading('w1', 'WATER', { status: 'CONFIRMED', value: 50, previousValue: 44, confirmedAt: '2026-11-03T02:00:00Z' }) }]),
    );
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('lists the rooms in code order with what the API knows, and nothing invented', () => {
    expect(store.sample).toBe(false);
    expect(store.status()).toBe('ready');
    expect(store.rooms().map((r) => r.code)).toEqual(['P.101', 'P.102', 'P.103']);
    const r101 = store.byId('r1')!;
    expect(r101).toMatchObject({ tenantName: 'Đặng Văn Long', floor: null, deposit: null, tenancyStart: null, invite: null, reading: 'pending', contract: 'CONFIRMED' });
    // Electricity shows the previous period's value (no date), water the confirmed one with its date.
    expect(r101.electricity).toEqual({ value: 100, date: null });
    expect(r101.water).toEqual({ value: 50, date: '2026-11-03' });
    const r102 = store.byId('r2')!;
    expect(r102).toMatchObject({ reading: null, electricity: null, water: null, contract: 'NONE' });
    expect(store.byId('r3')!.contract).toBe('INVITED');
    expect(store.counts()).toEqual({ all: 3, occupied: 1, vacant: 2, awaiting: 1 });
    expect(store.period()).toBe('2026-10');
  });

  it('loads the deposit, start date and contract state of a room when it is selected', () => {
    store.loadDetail('r1');
    expect(store.detailOf('r1')?.status).toBe('loading');
    http.expectOne(`${BASE}/rooms/r1`).flush(ok({ roomId: 'r1', currentTenancy: { tenancyId: 't1', status: 'ACTIVE', tenantName: 'Đặng Văn Long' } }));
    http.expectOne(`${BASE}/tenancies/t1`).flush(
      ok({
        tenancyId: 't1',
        status: 'ACTIVE',
        tenantName: 'Đặng Văn Long',
        versions: [
          { number: 2, status: 'UPLOADED', terms: { deposit: { value: 9 } }, files: [{ fileId: 'f1' }, { fileId: 'f2' }] },
          { number: 1, status: 'LOCKED', terms: { deposit: { value: 2_500_000 }, startDate: { value: '2026-03-01' } }, files: [] },
        ],
      }),
    );
    const row = store.byId('r1')!;
    expect(row.deposit).toBe(2_500_000);
    expect(row.tenancyStart).toBe('2026-03-01');
    expect(row.contractFiles).toBe(2);
    // The draft addendum is the latest version, but the locked one carries the terms in force.
    expect(store.detailOf('r1')?.status).toBe('ready');
    store.loadDetail('r1');
    http.expectNone(`${BASE}/rooms/r1`);
  });

  it('a room without a tenancy has no deposit and no start date', () => {
    store.loadDetail('r2');
    http.expectOne(`${BASE}/rooms/r2`).flush(ok({ roomId: 'r2', currentTenancy: null }));
    expect(store.byId('r2')).toMatchObject({ deposit: null, tenancyStart: null, contract: 'NONE' });
  });

  it('shows the error of the room detail and reads it again on request', () => {
    store.loadDetail('r1');
    http.expectOne(`${BASE}/rooms/r1`).flush({ success: false, message: 'Không tìm thấy phòng', errorCode: 'NOT_FOUND', traceId: 't' }, { status: 404, statusText: 'Not Found' });
    expect(store.detailOf('r1')).toMatchObject({ status: 'error', error: 'Không tìm thấy phòng' });
    store.loadDetail('r1', true);
    http.expectOne(`${BASE}/rooms/r1`).flush(ok({ roomId: 'r1', currentTenancy: null }));
    expect(store.detailOf('r1')?.status).toBe('ready');
  });

  it('invites: opens the draft tenancy, sends the invite, then reloads the rooms', () => {
    let result: unknown;
    store.inviteByApi('r2', ' ngoc.han@vidu.vn ').subscribe((i) => (result = i));
    const create = http.expectOne(`${BASE}/rooms/r2/tenancies`);
    expect(create.request.method).toBe('POST');
    create.flush(ok({ tenancyId: 't9', status: 'DRAFT' }));
    const invite = http.expectOne(`${BASE}/tenancies/t9/invites`);
    expect(invite.request.body).toEqual({ email: 'ngoc.han@vidu.vn' });
    invite.flush(ok({ inviteId: 'i1', email: 'ngoc.han@vidu.vn', expiresAt: '2026-11-28T10:00:00+07:00' }));
    expect(result).toEqual({ email: 'ngoc.han@vidu.vn', startDate: null, expiresAt: '2026-11-28T10:00:00+07:00' });
    expect(store.byId('r2')?.invite?.email).toBe('ngoc.han@vidu.vn');
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
  });

  it('passes the server message when the invite is refused', () => {
    let message = '';
    store.inviteByApi('r2', 'a@b.vn').subscribe({ error: (e: Error) => (message = e.message) });
    http.expectOne(`${BASE}/rooms/r2/tenancies`).flush(ok({ tenancyId: 't9' }));
    http.expectOne(`${BASE}/tenancies/t9/invites`).flush(
      { success: false, message: 'Hãy lưu điều khoản trước khi mời người thuê', errorCode: 'INVALID_STATE', traceId: 't' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(message).toBe('Hãy lưu điều khoản trước khi mời người thuê');
    // The tenancy may exist now: the room list is read again.
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
  });
});

describe('room mapping', () => {
  it('maps the display status to the contract column and refines it from the tenancy', () => {
    expect(contractFromDisplay('VACANT')).toBe('NONE');
    expect(contractFromDisplay('AWAITING_CONFIRM')).toBe('INVITED');
    expect(contractFromDisplay('MOVING_OUT')).toBe('CONFIRMED');
    expect(toRoomDetail({ status: 'AWAITING_TENANT', tenantName: 'A', versions: [{ number: 1, status: 'REJECTED', files: [] }] }, 'ACTIVE').contract).toBe('REJECTED');
    expect(toRoomDetail({ status: 'AWAITING_TENANT', tenantName: 'A', versions: [{ number: 1, status: 'AWAITING_CONFIRM', files: [] }] }, 'AWAITING_CONFIRM').contract).toBe('AWAITING_TENANT');
    expect(toRoomDetail({ status: 'DRAFT', tenantName: null, versions: [{ number: 1, status: 'UPLOADED', files: [{}] }] }, 'VACANT')).toMatchObject({ contract: 'DRAFT', files: 1 });
    expect(toRoomDetail(null, 'VACANT')).toMatchObject({ contract: null, files: null, deposit: null });
  });
});
