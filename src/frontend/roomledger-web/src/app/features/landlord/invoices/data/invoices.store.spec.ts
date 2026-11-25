import { TestBed } from '@angular/core/testing';

import { LandlordInvoicesStore } from './invoices.store';

describe('LandlordInvoicesStore', () => {
  let store: LandlordInvoicesStore;

  beforeEach(() => {
    store = TestBed.inject(LandlordInvoicesStore);
  });

  it('matches the LL-05 sample: 27 invoices, 7 sent, 20 drafts worth 61.920.000 đ', () => {
    expect(store.counts()).toEqual({ all: 27, sent: 7, draft: 20, unpaid: 5, paid: 2, overdue: 2 });
    expect(store.draftTotal()).toBe(61_920_000);
  });

  it('every invoice adds up: rent + electricity + water', () => {
    for (const i of store.invoices()) {
      expect(i.total).toBe(i.roomRent + i.electricity.amount + i.water.amount + i.serviceFee);
    }
    expect(store.invoices().find((i) => i.roomCode === 'P.204')!.total).toBe(3_245_000);
    expect(store.invoices().find((i) => i.roomCode === 'P.112')!.total).toBe(3_108_000);
  });

  it('counts the kỳ 10 invoices as 22 days overdue on 02/12', () => {
    const p110 = store.invoices().find((i) => i.roomCode === 'P.110')!;
    expect(store.groupOf(p110)).toBe('overdue');
    expect(store.overdueDays(p110)).toBe(22);
  });

  it('sends all drafts (LL-05d)', () => {
    expect(store.send()).toBe(20);
    expect(store.counts()).toMatchObject({ draft: 0, unpaid: 25, sent: 27 });
    expect(store.lastBatch()?.count).toBe(20);
  });

  it('marks paid and undoes; a disputed invoice cannot be marked paid', () => {
    const p202 = store.invoices().find((i) => i.roomCode === 'P.202')!;
    expect(store.markPaid(p202.invoiceId)).toBe(true);
    expect(store.find(p202.invoiceId)!.status).toBe('PAID');
    store.unmarkPaid(p202.invoiceId);
    expect(store.find(p202.invoiceId)!.status).toBe('SENT');
    expect(store.markPaid('inv-204-2026-11')).toBe(false);
  });

  it('recalculates P.204 to 3.209.000 đ after the water correction 67 → 65', () => {
    expect(store.totalWith('inv-204-2026-11', 'WATER', 65)).toBe(3_209_000);
    store.applyCorrection('inv-204-2026-11', 'WATER', 65, '2026-12-04T09:30:00+07:00');
    const p204 = store.find('inv-204-2026-11')!;
    expect(p204.total).toBe(3_209_000);
    expect(p204.water.usage).toBe(4);
    expect(p204.status).toBe('SENT');
    expect(store.markPaid(p204.invoiceId)).toBe(true);
  });
});
