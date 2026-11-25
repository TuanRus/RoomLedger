import { TestBed } from '@angular/core/testing';

import { TenantInvoiceStore, nextPeriod } from './tenant-invoice.store';

describe('TenantInvoiceStore', () => {
  let store: TenantInvoiceStore;
  beforeEach(() => (store = TestBed.inject(TenantInvoiceStore)));

  it('rolls periods over the year end', () => {
    expect(nextPeriod('2026-11')).toBe('2026-12');
    expect(nextPeriod('2026-12')).toBe('2027-01');
  });

  it('opens every invoice in the history, and totals add up', () => {
    for (const h of store.history()) {
      const d = store.detail(h.invoiceId)!;
      expect(d).not.toBeNull();
      expect(d.roomRent + d.electricity.amount + d.water.amount + d.serviceFee).toBe(d.total);
      expect(d.electricity.amount).toBe(d.electricity.usage * d.electricity.rate);
    }
  });

  it('counts one unpaid invoice', () => {
    expect(store.unpaidCount()).toBe(1);
  });

  it('points a paid invoice at the next invoice, and the newest at the meter schedule', () => {
    expect(store.nextAfter('inv-204-2026-10')).toEqual(expect.objectContaining({ kind: 'invoice', invoice: expect.objectContaining({ invoiceId: 'inv-204-2026-11' }) }));
    expect(store.nextAfter('inv-204-2026-11')).toEqual({ kind: 'schedule', period: '2026-12', window: '01–03/01/2027' });
    expect(store.nextAfter('nope')).toBeNull();
  });
});
