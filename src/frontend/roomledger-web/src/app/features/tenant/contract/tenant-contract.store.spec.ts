import { TestBed } from '@angular/core/testing';

import { TenantContractStore } from './tenant-contract.store';

describe('TenantContractStore', () => {
  let store: TenantContractStore;
  beforeEach(() => (store = TestBed.inject(TenantContractStore)));

  it('refuses a reason under 10 characters (after trimming)', () => {
    expect(store.reject('   sai cọc   ')).toBe(false);
    expect(store.decision().status).toBe('AWAITING_CONFIRM');
  });

  it('records a rejection with the trimmed reason', () => {
    expect(store.reject('  Tiền cọc sai số  ')).toBe(true);
    expect(store.decision()).toEqual(expect.objectContaining({ status: 'REJECTED', reason: 'Tiền cọc sai số' }));
  });

  it('confirms', () => {
    store.confirm();
    expect(store.decision().status).toBe('CONFIRMED');
  });
});
