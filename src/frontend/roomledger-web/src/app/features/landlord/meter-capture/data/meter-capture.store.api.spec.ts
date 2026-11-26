import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../../../../core/api/api.config';
import { UnsavedChanges } from '../../../../core/api/unsaved-changes';
import { LandlordPropertyStore } from '../../../../core/data/landlord-property.store';
import { MeterReviewStore } from '../../../../core/data/meter-review.store';
import { MeterCaptureStore, POLL_MS, costOf, slotFromReading } from './meter-capture.store';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });
const reading = (id: string, kind: string, extra: Record<string, unknown> = {}) => ({
  readingId: id, roomId: 'a', kind, period: '2026-10', status: 'PENDING_CONFIRM', value: 1200, aiValue: 1200, aiConfidence: 0.9, previousValue: 1100, ...extra,
});
const pendingAi = { status: 'PENDING_AI', value: null, aiValue: null };
const SHA_ABC = 'ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=';

describe('readings shared by the review and capture screens', () => {
  it('asks the server for the period readings once', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    const review = TestBed.inject(MeterReviewStore);
    const capture = TestBed.inject(MeterCaptureStore);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1' }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [] }));
    http.expectOne(`${BASE}/properties/p1/meter-readings?period=${review.period()}`).flush(ok([{ roomId: 'a', roomCode: 'P.101', electricity: null, water: null }]));
    http.verify();
    expect(review.status()).toBe('ready');
    expect(capture.status()).toBe('ready');
    expect(capture.rooms().map((r) => r.roomCode)).toEqual(['P.101']);
    expect(review.rows().map((r) => r.roomCode)).toEqual(['P.101']);

    // A reload from one screen asks again.
    capture.load();
    http.expectOne(`${BASE}/properties/p1/meter-readings?period=${review.period()}`).flush(ok([]));
    expect(capture.rooms()).toEqual([]);
    TestBed.resetTestingModule();
  });
});

describe('MeterCaptureStore with the API', () => {
  let store: MeterCaptureStore;
  let http: HttpTestingController;
  const url = `${BASE}/properties/p1/meter-readings?period=2026-10`;
  const jpg = () => new File(['abc'], 'n.jpg', { type: 'image/jpeg' });

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(MeterCaptureStore);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', electricityRate: 3500, waterRate: 18000 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(
      ok({ items: [{ roomId: 'a', code: 'P.101', status: 'OCCUPIED', tenantName: 'An' }, { roomId: 'b', code: 'P.102', status: 'OCCUPIED', tenantName: 'Bình' }] }),
    );
    http.expectOne(url).flush(
      ok([
        { roomId: 'b', roomCode: 'P.102', electricity: null, water: null },
        { roomId: 'a', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY', { status: 'NEEDS_REVIEW', value: null, aiValue: null }), water: null },
      ]),
    );
  });

  afterEach(() => {
    vi.useRealTimers();
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('builds the round from the period readings, with no sample data and no meter number', () => {
    expect(store.sample).toBe(false);
    expect(store.status()).toBe('ready');
    expect(store.period()).toBe('2026-10');
    expect(store.rooms().map((r) => [r.roomCode, r.tenantName])).toEqual([['P.101', 'An'], ['P.102', 'Bình']]);
    const a = store.rooms()[0];
    expect(a.electricity).toMatchObject({ base: 'retake', previousValue: 1100, meterNo: null, readingId: 'e1', sample: null });
    expect(a.water).toMatchObject({ base: 'todo', previousValue: null, readingId: null });
    expect(store.groups().todo.map((r) => r.roomCode)).toEqual(['P.101', 'P.102']);
    expect(store.rateOf('WATER')).toBe(18000);
  });

  it('ignores the demo controls', () => {
    store.open('a');
    store.captureSample();
    expect(store.jobs()).toEqual([]);
    store.setSimulateOffline(true);
    expect(store.online()).toBe(true);
  });

  it('maps reading statuses to slots and costs with the property rate', () => {
    expect(slotFromReading('WATER', null).base).toBe('todo');
    expect(slotFromReading('WATER', { status: 'PENDING_AI' }).base).toBe('sent');
    expect(slotFromReading('WATER', { status: 'NEEDS_REVIEW', value: 5 }).base).toBe('sent');
    expect(slotFromReading('WATER', { status: 'CONFIRMED', value: 5 })).toMatchObject({ base: 'approved', value: 5 });
    expect(costOf('WATER', 10, 15, 18000)).toEqual({ usage: 5, amount: 90000 });
    expect(costOf('WATER', null, 15, 18000)).toBeNull();
    expect(costOf('WATER', 10, 15, null)).toBeNull();
  });

  it('uploads a photo, waits for the AI, then approves with PATCH', async () => {
    store.open('a', 'WATER');
    store.capture(jpg());
    expect(store.activeJob()?.phase).toBe('uploading');
    const create = await vi.waitFor(() => http.expectOne(`${BASE}/rooms/a/meter-readings`));
    expect(create.request.body).toMatchObject({ kind: 'WATER', period: '2026-10', fileName: 'n.jpg', contentType: 'image/jpeg', size: 3, sha256: SHA_ABC });
    create.flush(ok({ reading: reading('w1', 'WATER', pendingAi), upload: { uploadUrl: 'https://s3.test/w1', method: 'PUT', headers: {} } }));
    http.expectOne('https://s3.test/w1').flush('');
    expect(store.activeJob()).toMatchObject({ phase: 'reading', readingId: 'w1' });

    vi.advanceTimersByTime(POLL_MS);
    http.expectOne(url).flush(ok([{ roomId: 'a', roomCode: 'P.101', electricity: null, water: reading('w1', 'WATER', pendingAi) }]));
    expect(store.activeJob()?.phase).toBe('reading');
    vi.advanceTimersByTime(POLL_MS);
    http.expectOne(url).flush(ok([{ roomId: 'a', roomCode: 'P.101', electricity: null, water: reading('w1', 'WATER', { value: 1180, aiValue: 1180 }) }]));
    expect(store.activeJob()).toMatchObject({ phase: 'read', aiValue: 1180, confidence: 0.9 });

    expect(store.approve(1000)).toBe(false);
    expect(store.approve(1180)).toBe(true);
    const patch = http.expectOne(`${BASE}/meter-readings/w1`);
    expect(patch.request.body).toEqual({ value: 1180, confirm: true });
    patch.flush(ok(reading('w1', 'WATER', { status: 'CONFIRMED', value: 1180 })));
    expect(store.activeJob()).toBeNull();
    expect(store.rooms()[0].water.base).toBe('approved');
  });

  it('polls once for all photos waiting for the AI and finishes each by its reading id', async () => {
    store.open('a', 'ELECTRICITY');
    store.capture(jpg());
    const c1 = await vi.waitFor(() => http.expectOne(`${BASE}/rooms/a/meter-readings`));
    c1.flush(ok({ reading: reading('e1', 'ELECTRICITY', pendingAi), upload: { uploadUrl: 'https://s3.test/e1', method: 'PUT', headers: {} } }));
    http.expectOne('https://s3.test/e1').flush('');
    store.open('a', 'WATER');
    store.capture(jpg());
    const c2 = await vi.waitFor(() => http.expectOne(`${BASE}/rooms/a/meter-readings`));
    c2.flush(ok({ reading: reading('w1', 'WATER', pendingAi), upload: { uploadUrl: 'https://s3.test/w1', method: 'PUT', headers: {} } }));
    http.expectOne('https://s3.test/w1').flush('');
    expect(store.jobs().map((j) => j.phase)).toEqual(['reading', 'reading']);

    // Two jobs waiting, one request per tick.
    vi.advanceTimersByTime(POLL_MS);
    http.expectOne(url).flush(ok([{ roomId: 'a', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY', { value: 1210, aiValue: 1210 }), water: reading('w1', 'WATER', pendingAi) }]));
    expect(store.jobs().map((j) => [j.readingId, j.phase])).toEqual([['e1', 'read'], ['w1', 'reading']]);

    vi.advanceTimersByTime(POLL_MS);
    http.expectOne(url).flush(ok([{ roomId: 'a', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY', { value: 1210, aiValue: 1210 }), water: reading('w1', 'WATER', { value: 1180, aiValue: 1180 }) }]));
    expect(store.jobs().map((j) => j.phase)).toEqual(['read', 'read']);

    // Nobody is waiting any more: the poller has stopped.
    vi.advanceTimersByTime(POLL_MS * 3);
    http.expectNone(url);
  });

  it('asks the browser to confirm leaving while a photo is still uploading', async () => {
    const guard = TestBed.inject(UnsavedChanges);
    expect(guard.any()).toBe(false);
    store.open('a', 'WATER');
    store.capture(jpg());
    expect(guard.any()).toBe(true);
    const create = await vi.waitFor(() => http.expectOne(`${BASE}/rooms/a/meter-readings`));
    create.flush(ok({ reading: reading('w1', 'WATER', pendingAi), upload: { uploadUrl: 'https://s3.test/w1', method: 'PUT', headers: {} } }));
    http.expectOne('https://s3.test/w1').flush('');
    expect(guard.any()).toBe(false);
  });

  it('reads the round again when another screen changed the readings', () => {
    TestBed.inject(LandlordPropertyStore).invalidateReadings();
    TestBed.tick();
    http.expectOne(url).flush(ok([{ roomId: 'a', roomCode: 'P.101', electricity: null, water: null }]));
    expect(store.rooms().map((r) => r.roomCode)).toEqual(['P.101']);
  });

  it('marks the photo unreadable when the AI gives no number, and keeps the job when saving fails', async () => {
    store.open('a', 'ELECTRICITY');
    store.capture(jpg());
    const create = await vi.waitFor(() => http.expectOne(`${BASE}/rooms/a/meter-readings`));
    create.flush(ok({ reading: reading('e1', 'ELECTRICITY', { status: 'PENDING_AI' }), upload: { uploadUrl: 'https://s3.test/e1', method: 'PUT', headers: {} } }));
    http.expectOne('https://s3.test/e1').flush('');
    vi.advanceTimersByTime(POLL_MS);
    http.expectOne(url).flush(
      ok([{ roomId: 'a', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY', { status: 'NEEDS_REVIEW', value: null, aiValue: null }), water: null }]),
    );
    expect(store.activeJob()?.phase).toBe('unreadable');

    store.approve(1150);
    http.expectOne(`${BASE}/meter-readings/e1`).flush({ success: false, message: 'Chỉ số nhỏ hơn kỳ trước', errorCode: 'VALIDATION_ERROR', traceId: 't' }, { status: 400, statusText: 'Bad Request' });
    expect(store.activeJob()?.error).toBe('Chỉ số nhỏ hơn kỳ trước');
    expect(store.approving()).toBe(false);
    expect(store.active()).not.toBeNull();
  });

  it('refuses other file types and shows the error when creating the reading fails', async () => {
    store.open('b', 'WATER');
    store.capture(new File(['x'], 'a.webp', { type: 'image/webp' }));
    expect(store.activeJob()?.error).toBe('Chỉ nhận ảnh JPG, PNG hoặc HEIC.');

    store.capture(jpg());
    const create = await vi.waitFor(() => http.expectOne(`${BASE}/rooms/b/meter-readings`));
    create.flush({ success: false, message: 'Kỳ đã đóng', errorCode: 'PERIOD_CLOSED', traceId: 't' }, { status: 409, statusText: 'Conflict' });
    expect(store.activeJob()?.error).toBe('Kỳ đã đóng');
    expect(store.failedCount()).toBe(1);
    store.discard();
  });
});
