import { Service, computed, signal } from '@angular/core';

import { MeterKind } from '../../../core/data/models';
import type { components } from '../../../generated/api-types';

type Schemas = components['schemas'];
export type DisputeStatus = Schemas['DisputeStatus'];
export type DisputeResolution = Schemas['DisputeResolution'];

/** What the tenant disputes. "OTHER" has no reading (not supported by CreateDisputeRequest yet, see API gaps). */
export type DisputeSubject = MeterKind | 'OTHER';

/**
 * DisputeResponse with required fields, plus what the dispute screens show that the contract does not carry yet:
 * subject and period, the value the tenant proposes, the reading before/after, the meter photo,
 * the rate and the invoice total before/after a correction.
 */
export interface TenantDispute {
  disputeId: string;
  invoiceId: string;
  readingId: string | null;
  reason: string;
  status: DisputeStatus;
  resolution: DisputeResolution | null;
  response: string | null;
  createdAt: string;
  resolvedAt: string | null;
  // Not in DisputeResponse yet:
  subject: DisputeSubject;
  period: string;
  proposedValue: number | null;
  recordedValue: number | null;
  previousValue: number | null;
  correctedValue: number | null;
  photoUrl: string | null;
  rate: number | null;
  totalBefore: number | null;
  totalAfter: number | null;
}

export type DisputeDraft = Pick<
  TenantDispute,
  'invoiceId' | 'readingId' | 'subject' | 'period' | 'proposedValue' | 'recordedValue' | 'previousValue' | 'photoUrl' | 'rate' | 'totalBefore'
> & { reason: string };

/** How a dispute reads to the tenant; derived, never stored. */
export type DisputeView = 'waiting' | 'corrected' | 'kept';

export function viewOf(d: TenantDispute): DisputeView {
  if (d.status === 'OPEN') return 'waiting';
  return d.resolution === 'CORRECTED' ? 'corrected' : 'kept';
}

export function subjectLabel(subject: DisputeSubject): string {
  return subject === 'WATER' ? 'Nước' : subject === 'ELECTRICITY' ? 'Điện' : 'Khác';
}

export function unitOf(subject: DisputeSubject): string {
  return subject === 'WATER' ? 'm³' : subject === 'ELECTRICITY' ? 'kWh' : '';
}

/** "2026-12-04T09:05:00+07:00" -> "09:05, 04/12" (timeline). */
export function shortStamp(iso: string): string {
  return `${iso.slice(11, 16)}, ${iso.slice(8, 10)}/${iso.slice(5, 7)}`;
}

/** "2026-12-04T09:05:00+07:00" -> "04/12/2026". */
export function dmy(iso: string): string {
  return iso.slice(0, 10).split('-').reverse().join('/');
}

/** Current time as "2026-12-04T20:15:00+07:00" in the browser's local offset (the API will return createdAt). */
export function localIso(date: Date): string {
  const p = (n: number) => String(Math.abs(n)).padStart(2, '0');
  const off = -date.getTimezoneOffset();
  return (
    `${date.getFullYear()}-${p(date.getMonth() + 1)}-${p(date.getDate())}T${p(date.getHours())}:${p(date.getMinutes())}:00` +
    `${off >= 0 ? '+' : '-'}${p(Math.trunc(off / 60))}:${p(off % 60)}`
  );
}

/**
 * Sample disputes of P.204 (Trần Minh Anh), one per state. The corrected water dispute is the story in
 * the mockups (TN-20 / TD-04); "waiting" and "kept" fill states the mockups do not draw.
 */
export const SAMPLE_DISPUTES: TenantDispute[] = [
  {
    disputeId: 'd-204-other-11', invoiceId: 'inv-204-2026-11', readingId: null, subject: 'OTHER', period: '2026-11',
    reason: 'Hóa đơn ghi hạn thanh toán 10/12 nhưng hợp đồng ghi hạn ngày 15 hằng tháng. Nhờ cô xem lại hạn giúp em.',
    status: 'OPEN', resolution: null, response: null, createdAt: '2026-12-04T20:15:00+07:00', resolvedAt: null,
    proposedValue: null, recordedValue: null, previousValue: null, correctedValue: null, photoUrl: null, rate: null, totalBefore: 3_209_000, totalAfter: null,
  },
  {
    disputeId: 'd-204-elec-11', invoiceId: 'inv-204-2026-11', readingId: 'r-204-ELECTRICITY-2026-11', subject: 'ELECTRICITY', period: '2026-11',
    reason: 'Tháng này em đi vắng 5 ngày mà số điện vẫn cao hơn mọi tháng. Em nghĩ số đúng là 1.248.',
    status: 'RESOLVED', resolution: 'KEPT',
    response: 'Cô phóng to ảnh gốc: công tơ ghi 01284, số rõ. Các tháng trước phòng dùng khoảng 165 kWh, tháng này trời nóng nên lên 182 kWh. Cô giữ nguyên.',
    createdAt: '2026-12-03T19:40:00+07:00', resolvedAt: '2026-12-04T08:30:00+07:00',
    proposedValue: 1248, recordedValue: 1284, previousValue: 1102, correctedValue: null, photoUrl: 'sample/elec-204.jpg', rate: 3_500,
    totalBefore: 3_245_000, totalAfter: null,
  },
  {
    disputeId: 'd-204-water-11', invoiceId: 'inv-204-2026-11', readingId: 'r-204-WATER-2026-11', subject: 'WATER', period: '2026-11',
    reason: 'Đồng hồ trong phòng ghi 65, phóng to ảnh cũng thấy số cuối là 5.',
    status: 'RESOLVED', resolution: 'CORRECTED', response: 'Cô phóng to ảnh gốc, đúng là 65. Cô đã sửa.',
    createdAt: '2026-12-03T14:20:00+07:00', resolvedAt: '2026-12-04T09:05:00+07:00',
    proposedValue: 65, recordedValue: 67, previousValue: 61, correctedValue: 65, photoUrl: 'sample/water-204.jpg', rate: 18_000,
    totalBefore: 3_245_000, totalAfter: 3_209_000,
  },
];

/**
 * The signed-in tenant's disputes. Sample data until the API is connected:
 * replace with the disputes in GET /invoices/{id} (or a tenant dispute list) and POST /invoices/{invoiceId}/disputes.
 */
@Service()
export class TenantDisputeStore {
  private readonly items = signal<TenantDispute[]>(SAMPLE_DISPUTES);
  private seq = 0;

  /** Newest first: samples are listed newest first and new disputes are added on top. */
  readonly disputes = this.items.asReadonly();
  readonly waitingCount = computed(() => this.items().filter((d) => d.status === 'OPEN').length);

  byId(disputeId: string | null | undefined): TenantDispute | null {
    return this.items().find((d) => d.disputeId === disputeId) ?? null;
  }

  /** Stands in for POST /invoices/{invoiceId}/disputes; the new dispute waits for the landlord. */
  create(draft: DisputeDraft, now: Date = new Date()): TenantDispute {
    this.seq += 1;
    const dispute: TenantDispute = {
      ...draft,
      reason: draft.reason.trim(),
      disputeId: `d-new-${this.seq}`,
      status: 'OPEN',
      resolution: null,
      response: null,
      createdAt: localIso(now),
      resolvedAt: null,
      correctedValue: null,
      totalAfter: null,
    };
    this.items.update((list) => [dispute, ...list]);
    return dispute;
  }
}
