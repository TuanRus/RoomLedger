import { TestBed } from '@angular/core/testing';

import { MeterCaptureStore, READING_MS, UPLOAD_MS, costOf } from './meter-capture.store';

describe('MeterCaptureStore', () => {
  let store: MeterCaptureStore;

  beforeEach(() => {
    store = TestBed.inject(MeterCaptureStore);
  });

  it('starts the sample round at 26/32 with 6 rooms to capture and 2 retakes', () => {
    const g = store.groups();
    expect(store.total()).toBe(32);
    expect(store.recordedCount()).toBe(26);
    expect(g.todo.map((r) => r.roomCode)).toEqual(['P.204', 'P.307', 'P.308', 'P.113', 'P.214', 'P.215']);
    expect(g.retake.map((r) => r.roomCode)).toEqual(['P.306', 'P.210']);
  });

  it('opens P.204 on water, the meter still missing', () => {
    store.open('r-204');
    expect(store.active()).toEqual({ roomId: 'r-204', kind: 'WATER' });
    expect(store.nextRoomCode()).toBe('P.307');
  });

  it('uploads, reads and approves P.204 water (LM-06 → LM-08), then moves to P.307', () => {
    store.open('r-204');
    store.captureSample();
    expect(store.activeJob()?.phase).toBe('uploading');
    store.tick(UPLOAD_MS / 2);
    expect(Math.round(store.activeJob()!.progress)).toBe(50);
    store.tick(UPLOAD_MS / 2);
    expect(store.activeJob()?.phase).toBe('reading');
    store.tick(READING_MS);
    expect(store.activeJob()?.phase).toBe('read');
    expect(store.activeJob()?.aiValue).toBe(67);
    expect(costOf('WATER', 61, 67)).toEqual({ usage: 6, amount: 108000 });

    expect(store.approve(67)).toBe(true);
    expect(store.active()).toEqual({ roomId: 'r-307', kind: 'ELECTRICITY' });
    expect(store.recordedCount()).toBe(27);
  });

  it('cannot read the blurry P.306 photo (LM-09) and rejects a value below the previous one', () => {
    store.open('r-306');
    store.captureSample();
    store.tick(UPLOAD_MS);
    store.tick(READING_MS);
    expect(store.activeJob()?.phase).toBe('unreadable');
    expect(store.approve(1000)).toBe(false);
    expect(store.approve(2090)).toBe(true);
  });

  it('holds uploads while offline and resumes on retry (LM-10)', () => {
    store.setAutoSend(false);
    store.open('r-307');
    store.captureSample();
    store.tick(UPLOAD_MS * 0.64);
    store.setSimulateOffline(true);
    expect(store.heldCount()).toBe(1);
    store.tick(UPLOAD_MS);
    expect(Math.round(store.pending()[0].progress)).toBe(64);
    expect(store.retry()).toBe(false);

    store.setSimulateOffline(false);
    expect(store.heldCount()).toBe(1);
    expect(store.retry()).toBe(true);
    store.tick(UPLOAD_MS);
    expect(store.pending()).toHaveLength(0);
  });

  it('sends held photos automatically when the network returns and auto-send is on', () => {
    store.setSimulateOffline(true);
    store.open('r-308');
    store.captureSample();
    expect(store.heldCount()).toBe(1);
    store.setSimulateOffline(false);
    expect(store.heldCount()).toBe(0);
  });
});
