import { TestBed } from '@angular/core/testing';

import { ReviewRow } from './models';
import { MeterReviewStore, checkValue, deltaOf, stateOf } from './meter-review.store';

describe('MeterReviewStore', () => {
  let store: MeterReviewStore;

  beforeEach(() => {
    store = TestBed.inject(MeterReviewStore);
  });

  it('groups sample rows by review state', () => {
    expect(store.counts()).toEqual({ pending: 8, abnormal: 1, unreadable: 1, unrecorded: 3, approved: 0 });
  });

  it('pre-selects only pending readings within ±10% of the 3-period average', () => {
    const ids = store.normalPendingIds();
    expect(ids).toHaveLength(8);
    expect(ids.some((id) => id.startsWith('r-305'))).toBe(false);
  });

  it('flags the +64% spike as abnormal', () => {
    const row = store.rows().find((r) => r.roomCode === 'P.305')!;
    expect(stateOf(row)).toBe('abnormal');
    expect(Math.round(deltaOf(row)! * 100)).toBe(64);
  });

  it('approves readings and undoes the last approval', () => {
    const approved = store.approve(store.normalPendingIds());
    expect(approved).toBe(8);
    expect(store.counts().approved).toBe(8);

    store.undoApprove();
    expect(store.counts().approved).toBe(0);
    expect(store.counts().pending).toBe(8);
  });

  it('keeps a landlord correction and marks it as edited', () => {
    const id = 'r-305-ELECTRICITY-2026-11';
    store.setValue(id, 2898);
    const row = store.rows().find((r) => r.reading?.readingId === id)!;
    expect(row.reading!.value).toBe(2898);
    expect(row.reading!.editedByLandlord).toBe(true);
  });

  it('switches to water readings', () => {
    store.kind.set('WATER');
    expect(store.rows().map((r) => r.roomCode)).toEqual(['P.204', 'P.112']);
  });
});

describe('checkValue', () => {
  const row = { averageUsage: 100, reading: { previousValue: 1000 } } as unknown as ReviewRow;

  it('blocks a value below the previous reading', () => {
    expect(checkValue(row, 900)).toEqual({ usage: -100, delta: -2, problem: 'below-previous' });
  });

  it('flags usage more than 50% away from the 3-period average', () => {
    expect(checkValue(row, 1160).problem).toBe('abnormal');
    expect(checkValue(row, 1040).problem).toBe('abnormal');
  });

  it('accepts usage near the average', () => {
    expect(checkValue(row, 1120)).toEqual({ usage: 120, delta: expect.closeTo(0.2), problem: null });
  });

  it('returns nothing to check without a value', () => {
    expect(checkValue(row, null).problem).toBeNull();
  });
});
