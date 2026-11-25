import { TestBed } from '@angular/core/testing';

import { ContractFile, contractChip, endDateFor, fileSize, latest, scanFileError, termMonths } from './contracts.model';
import { ContractsStore, changedNote } from './contracts.store';

const file = (id: string): ContractFile => ({ fileId: id, fileName: `${id}.pdf`, contentType: 'application/pdf', size: 1000, url: `blob:${id}`, local: true });

describe('contracts model', () => {
  it('computes term end dates and months', () => {
    expect(endDateFor('2026-06-01', 12)).toBe('2027-05-31');
    expect(endDateFor('2026-01-31', 1)).toBe('2026-03-02');
    expect(termMonths('2026-06-01', '2027-05-31')).toBe(12);
    expect(termMonths('2026-11-15', '2027-11-14')).toBe(12);
  });

  it('accepts PDF and photos up to 20 MB only', () => {
    expect(scanFileError({ name: 'a.pdf', type: 'application/pdf', size: 1000 })).toBeNull();
    expect(scanFileError({ name: 'a.HEIC', type: '', size: 1000 })).toBeNull();
    expect(scanFileError({ name: 'a.docx', type: 'application/msword', size: 1000 })).toContain('chỉ nhận');
    expect(scanFileError({ name: 'big.jpg', type: 'image/jpeg', size: 21 * 1024 * 1024 })).toContain('20 MB');
    expect(fileSize(1_887_000)).toBe('1,8 MB');
  });

  it('names changed terms', () => {
    const t = { monthlyRent: 1, deposit: 1, startDate: 'a', endDate: 'b', electricityRate: 1, waterRate: 1 };
    expect(changedNote(t, { ...t, deposit: 2 })).toBe('sửa tiền cọc');
    expect(changedNote(t, t)).toBeNull();
  });
});

describe('ContractsStore', () => {
  let store: ContractsStore;
  beforeEach(() => (store = TestBed.inject(ContractsStore)));

  it('shows P.305 as reported wrong by the tenant while version 2 is a draft', () => {
    expect(contractChip(store.byId('t-305')!).label).toBe('Người thuê báo không đúng');
  });

  it('replaces the pages of a draft, then sending makes it wait for the tenant', () => {
    store.uploadScan('t-305', [file('x')], '2026-11-24');
    let c = store.byId('t-305')!;
    expect(c.versions).toHaveLength(2);
    expect(latest(c)?.files.map((f) => f.fileId)).toEqual(['x']);

    store.send('t-305', latest(c)!.terms, '2026-11-24');
    c = store.byId('t-305')!;
    expect(latest(c)).toMatchObject({ status: 'AWAITING_CONFIRM', sentAt: '2026-11-24', note: 'sửa tiền cọc' });
    expect(contractChip(c).label).toBe('Chờ người thuê xác nhận');
  });

  it('creates a new draft version when the latest one is locked', () => {
    store.uploadScan('t-101', [file('y')], '2026-11-24');
    const v = latest(store.byId('t-101')!)!;
    expect(v).toMatchObject({ number: 2, type: 'ADDENDUM', status: 'UPLOADED' });
    expect(v.terms.monthlyRent).toBe(2_500_000);
  });
});
