import { TestBed } from '@angular/core/testing';

import { OverviewStore, capturePercent, formatChange } from './overview.store';

describe('OverviewStore', () => {
  it('sums overdue invoices and capture progress like LL-02', () => {
    const store = TestBed.inject(OverviewStore);
    expect(store.overdueTotal()).toBe(6_485_000);
    expect(store.capturePercent()).toBe(84);
  });

  it('formats helpers', () => {
    expect(capturePercent(5, 0)).toBe(0);
    expect(formatChange(0.64)).toBe('+64%');
    expect(formatChange(-0.2)).toBe('−20%');
    expect(formatChange(null)).toBe('—');
  });
});
