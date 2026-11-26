import { Service, computed, inject, signal } from '@angular/core';
import { Observable, forkJoin, map, of, tap } from 'rxjs';

import { injectApi } from '../../../core/api/api-client';
import { LandlordPropertyStore, PropertyResponse, RoomResponse } from '../../../core/data/landlord-property.store';
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

/** What the API has no field for: shown as "Chưa có dữ liệu" and never saved. The form needs a value, these are never displayed. */
const NO_DATA: Pick<PricingSettings, 'readingStartDay' | 'dueDay' | 'remindReading' | 'payment'> = {
  readingStartDay: 1,
  dueDay: 10,
  remindReading: false,
  payment: { enabled: false, bankName: '', accountNumber: '', accountHolder: '', transferNoteTemplate: '', qrImageUrl: null },
};

/** PropertyResponse and its rooms -> the settings screen model (prices and rents; the rest has no API). */
export function settingsFromApi(property: PropertyResponse | null, rooms: RoomResponse[]): PricingSettings {
  return {
    ...NO_DATA,
    electricityRate: property?.electricityRate ?? 0,
    waterRate: property?.waterRate ?? 0,
    rents: rooms
      .map((r) => ({ roomId: r.roomId ?? '', roomCode: r.code ?? '', areaM2: null, monthlyRent: r.monthlyRent ?? 0, since: null }))
      .sort((a, b) => a.roomCode.localeCompare(b.roomCode, 'vi', { numeric: true })),
  };
}

/**
 * LL-06 settings for one property. Sample data by default. With environment.dataSource 'api' only what the API has
 * is real: the prices (read-only, there is no PATCH /properties) and each room's rent (PATCH /rooms/{id} with its version).
 * Reading days, due day, reminder and the bank transfer details have no endpoint.
 */
@Service()
export class SettingsStore {
  private readonly api = injectApi();
  private readonly property = inject(LandlordPropertyStore);

  /** True with sample data: the parts the API does not have may show. */
  readonly sample = !this.api;
  /** Loading state of the property and rooms the settings are read from. */
  readonly status = this.property.status;
  readonly error = this.property.error;
  readonly saveError = signal<string | null>(null);

  private readonly sampleSettings = signal<PricingSettings>(this.api ? settingsFromApi(null, []) : SAMPLE_SETTINGS);
  readonly settings = computed<PricingSettings>(() =>
    this.api ? settingsFromApi(this.property.property(), this.property.rooms()) : this.sampleSettings(),
  );

  /** Rooms with a tenant; a price change applies to all of them. */
  readonly occupiedRooms = computed(() => (this.api ? this.property.occupiedCount() : 32));
  /** First period that is not invoiced yet; changes apply from here. Sample data only. */
  readonly nextPeriod = '2026-12';

  reload(): void {
    this.property.load();
  }

  /** Sample data: saves settings. Rooms whose rent changed apply the new rent from the next period. */
  save(next: PricingSettings): void {
    const before = new Map(this.settings().rents.map((r) => [r.roomId, r.monthlyRent]));
    this.sampleSettings.set({
      ...next,
      rents: next.rents.map((r) => (before.get(r.roomId) === r.monthlyRent ? r : { ...r, since: this.nextPeriod })),
    });
  }

  /** API: PATCH /rooms/{id} { monthlyRent, version } for each room whose rent changed. Emits how many were saved. */
  saveRents(next: PricingSettings): Observable<number> {
    const api = this.api;
    if (!api) return of(0);
    this.saveError.set(null);
    const before = new Map(this.settings().rents.map((r) => [r.roomId, r.monthlyRent]));
    const versions = this.property.roomById();
    const changed = next.rents.filter((r) => before.get(r.roomId) !== r.monthlyRent);
    if (!changed.length) return of(0);
    return forkJoin(
      changed.map((r) =>
        api.patch<RoomResponse>(`/rooms/${r.roomId}`, { monthlyRent: r.monthlyRent, version: versions.get(r.roomId)?.version ?? 0 }).pipe(
          tap((room) => this.property.updateRoom(room)),
        ),
      ),
    ).pipe(
      map((rooms) => rooms.length),
      tap({ error: (err: unknown) => {
        this.saveError.set(err instanceof Error ? err.message : 'Chưa lưu được tiền phòng, thử lại sau.');
        this.property.reloadRooms();
      } }),
    );
  }
}
