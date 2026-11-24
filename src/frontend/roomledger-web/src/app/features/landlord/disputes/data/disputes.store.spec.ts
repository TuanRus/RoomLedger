import { TestBed } from '@angular/core/testing';

import { LandlordInvoicesStore } from '../../invoices/data/invoices.store';
import { LandlordDisputesStore } from './disputes.store';

describe('LandlordDisputesStore', () => {
  let store: LandlordDisputesStore;
  let invoices: LandlordInvoicesStore;

  beforeEach(() => {
    store = TestBed.inject(LandlordDisputesStore);
    invoices = TestBed.inject(LandlordInvoicesStore);
  });

  it('starts with 3 new and 1 answered, new ones first', () => {
    expect(store.counts()).toEqual({ open: 3, resolved: 1 });
    expect(store.ordered().map((d) => d.roomCode)).toEqual(['P.112', 'P.204', 'P.207', 'P.305']);
  });

  it('previews the P.112 recalculation 3.108.000 → 3.000.000 đ', () => {
    const d = store.find('dp-112-2026-11')!;
    expect(store.recalc(d, 142)).toEqual({ before: 3_108_000, after: 3_000_000 });
  });

  it('correcting resolves the dispute and recalculates the invoice', () => {
    expect(store.correct('dp-112-2026-11', 142)).toBe('ok');
    const d = store.find('dp-112-2026-11')!;
    expect(d.status).toBe('RESOLVED');
    expect(d.resolution).toBe('CORRECTED');
    expect(d.response).toContain('3.000.000');
    expect(invoices.find('inv-112-2026-11')!.total).toBe(3_000_000);
    expect(store.counts()).toEqual({ open: 2, resolved: 2 });
  });

  it('rejects a corrected value below the previous reading', () => {
    expect(store.correct('dp-112-2026-11', 100)).toBe('invalid-value');
  });

  it('keeping requires a reply and frees the invoice for payment', () => {
    expect(store.keep('dp-207-2026-11', '   ')).toBe('empty-reply');
    expect(store.keep('dp-207-2026-11', 'Công tơ vẫn chạy do tủ lạnh.')).toBe('ok');
    expect(store.find('dp-207-2026-11')!.response).toBe('Công tơ vẫn chạy do tủ lạnh.');
    expect(invoices.find('inv-207-2026-11')!.status).toBe('SENT');
    expect(store.keep('dp-207-2026-11', 'again')).toBe('not-open');
  });
});
