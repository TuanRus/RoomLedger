import { TestBed } from '@angular/core/testing';

import { TenantReadingsStore, datePart, tenantView } from './tenant-readings';

describe('TenantReadingsStore', () => {
  let store: TenantReadingsStore;
  beforeEach(() => (store = TestBed.inject(TenantReadingsStore)));

  it('derives readings from the invoices, newest first, chained period to period', () => {
    const water = store.list('WATER');
    expect(water.map((r) => r.value)).toEqual([67, 61, 56, 50]);
    expect(water[0].previousValue).toBe(water[1].value);
    expect(water.every((r) => r.status === 'CONFIRMED')).toBe(true);
  });

  it('hides the number and photo of a reading the landlord has not approved', () => {
    const [elec] = store.list('ELECTRICITY', true);
    expect(elec.status).toBe('PENDING_CONFIRM');
    expect(elec.value).toBeNull();
    expect(elec.photoUrl).toBeNull();
    expect(elec.capturedAt).toContain('2026-12-01');
    expect(store.list('ELECTRICITY', true)[1].value).toBe(1102);
  });

  it('tenantView leaves approved readings alone', () => {
    const r = store.list('WATER')[0];
    expect(tenantView(r)).toBe(r);
  });

  it('formats dates from the ISO string', () => {
    expect(datePart('2026-12-02T09:20:00+07:00')).toBe('02/12');
    expect(datePart('2026-12-02T09:20:00+07:00', true)).toBe('02/12/2026');
    expect(datePart(null)).toBe('—');
  });
});
