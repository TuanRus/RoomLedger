import { Service, computed, signal } from '@angular/core';

import { MeterKind, ReviewRow, ReviewState } from './models';
import { ELECTRICITY_REVIEW, SAMPLE_PERIOD, WATER_REVIEW } from './sample-ledger';

/** Usage within this band of the 3-period average counts as normal and is offered for bulk approval. */
export const NORMAL_BAND = { min: -0.1, max: 0.1 };

export function usageOf(row: ReviewRow): number | null {
  const r = row.reading;
  return r?.value == null || r.previousValue == null ? null : r.value - r.previousValue;
}

/** Change against the 3-period average, e.g. 0.64 for +64%. */
export function deltaOf(row: ReviewRow): number | null {
  const usage = usageOf(row);
  return usage == null || row.averageUsage === 0 ? null : usage / row.averageUsage - 1;
}

export function stateOf(row: ReviewRow): ReviewState {
  const r = row.reading;
  if (!r) return 'unrecorded';
  if (r.status === 'CONFIRMED') return 'approved';
  if (r.reviewReason === 'AI_UNREADABLE' && r.value == null) return 'unreadable';
  if (r.needsReview) return 'abnormal';
  return 'pending';
}

/**
 * Landlord meter review state for one property and period.
 * Backed by sample data until the API is connected: replace load/approve/setValue with
 * GET /properties/{id}/meter-readings, POST …/meter-readings:confirm-all and PATCH /meter-readings/{id}.
 */
@Service()
export class MeterReviewStore {
  readonly period = SAMPLE_PERIOD;
  private readonly data = signal<Record<MeterKind, ReviewRow[]>>({
    ELECTRICITY: structuredClone(ELECTRICITY_REVIEW),
    WATER: structuredClone(WATER_REVIEW),
  });
  private lastApproved: { kind: MeterKind; ids: string[] } | null = null;

  readonly kind = signal<MeterKind>('ELECTRICITY');
  readonly rows = computed(() => this.data()[this.kind()]);

  readonly counts = computed(() => {
    const c: Record<ReviewState, number> = { pending: 0, abnormal: 0, unreadable: 0, unrecorded: 0, approved: 0 };
    for (const row of this.rows()) c[stateOf(row)]++;
    return c;
  });

  /** Pending readings inside the normal band: the default bulk selection. */
  readonly normalPendingIds = computed(() =>
    this.rows()
      .filter((row) => stateOf(row) === 'pending')
      .filter((row) => {
        const d = deltaOf(row);
        return d != null && d >= NORMAL_BAND.min && d <= NORMAL_BAND.max;
      })
      .map((row) => row.reading!.readingId),
  );

  approve(readingIds: string[]): number {
    const ids = new Set(readingIds);
    const kind = this.kind();
    this.patch(kind, (row) =>
      row.reading && ids.has(row.reading.readingId)
        ? { ...row, reading: { ...row.reading, status: 'CONFIRMED', needsReview: false, confirmedAt: new Date().toISOString() } }
        : row,
    );
    this.lastApproved = { kind, ids: [...ids] };
    return ids.size;
  }

  /** Reverts the most recent approve() while its undo toast is visible. */
  undoApprove(): void {
    const last = this.lastApproved;
    if (!last) return;
    const ids = new Set(last.ids);
    const original = new Map(
      (last.kind === 'ELECTRICITY' ? ELECTRICITY_REVIEW : WATER_REVIEW).map((r) => [r.reading?.readingId, r.reading]),
    );
    this.patch(last.kind, (row) =>
      row.reading && ids.has(row.reading.readingId)
        ? { ...row, reading: { ...row.reading, status: original.get(row.reading.readingId)?.status ?? 'PENDING_CONFIRM', needsReview: original.get(row.reading.readingId)?.needsReview ?? false, confirmedAt: null } }
        : row,
    );
    this.lastApproved = null;
  }

  /** Landlord corrects the number read from the photo. */
  setValue(readingId: string, value: number): void {
    this.patch(this.kind(), (row) =>
      row.reading?.readingId === readingId ? { ...row, reading: { ...row.reading, value, editedByLandlord: true } } : row,
    );
  }

  private patch(kind: MeterKind, fn: (row: ReviewRow) => ReviewRow): void {
    this.data.update((d) => ({ ...d, [kind]: d[kind].map(fn) }));
  }
}
