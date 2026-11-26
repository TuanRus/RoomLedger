import { Service, computed, inject, signal } from '@angular/core';

import { injectApi } from '../../../../core/api/api-client';
import { LandlordPropertyStore, RoomResponse } from '../../../../core/data/landlord-property.store';
import { MeStore } from '../../../../core/data/me.store';
import { MeterReviewStore } from '../../../../core/data/meter-review.store';
import { LandlordDisputesStore } from '../../disputes/data/disputes.store';
import { LandlordInvoice } from '../../invoices/data/invoices.sample';
import { LandlordInvoicesStore } from '../../invoices/data/invoices.store';
import { MeterCaptureStore } from '../../meter-capture/data/meter-capture.store';
import { OVERVIEW_SAMPLE, OverviewData } from './overview.sample';

/** Whole percent of rooms recorded, clamped to 0–100. */
export function capturePercent(recorded: number, total: number): number {
  return total <= 0 ? 0 : Math.min(100, Math.max(0, Math.round((recorded / total) * 100)));
}

/** 0.64 -> "+64%", -0.2 -> "−20%". */
export function formatChange(change: number | null): string {
  if (change == null) return '—';
  const p = Math.round(change * 100);
  return p > 0 ? `+${p}%` : p < 0 ? `−${Math.abs(p)}%` : '0%';
}

/** Unpaid invoices (sent or disputed) of an earlier period than `period`, as the "quá hạn" list. */
export function overdueOf(invoices: LandlordInvoice[], period: string): OverviewData['overdue'] {
  return invoices
    .filter((i) => (i.status === 'SENT' || i.status === 'DISPUTED') && i.period < period)
    .map((i) => ({ roomCode: i.roomCode, period: i.period, amount: i.total, daysLate: null }))
    .sort((a, b) => a.period.localeCompare(b.period) || a.roomCode.localeCompare(b.roomCode, 'vi', { numeric: true }));
}

/**
 * Rooms whose contract waits for the tenant (displayStatus AWAITING_CONFIRM), for the "Hợp đồng" line of the work queue.
 * From the room list alone: the contracts themselves are only read when the contracts page opens.
 */
export function contractsNeedingAction(rooms: RoomResponse[]): OverviewData['summary']['contracts'] {
  const waiting = rooms.filter((r) => r.displayStatus === 'AWAITING_CONFIRM').map((r) => r.code ?? '').filter(Boolean);
  const shown = waiting.slice(0, 3).join(', ') + (waiting.length > 3 ? '…' : '');
  return { needAction: waiting.length, note: `${shown} chờ người thuê xác nhận` };
}

/**
 * Landlord dashboard (LL-02). Sample data by default; with environment.dataSource 'api' it has no request of its own:
 * it is derived from the me, property, invoices, disputes and capture stores.
 */
@Service()
export class OverviewStore {
  private readonly api = injectApi();
  private readonly me = inject(MeStore);
  private readonly property = inject(LandlordPropertyStore);
  private readonly invoices = inject(LandlordInvoicesStore);
  private readonly disputes = inject(LandlordDisputesStore);
  private readonly capture = inject(MeterCaptureStore);
  private readonly meters = inject(MeterReviewStore);

  /** True with sample data. */
  readonly sample = !this.api;
  private readonly sampleData = signal<OverviewData | null>(this.api ? null : OVERVIEW_SAMPLE);

  readonly overview = computed<OverviewData>(() => {
    const sample = this.sampleData();
    if (sample) return sample;
    const rooms = this.property.rooms();
    const previous = this.invoices.previousStats();
    return {
      landlordName: this.me.me()?.fullName ?? '',
      period: this.property.period(),
      captureWindow: null,
      paymentDue: null,
      capture: { recorded: this.capture.recordedCount(), total: this.capture.total(), deadline: null },
      overdue: overdueOf(this.invoices.invoices(), this.property.period()),
      summary: {
        rooms: { total: rooms.length, occupied: this.property.occupiedCount(), vacant: this.property.vacantCount() },
        lastPeriod: previous.total > 0 ? { ...previous, overdue: 0 } : null,
        contracts: contractsNeedingAction(rooms),
        disputes: { open: this.disputes.counts().open, note: null },
      },
    };
  });

  /** Loading while any store the dashboard reads from is still loading; an error from any of them. */
  readonly status = computed<'idle' | 'loading' | 'ready' | 'error'>(() => {
    if (!this.api) return 'ready';
    const all = [this.me.status(), this.property.status(), this.invoices.status(), this.capture.status(), this.meters.status()];
    if (all.includes('error')) return 'error';
    return all.includes('loading') || all.includes('idle') ? 'loading' : 'ready';
  });
  readonly error = computed<string | null>(() => {
    if (!this.api) return null;
    return this.me.error() ?? this.property.error() ?? this.invoices.error() ?? this.capture.error() ?? this.meters.error();
  });

  readonly capturePercent = computed(() => {
    const c = this.overview().capture;
    return capturePercent(c.recorded, c.total);
  });

  readonly overdueTotal = computed(() => this.overview().overdue.reduce((sum, i) => sum + i.amount, 0));

  /** Reads everything again (the "Thử lại" button). */
  reload(): void {
    this.me.load();
    this.property.load();
    this.invoices.load();
    // One request for the readings both screens show.
    this.property.invalidateReadings();
    this.capture.load(true);
    this.meters.load(true);
  }
}
