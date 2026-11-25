import { TestBed } from '@angular/core/testing';

import { RoomsStore, addHoursVn, isValidEmail, matchesQuery } from './rooms.store';

describe('RoomsStore', () => {
  let store: RoomsStore;

  beforeEach(() => {
    store = TestBed.inject(RoomsStore);
  });

  it('counts the rooms like LL-03', () => {
    expect(store.counts()).toEqual({ all: 36, occupied: 32, vacant: 4, awaiting: 1 });
  });

  it('matches the period story: 19 pending, 3 abnormal, 2 unreadable, 5 unrecorded, 3 approved', () => {
    const tally: Record<string, number> = {};
    for (const r of store.rooms()) if (r.reading) tally[r.reading] = (tally[r.reading] ?? 0) + 1;
    expect(tally).toEqual({ pending: 19, abnormal: 3, unreadable: 2, unrecorded: 5, approved: 3 });
  });

  it('searches by room code or tenant name without accents', () => {
    const p204 = store.byId('r-204')!;
    expect(matchesQuery(p204, '204')).toBe(true);
    expect(matchesQuery(p204, 'tran minh')).toBe(true);
    expect(matchesQuery(p204, 'P.305')).toBe(false);
  });

  it('sends an invite valid for 72 hours and marks the room as waiting', () => {
    const invite = store.invite('r-209', ' ngoc.han@vidu.vn ', '2026-12-01');
    expect(invite).toEqual({ email: 'ngoc.han@vidu.vn', startDate: '2026-12-01', expiresAt: '2026-12-05T09:00:00+07:00' });
    expect(store.byId('r-209')!.displayStatus).toBe('AWAITING_CONFIRM');
    expect(store.counts().awaiting).toBe(2);
  });

  it('moves a tenant: old room empty, new room rented with a contract awaiting confirmation', () => {
    store.endTenancy('r-204', 'r-302', '2026-12-15');
    expect(store.byId('r-204')).toMatchObject({ status: 'VACANT', tenantName: null });
    expect(store.byId('r-302')).toMatchObject({ status: 'OCCUPIED', tenantName: 'Trần Minh Anh', contract: 'AWAITING_TENANT' });
  });

  it('validates email and adds hours in Vietnam time', () => {
    expect(isValidEmail('a@b.vn')).toBe(true);
    expect(isValidEmail('a@b')).toBe(false);
    expect(addHoursVn('2026-12-02T23:30:00+07:00', 1)).toBe('2026-12-03T00:30:00+07:00');
  });
});
