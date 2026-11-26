import { HttpClient } from '@angular/common/http';
import { Service, computed, inject, signal } from '@angular/core';
import { Observable, Subscription, concat, defer, forkJoin, from, last, map, of, switchMap, tap, throwError } from 'rxjs';

import type { components } from '../../../generated/api-types';
import { injectApi } from '../../../core/api/api-client';
import { LandlordPropertyStore, PropertyResponse, RoomResponse } from '../../../core/data/landlord-property.store';
import { QR_MAX_BYTES, putPresigned, qrType, sha256Base64 } from '../shared/upload';
import { PricingSettings } from './settings.model';

export type PaymentInfoResponse = components['schemas']['PaymentInfoResponse'];
type UploadUrlResponse = components['schemas']['UploadUrlResponse'];

/** Sample data, same story as the design mockups (Nhà trọ Cô Lan, 25/11/2026). Bank and QR are samples. */
const SAMPLE_SETTINGS: PricingSettings = {
  electricityRate: 3_500,
  waterRate: 18_000,
  serviceFee: 100_000,
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
const NO_DATA: Pick<PricingSettings, 'readingStartDay' | 'dueDay' | 'remindReading'> = {
  readingStartDay: 1,
  dueDay: 10,
  remindReading: false,
};

/** PropertyResponse, its rooms and the payment info -> the settings screen model (the rest has no API). */
export function settingsFromApi(property: PropertyResponse | null, rooms: RoomResponse[], payment: PaymentInfoResponse | null = null): PricingSettings {
  return {
    ...NO_DATA,
    // The API has no on/off switch: invoices show the details once bank, account number and holder are all set.
    payment: {
      enabled: true,
      bankName: payment?.bankName ?? '',
      accountNumber: payment?.accountNumber ?? '',
      accountHolder: payment?.accountHolder ?? '',
      transferNoteTemplate: payment?.transferNoteTemplate ?? '',
      qrImageUrl: payment?.qrUrl ?? null,
    },
    electricityRate: property?.electricityRate ?? 0,
    waterRate: property?.waterRate ?? 0,
    serviceFee: property?.serviceFee ?? 0,
    rents: rooms
      .map((r) => ({ roomId: r.roomId ?? '', roomCode: r.code ?? '', areaM2: null, monthlyRent: r.monthlyRent ?? 0, since: null }))
      .sort((a, b) => a.roomCode.localeCompare(b.roomCode, 'vi', { numeric: true })),
  };
}

/**
 * LL-06 settings for one property. Sample data by default. With environment.dataSource 'api' only what the API has
 * is real: the prices (PATCH /properties/{id}), each room's rent (PATCH /rooms/{id} with its version) and the bank
 * transfer details (GET/PUT /properties/{id}/payment-info) with the QR image (POST /uploads PROPERTY_QR, PUT, :confirm, DELETE).
 * Reading days, due day and the reminder have no endpoint.
 */
@Service()
export class SettingsStore {
  private readonly api = injectApi();
  private readonly property = inject(LandlordPropertyStore);
  private readonly http = inject(HttpClient);
  private paymentLoading: Subscription | null = null;

  /** True with sample data: the parts the API does not have may show. */
  readonly sample = !this.api;
  /** Loading state of the property and rooms the settings are read from. */
  readonly status = this.property.status;
  readonly error = this.property.error;
  readonly saveError = signal<string | null>(null);

  /** API: the bank transfer details of the property; null until read. */
  readonly paymentInfo = signal<PaymentInfoResponse | null>(null);
  readonly paymentError = signal<string | null>(null);
  /** QR upload: null when idle, 0 to 100 while sending and confirming. */
  readonly qrProgress = signal<number | null>(null);
  readonly qrError = signal<string | null>(null);
  /** The period the transfer note preview uses (the period the landlord works on). */
  readonly period = this.property.period;

  private readonly sampleSettings = signal<PricingSettings>(this.api ? settingsFromApi(null, []) : SAMPLE_SETTINGS);
  readonly settings = computed<PricingSettings>(() =>
    this.api ? settingsFromApi(this.property.property(), this.property.rooms(), this.paymentInfo()) : this.sampleSettings(),
  );

  /** Rooms with a tenant; a price change applies to all of them. */
  readonly occupiedRooms = computed(() => (this.api ? this.property.occupiedCount() : 32));
  /** First period that is not invoiced yet; changes apply from here. Sample data only. */
  readonly nextPeriod = '2026-12';

  constructor() {
    this.loadPayment();
  }

  reload(): void {
    this.property.load();
    this.loadPayment();
  }

  /** API: reads the payment info of the landlord's property, once the property is known. */
  private loadPayment(): void {
    const api = this.api;
    if (!api) return;
    this.paymentLoading?.unsubscribe();
    this.paymentError.set(null);
    this.paymentLoading = this.property
      .loadedProperty()
      .pipe(switchMap((p) => (p?.propertyId ? api.get<PaymentInfoResponse>(`/properties/${p.propertyId}/payment-info`) : of(null))))
      .subscribe({
        next: (info) => this.paymentInfo.set(info),
        error: (err: unknown) => this.paymentError.set(err instanceof Error ? err.message : 'Không tải được thông tin chuyển khoản.'),
      });
  }

  /** API: true when saving would clear bank details that exist (all three fields emptied): the page asks first. */
  clearsBank(next: PricingSettings): boolean {
    const b = next.payment;
    const had = this.settings().payment;
    return !!(had.bankName || had.accountNumber || had.accountHolder) && !b.bankName.trim() && !b.accountNumber.trim() && !b.accountHolder.trim();
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

  /**
   * API: saves what changed, in this order: prices and service fee (PATCH /properties/{id}), bank details (PUT payment-info;
   * DELETE payment-info when all three fields were emptied and details existed), room rents. Stops at the first failure and shows its message. After the prices are saved
   * the property store reloads, so every screen reads the new prices.
   */
  saveApi(next: PricingSettings): Observable<void> {
    const api = this.api;
    const propertyId = this.property.propertyId();
    if (!api || !propertyId) return of(undefined);
    this.saveError.set(null);
    const before = this.settings();
    const ratesChanged =
      next.electricityRate !== before.electricityRate || next.waterRate !== before.waterRate || next.serviceFee !== before.serviceFee;
    const bank = next.payment;
    const hasBank = bank.bankName.trim() !== '' || bank.accountNumber.trim() !== '' || bank.accountHolder.trim() !== '';
    const bankChanged =
      hasBank &&
      (bank.bankName.trim() !== before.payment.bankName ||
        bank.accountNumber.replace(/\s/g, '') !== before.payment.accountNumber ||
        bank.accountHolder.trim() !== before.payment.accountHolder ||
        bank.transferNoteTemplate.trim() !== before.payment.transferNoteTemplate);
    const steps: Observable<unknown>[] = [];
    if (ratesChanged) {
      steps.push(
        api.patch<PropertyResponse>(`/properties/${propertyId}`, { electricityRate: next.electricityRate, waterRate: next.waterRate, serviceFee: next.serviceFee }).pipe(
          tap((p) => this.property.property.update((cur) => ({ ...cur, ...p }))),
        ),
      );
    }
    if (bankChanged) {
      const template = bank.transferNoteTemplate.trim();
      steps.push(
        api
          .put<PaymentInfoResponse>(`/properties/${propertyId}/payment-info`, {
            bankName: bank.bankName.trim(),
            accountNumber: bank.accountNumber.trim(),
            accountHolder: bank.accountHolder.trim(),
            transferNoteTemplate: template === '' ? null : template,
          })
          .pipe(tap((info) => this.paymentInfo.set(info))),
      );
    }
    if (this.clearsBank(next)) {
      // All three fields emptied: remove the details and the QR (DELETE payment-info)
      steps.push(api.delete<PaymentInfoResponse>(`/properties/${propertyId}/payment-info`).pipe(tap((info) => this.paymentInfo.set(info))));
    }

    steps.push(defer(() => this.saveRents(next)));
    return concat(...steps).pipe(
      last(),
      tap(() => {
        if (ratesChanged) this.property.load();
      }),
      map(() => undefined),
      tap({ error: (err: unknown) => this.saveError.set(err instanceof Error ? err.message : 'Chưa lưu được, thử lại sau.') }),
    );
  }

  /**
   * API: sends a QR image. POST /uploads (PROPERTY_QR) -> PUT to the presigned URL -> POST payment-info/qr:confirm.
   * The old QR stays until the new one is confirmed. Emits the progress, 0 to 100 (100 once confirmed).
   */
  uploadQr(file: File): Observable<number> {
    const api = this.api;
    const propertyId = this.property.propertyId();
    const type = qrType(file);
    if (!api || !propertyId) return throwError(() => new Error('Chưa có nhà trọ để gắn mã QR.'));
    if (!type) return throwError(() => new Error('Chọn ảnh PNG, JPG hoặc WEBP.'));
    if (file.size > QR_MAX_BYTES) return throwError(() => new Error('Ảnh QR tối đa 2 MB.'));
    this.qrError.set(null);
    this.qrProgress.set(0);
    const confirm$ = defer(() => api.post<PaymentInfoResponse>(`/properties/${propertyId}/payment-info/qr:confirm`)).pipe(
      tap((info) => this.paymentInfo.set(info)),
      map(() => 100),
    );
    return from(sha256Base64(file)).pipe(
      switchMap((sha256) =>
        api.post<UploadUrlResponse>('/uploads', { targetType: 'PROPERTY_QR', targetId: propertyId, fileName: file.name, contentType: type, size: file.size, sha256 }),
      ),
      switchMap((upload) => concat(putPresigned(this.http, upload, file).pipe(map((p) => Math.min(p, 99))), confirm$)),
      tap({
        next: (p) => this.qrProgress.set(p),
        error: (err: unknown) => {
          this.qrProgress.set(null);
          this.qrError.set(err instanceof Error ? err.message : 'Chưa tải được ảnh QR, thử lại.');
        },
        complete: () => this.qrProgress.set(null),
      }),
    );
  }

  /** API: DELETE payment-info/qr. */
  removeQr(): Observable<void> {
    const api = this.api;
    const propertyId = this.property.propertyId();
    if (!api || !propertyId) return of(undefined);
    this.qrError.set(null);
    return api.delete<PaymentInfoResponse>(`/properties/${propertyId}/payment-info/qr`).pipe(
      tap({
        next: (info) => this.paymentInfo.set(info),
        error: (err: unknown) => this.qrError.set(err instanceof Error ? err.message : 'Chưa xóa được mã QR, thử lại.'),
      }),
      map(() => undefined),
    );
  }
}
