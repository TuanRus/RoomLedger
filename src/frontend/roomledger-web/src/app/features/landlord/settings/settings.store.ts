import { Service, signal } from '@angular/core';

import { PricingSettings } from './settings.model';

/** Sample data, same story as the design mockups (Nhà trọ Cô Lan, 25/11/2026). Bank and QR are samples. */
const SAMPLE_SETTINGS: PricingSettings = {
  electricityRate: 3_500,
  waterRate: 18_000,
  readingStartDay: 1,
  dueDay: 10,
  remindReading: true,
  rents: [
    { roomId: 'r-101', roomCode: 'P.101', areaM2: 18, monthlyRent: 2_500_000, since: '2026-03' },
    { roomId: 'r-102', roomCode: 'P.102', areaM2: 18, monthlyRent: 2_500_000, since: '2026-03' },
    { roomId: 'r-201', roomCode: 'P.201', areaM2: 22, monthlyRent: 2_700_000, since: '2026-01' },
    { roomId: 'r-204', roomCode: 'P.204', areaM2: 20, monthlyRent: 2_500_000, since: '2026-03' },
    { roomId: 'r-209', roomCode: 'P.209', areaM2: 20, monthlyRent: 2_500_000, since: '2026-03' },
    { roomId: 'r-305', roomCode: 'P.305', areaM2: 24, monthlyRent: 2_800_000, since: '2026-06' },
  ],
  payment: {
    enabled: true,
    bankName: 'Ngân hàng mẫu',
    accountNumber: '0000 1234 5678',
    accountHolder: 'LE THI LAN',
    transferNoteTemplate: 'P[phòng] T[tháng]',
    qrImageUrl: 'sample/qr-mau.png',
  },
};

export const BANKS = ['Ngân hàng mẫu', 'Vietcombank', 'VietinBank', 'BIDV', 'Agribank', 'Techcombank', 'MB Bank', 'ACB', 'VPBank', 'Sacombank', 'TPBank'];

/** LL-06 settings for one property. No HTTP yet: the API has no endpoint for most of these fields. */
@Service()
export class SettingsStore {
  readonly settings = signal<PricingSettings>(SAMPLE_SETTINGS);
  /** Rooms with a tenant; a price change applies to all of them. */
  readonly occupiedRooms = 32;
  /** First period that is not invoiced yet; changes apply from here. */
  readonly nextPeriod = '2026-12';

  /** Saves settings. Rooms whose rent changed apply the new rent from the next period. */
  save(next: PricingSettings): void {
    const before = new Map(this.settings().rents.map((r) => [r.roomId, r.monthlyRent]));
    this.settings.set({
      ...next,
      rents: next.rents.map((r) => (before.get(r.roomId) === r.monthlyRent ? r : { ...r, since: this.nextPeriod })),
    });
  }
}
