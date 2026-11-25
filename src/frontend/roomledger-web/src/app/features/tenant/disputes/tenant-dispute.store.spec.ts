import { TestBed } from '@angular/core/testing';

import { SAMPLE_DISPUTES, TenantDisputeStore, localIso, shortStamp, viewOf } from './tenant-dispute.store';

describe('TenantDisputeStore', () => {
  let store: TenantDisputeStore;

  beforeEach(() => {
    store = TestBed.inject(TenantDisputeStore);
  });

  it('has one sample dispute per state, newest first', () => {
    expect(store.disputes().map(viewOf)).toEqual(['waiting', 'kept', 'corrected']);
    expect(store.waitingCount()).toBe(1);
  });

  it('keeps the corrected water dispute consistent with the invoice ledger', () => {
    const d = store.byId('d-204-water-11')!;
    const usage = d.correctedValue! - d.previousValue!;
    expect(usage).toBe(4);
    expect(d.totalBefore! - (6 - usage) * d.rate!).toBe(d.totalAfter);
    expect(d.totalAfter).toBe(3_209_000);
  });

  it('creates a waiting dispute at the top of the list', () => {
    const created = store.create(
      {
        invoiceId: 'inv-204-2026-11', readingId: 'r-204-ELECTRICITY-2026-11', subject: 'ELECTRICITY', period: '2026-11',
        proposedValue: 1280, recordedValue: 1284, previousValue: 1102, photoUrl: 'sample/elec-204.jpg', rate: 3500, totalBefore: 3_209_000,
        reason: '  Công tơ trong phòng ghi 1280.  ',
      },
      new Date(2026, 11, 5, 8, 30),
    );
    expect(created.status).toBe('OPEN');
    expect(created.reason).toBe('Công tơ trong phòng ghi 1280.');
    expect(created.createdAt.startsWith('2026-12-05T08:30:00')).toBe(true);
    expect(store.disputes()[0]).toBe(created);
    expect(store.waitingCount()).toBe(2);
    expect(store.byId(created.disputeId)).toBe(created);
  });

  it('formats timeline stamps', () => {
    expect(shortStamp(SAMPLE_DISPUTES.find((d) => d.disputeId === 'd-204-water-11')!.createdAt)).toBe('14:20, 03/12');
    expect(localIso(new Date(2026, 0, 2, 3, 4))).toMatch(/^2026-01-02T03:04:00[+-]\d{2}:\d{2}$/);
  });
});
