import { TestBed } from '@angular/core/testing';

import {
  accountNumberError,
  dueDayError,
  formatMoney,
  fromForm,
  moneyError,
  parseMoney,
  sameForm,
  toForm,
  transferNoteError,
  transferNoteExample,
} from './settings.model';
import { SettingsStore } from './settings.store';

describe('settings model', () => {
  it('parses and formats money the way landlords type it', () => {
    expect(parseMoney('3.800')).toBe(3800);
    expect(parseMoney('2 600 000 đ')).toBe(2_600_000);
    expect(parseMoney('3,8')).toBeNull();
    expect(parseMoney('')).toBeNull();
    expect(formatMoney(2_600_000)).toBe('2.600.000');
  });

  it('validates money ranges with Vietnamese messages', () => {
    expect(moneyError('3.800', 'Giá điện', 500, 20_000)).toBeNull();
    expect(moneyError('', 'Giá điện', 500, 20_000)).toBe('Nhập giá điện.');
    expect(moneyError('abc', 'Giá điện', 500, 20_000)).toContain('chỉ gồm chữ số');
    expect(moneyError('100', 'Giá điện', 500, 20_000)).toBe('Giá điện tối thiểu 500 đ.');
    expect(moneyError('30.000', 'Giá điện', 500, 20_000)).toBe('Giá điện tối đa 20.000 đ.');
  });

  it('requires the due day after the 3-day reading window', () => {
    expect(dueDayError(1, 10)).toBeNull();
    expect(dueDayError(1, 3)).toContain('sau ngày 3');
    expect(dueDayError(8, 9)).not.toBeNull();
  });

  it('checks bank account number and transfer note template', () => {
    expect(accountNumberError('0000 1234 5678')).toBeNull();
    expect(accountNumberError('12a4')).toBe('Số tài khoản chỉ gồm chữ số.');
    expect(accountNumberError('123')).toContain('6 đến 20');
    expect(transferNoteError('P[phòng] T[tháng]')).toBeNull();
    expect(transferNoteError('Tien nha')).toContain('[phòng]');
    expect(transferNoteExample('P[phòng] T[tháng]', '204', 11)).toBe('P204 T11');
  });

  it('round-trips settings through the form and treats "3800" like "3.800"', () => {
    const s = TestBed.inject(SettingsStore).settings();
    const f = toForm(s);
    expect(sameForm(f, { ...f, electricityRate: '3500' })).toBe(true);
    expect(sameForm(f, { ...f, electricityRate: '3.800' })).toBe(false);
    const back = fromForm({ ...f, accountHolder: ' le thi lan ' }, s, s.payment.qrImageUrl);
    expect(back.payment.accountHolder).toBe('LE THI LAN');
    expect(back.electricityRate).toBe(3500);
  });
});

describe('SettingsStore', () => {
  it('moves the "applies from" period only for rooms whose rent changed', () => {
    const store = TestBed.inject(SettingsStore);
    const s = store.settings();
    store.save({ ...s, rents: s.rents.map((r) => (r.roomCode === 'P.209' ? { ...r, monthlyRent: 2_600_000 } : r)) });
    const rents = store.settings().rents;
    expect(rents.find((r) => r.roomCode === 'P.209')).toMatchObject({ monthlyRent: 2_600_000, since: '2026-12' });
    expect(rents.find((r) => r.roomCode === 'P.101')?.since).toBe('2026-03');
  });
});
