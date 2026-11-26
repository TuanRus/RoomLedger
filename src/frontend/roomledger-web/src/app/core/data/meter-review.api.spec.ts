import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../api/api.config';
import { UnsavedChanges } from '../api/unsaved-changes';
import { LandlordPropertyStore } from './landlord-property.store';
import { MeterReviewStore, UNDO_SECONDS, reviewPeriod, stateOf } from './meter-review.store';

const BASE = 'http://api.test/api/v1';
const PROPERTY = 'p-1';

function reading(id: string, kind: 'ELECTRICITY' | 'WATER', extra: Record<string, unknown> = {}) {
  return { readingId: id, roomId: 'room', kind, period: '2026-10', status: 'PENDING_CONFIRM', value: 1200, aiValue: 1200, aiConfidence: 0.95, previousValue: 1100, needsReview: false, reviewReason: null, editedByLandlord: false, confirmedAt: null, photoUrl: null, ...extra };
}

describe('MeterReviewStore with the API', () => {
  let store: MeterReviewStore;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 25));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
    store = TestBed.inject(MeterReviewStore);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/properties?limit=1`).flush({ success: true, message: 'OK', data: { items: [{ propertyId: PROPERTY }] } });
    http.expectOne(`${BASE}/properties/${PROPERTY}/rooms?limit=100`).flush({ success: true, message: 'OK', data: { items: [{ roomId: 'r1', code: 'P.101', status: 'OCCUPIED', tenantName: 'Nguyễn Văn A' }, { roomId: 'r2', code: 'P.102', status: 'VACANT', tenantName: null }] } });
    http.expectOne(`${BASE}/properties/${PROPERTY}/meter-readings?period=2026-10`).flush({
      success: true,
      message: 'OK',
      data: [
        { roomId: 'r1', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY'), water: reading('w1', 'WATER', { value: 50, aiValue: 50, previousValue: 44 }) },
        { roomId: 'r2', roomCode: 'P.102', electricity: reading('e2', 'ELECTRICITY', { status: 'NEEDS_REVIEW', needsReview: true, value: null, aiValue: null, reviewReason: 'AI_FAILED' }), water: null },
      ],
    });
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('reviews last month and maps the API rows', () => {
    expect(store.period()).toBe('2026-10');
    expect(store.status()).toBe('ready');
    expect(store.rows().map((r) => [r.roomCode, stateOf(r)])).toEqual([['P.101', 'pending'], ['P.102', 'unreadable']]);
    store.kind.set('WATER');
    expect(store.rows().map(stateOf)).toEqual(['pending', 'unrecorded']);
  });

  it('joins the tenant name from the room list', () => {
    expect(store.rows().map((r) => r.tenantName)).toEqual(['Nguyễn Văn A', '']);
  });

  it('offers pending readings for bulk approval when the API sends no average', () => {
    expect(store.normalPendingIds()).toEqual(['e1']);
  });

  it('sends the approval only after the undo window', () => {
    store.approve(['e1']);
    expect(store.counts().approved).toBe(1);
    http.expectNone(`${BASE}/meter-readings/e1`);

    vi.advanceTimersByTime(UNDO_SECONDS * 1000);
    const req = http.expectOne(`${BASE}/meter-readings/e1`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ value: 1200, confirm: true });
    req.flush({ success: true, message: 'OK', data: reading('e1', 'ELECTRICITY', { status: 'CONFIRMED', confirmedAt: '2026-11-25T01:00:00Z' }) });
    expect(store.counts().approved).toBe(1);
  });

  it('sends nothing when the approval is undone', () => {
    store.approve(['e1']);
    store.undoApprove();
    vi.advanceTimersByTime(UNDO_SECONDS * 1000);
    http.expectNone(`${BASE}/meter-readings/e1`);
    expect(store.counts().pending).toBe(1);
  });

  it('sends a corrected value with the approval', () => {
    store.setValue('e2', 1180);
    store.approve(['e2']);
    vi.advanceTimersByTime(UNDO_SECONDS * 1000);
    expect(http.expectOne(`${BASE}/meter-readings/e2`).request.body).toEqual({ value: 1180, confirm: true });
  });

  it('undo after a typed value puts the number the AI read back', () => {
    store.approve(['e1'], 1250);
    expect(store.rows()[0].reading).toMatchObject({ status: 'CONFIRMED', value: 1250, editedByLandlord: true });
    store.undoApprove();
    expect(store.rows()[0].reading).toMatchObject({ status: 'PENDING_CONFIRM', value: 1200, editedByLandlord: false });
    vi.advanceTimersByTime(UNDO_SECONDS * 1000);
    http.expectNone(`${BASE}/meter-readings/e1`);
  });

  it('sends the typed value and keeps the approval when the readings are reloaded inside the window', () => {
    store.approve(['e1'], 1250);
    store.load();
    http.expectOne(`${BASE}/properties/${PROPERTY}/meter-readings?period=2026-10`).flush({
      success: true,
      message: 'OK',
      data: [{ roomId: 'r1', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY'), water: null }],
    });
    // The server does not know the approval yet, the screen still shows it.
    expect(store.rows()[0].reading).toMatchObject({ status: 'CONFIRMED', value: 1250 });
    vi.advanceTimersByTime(UNDO_SECONDS * 1000);
    expect(http.expectOne(`${BASE}/meter-readings/e1`).request.body).toEqual({ value: 1250, confirm: true });
  });

  it('ignores the answer of an older load that arrives after a newer one', () => {
    store.load();
    store.load();
    const [older, newer] = http.match(`${BASE}/properties/${PROPERTY}/meter-readings?period=2026-10`);
    newer.flush({ success: true, message: 'OK', data: [] });
    older.flush({ success: true, message: 'OK', data: [{ roomId: 'r1', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY'), water: null }] });
    expect(store.rows()).toEqual([]);
    expect(store.status()).toBe('ready');
  });

  it('reloads when another screen changed the readings, and keeps an approval held for undo', () => {
    store.approve(['e1'], 1250);
    TestBed.inject(LandlordPropertyStore).invalidateReadings();
    TestBed.tick();
    http.expectOne(`${BASE}/properties/${PROPERTY}/meter-readings?period=2026-10`).flush({
      success: true,
      message: 'OK',
      data: [{ roomId: 'r1', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY'), water: null }],
    });
    expect(store.rows()[0].reading).toMatchObject({ status: 'CONFIRMED', value: 1250 });
    // Its own save does not make it reload again.
    vi.advanceTimersByTime(UNDO_SECONDS * 1000);
    http.expectOne(`${BASE}/meter-readings/e1`).flush({ success: true, message: 'OK', data: reading('e1', 'ELECTRICITY', { status: 'CONFIRMED', value: 1250 }) });
    TestBed.tick();
    http.expectNone(`${BASE}/properties/${PROPERTY}/meter-readings?period=2026-10`);
  });

  it('keeps the approval shown while its PATCH is on the way, and a load that began earlier cannot overwrite the reply', () => {
    const readingsUrl = `${BASE}/properties/${PROPERTY}/meter-readings?period=2026-10`;
    const stale = { success: true, message: 'OK', data: [{ roomId: 'r1', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY'), water: null }] };
    store.approve(['e1']);
    vi.advanceTimersByTime(UNDO_SECONDS * 1000);
    const patch = http.expectOne(`${BASE}/meter-readings/e1`);

    // Another screen changes the readings while the PATCH is in flight: the reload must not flicker back to pending.
    TestBed.inject(LandlordPropertyStore).invalidateReadings();
    TestBed.tick();
    const early = http.expectOne(readingsUrl);
    early.flush(stale);
    expect(store.rows()[0].reading?.status).toBe('CONFIRMED');

    // A load still running when the reply arrives is dropped and the readings are read once more.
    store.load();
    const inFlight = http.expectOne(readingsUrl);
    patch.flush({ success: true, message: 'OK', data: reading('e1', 'ELECTRICITY', { status: 'CONFIRMED', value: 1200 }) });
    const again = http.expectOne(readingsUrl);
    again.flush({ success: true, message: 'OK', data: [{ roomId: 'r1', roomCode: 'P.101', electricity: reading('e1', 'ELECTRICITY', { status: 'CONFIRMED' }), water: null }] });
    inFlight.flush(stale);
    expect(store.rows()[0].reading?.status).toBe('CONFIRMED');
  });

  it('asks the browser to confirm leaving while an approval is held', () => {
    const guard = TestBed.inject(UnsavedChanges);
    expect(guard.any()).toBe(false);
    store.approve(['e1']);
    expect(guard.any()).toBe(true);
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    store.undoApprove();
    expect(guard.any()).toBe(false);
  });

  it('reloads and shows the error when saving fails', () => {
    store.approve(['e1']);
    vi.advanceTimersByTime(UNDO_SECONDS * 1000);
    http.expectOne(`${BASE}/meter-readings/e1`).flush(
      { success: false, message: 'Chỉ số chưa đọc xong hoặc đã được xác nhận', errorCode: 'INVALID_STATE', traceId: 't' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(store.error()).toContain('Chỉ số chưa đọc xong');
    http.expectOne(`${BASE}/properties/${PROPERTY}/meter-readings?period=2026-10`);
  });
});

describe('reviewPeriod', () => {
  it('is the month before today', () => {
    expect(reviewPeriod(new Date(2026, 11, 2))).toBe('2026-11');
    expect(reviewPeriod(new Date(2027, 0, 2))).toBe('2026-12');
  });
});
