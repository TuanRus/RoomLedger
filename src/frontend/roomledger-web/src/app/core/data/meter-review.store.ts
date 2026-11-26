import { DestroyRef, Service, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription, forkJoin, map } from 'rxjs';

import type { components } from '../../generated/api-types';
import { injectApi } from '../api/api-client';
import { UnsavedChanges } from '../api/unsaved-changes';
import { LandlordPropertyStore, reviewPeriod } from './landlord-property.store';
import { MeterKind, MeterReading, ReviewRow, ReviewState } from './models';
import { ELECTRICITY_REVIEW, WATER_REVIEW } from './sample-ledger';

export { reviewPeriod };

type RoomReadingsResponse = components['schemas']['RoomReadingsResponse'];
type MeterReadingResponse = components['schemas']['MeterReadingResponse'];

/** Usage within this band of the 3-period average counts as normal and is offered for bulk approval. */
export const NORMAL_BAND = { min: -0.1, max: 0.1 };

/** Seconds an approval can be undone. With the API, the approval is only sent once this window closes. */
export const UNDO_SECONDS = 10;

export function usageOf(row: ReviewRow): number | null {
  const r = row.reading;
  return r?.value == null || r.previousValue == null ? null : r.value - r.previousValue;
}

/** Change against the 3-period average, e.g. 0.64 for +64%. */
export function deltaOf(row: ReviewRow): number | null {
  const usage = usageOf(row);
  return usage == null || row.averageUsage === 0 ? null : usage / row.averageUsage - 1;
}

/** How far usage may stray from the 3-period average before approving needs a second look. */
export const ABNORMAL_DELTA = 0.5;

export interface ValueCheck {
  usage: number | null;
  /** Usage against the 3-period average, e.g. 0.64 = +64%; null when there is no average. */
  delta: number | null;
  /** 'below-previous' blocks approval; 'abnormal' needs the landlord to confirm they checked the photo. */
  problem: 'below-previous' | 'abnormal' | null;
}

/** Checks a value the landlord is about to approve (AI value or typed), before it reaches the tenant. */
export function checkValue(row: ReviewRow, value: number | null): ValueCheck {
  const previous = row.reading?.previousValue;
  if (value == null || previous == null) return { usage: null, delta: null, problem: null };
  const usage = value - previous;
  const delta = row.averageUsage > 0 ? usage / row.averageUsage - 1 : null;
  if (usage < 0) return { usage, delta, problem: 'below-previous' };
  return { usage, delta, problem: delta != null && Math.abs(delta) > ABNORMAL_DELTA ? 'abnormal' : null };
}

export function stateOf(row: ReviewRow): ReviewState {
  const r = row.reading;
  if (!r) return 'unrecorded';
  if (r.status === 'CONFIRMED') return 'approved';
  // Photo taken but the AI has not answered yet: nothing to approve so far.
  if (r.status === 'PENDING_AI' && r.value == null) return 'unrecorded';
  // No number from the photo: AI_UNREADABLE in the sample, AI_FAILED / LOW_CONFIDENCE from the API.
  if (r.needsReview && r.value == null) return 'unreadable';
  if (r.needsReview) return 'abnormal';
  return 'pending';
}

/** States the landlord still has to act on, in the order they are worked through. */
const PRIORITY: Record<ReviewState, number> = { abnormal: 0, unreadable: 1, pending: 2, unrecorded: 3, approved: 4 };

export function needsAction(row: ReviewRow): boolean {
  return PRIORITY[stateOf(row)] <= PRIORITY.pending;
}

/** Problems first (abnormal, unreadable), then pending, unrecorded and approved; room order kept inside each group. */
export function byPriority(rows: ReviewRow[]): ReviewRow[] {
  return [...rows].sort((a, b) => PRIORITY[stateOf(a)] - PRIORITY[stateOf(b)]);
}

/**
 * Pending readings offered for bulk approval: usage within the normal band of the 3-period average.
 * Without an average (the API does not send one yet, see API-GAPS.md) any pending reading at or above the
 * previous value qualifies; the API already flags unusual readings as NEEDS_REVIEW.
 */
export function isNormalPending(row: ReviewRow): boolean {
  if (stateOf(row) !== 'pending') return false;
  const d = deltaOf(row);
  if (d != null) return d >= NORMAL_BAND.min && d <= NORMAL_BAND.max;
  const usage = usageOf(row);
  return row.averageUsage === 0 && usage != null && usage >= 0;
}

function toReading(r: MeterReadingResponse): MeterReading {
  return {
    readingId: r.readingId ?? '',
    roomId: r.roomId ?? '',
    kind: r.kind ?? 'ELECTRICITY',
    period: r.period ?? '',
    status: r.status ?? 'PENDING_AI',
    value: r.value ?? null,
    aiValue: r.aiValue ?? null,
    aiConfidence: r.aiConfidence ?? null,
    previousValue: r.previousValue ?? null,
    needsReview: r.needsReview ?? false,
    reviewReason: r.reviewReason ?? null,
    editedByLandlord: r.editedByLandlord ?? false,
    confirmedAt: r.confirmedAt ?? null,
    photoUrl: r.photoUrl ?? null,
  } as MeterReading;
}

/** RoomReadingsResponse -> one review row per meter kind. Fields the API lacks stay empty (API-GAPS.md). */
export function toReviewRows(rooms: RoomReadingsResponse[], kind: MeterKind): ReviewRow[] {
  return rooms.map((room) => {
    const r = kind === 'ELECTRICITY' ? room.electricity : room.water;
    return {
      roomId: room.roomId ?? '',
      roomCode: room.roomCode ?? '',
      tenantName: '',
      reading: r ? toReading(r) : null,
      digitCropUrl: null,
      averageUsage: 0,
      capturedAt: null,
      history: [],
    };
  });
}

interface PendingApproval {
  kind: MeterKind;
  /** The readings as they were before approve(), to put back on undo. */
  before: Map<string, MeterReading>;
  /** The readings as approved (with the number the landlord typed, if any): what is sent, whatever a reload brings in between. */
  approved: Map<string, MeterReading>;
  timer: ReturnType<typeof setTimeout> | null;
}

/**
 * Landlord meter review state for one property and period.
 * Sample data by default; with environment.dataSource 'api' it reads GET /properties/{id}/meter-readings
 * (first property of the landlord, period = last month) and saves approvals with PATCH /meter-readings/{id}
 * { value, confirm: true } once the undo window has passed, because a confirmed reading cannot be reopened.
 */
@Service()
export class MeterReviewStore {
  private readonly api = injectApi();
  private readonly property = inject(LandlordPropertyStore);

  /** True with sample data: nothing is loaded from the API. */
  readonly sample = !this.api;
  readonly period = this.property.period.asReadonly();
  readonly propertyId = signal<string | null>(null);
  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>(this.api ? 'idle' : 'ready');
  /** Last load or save error, in Vietnamese, for a banner. */
  readonly error = signal<string | null>(null);

  private readonly data = signal<Record<MeterKind, ReviewRow[]>>(
    this.api
      ? { ELECTRICITY: [], WATER: [] }
      : { ELECTRICITY: structuredClone(ELECTRICITY_REVIEW), WATER: structuredClone(WATER_REVIEW) },
  );
  private lastApproved: PendingApproval | null = null;
  /** Approvals whose PATCH is on its way: still shown as approved, whatever a reload brings, until the answer is in. */
  private readonly committing = new Set<PendingApproval>();
  private loading: Subscription | null = null;
  /** The readings revision this copy was loaded at. */
  private seenRevision = -1;

  /** Rows with the tenant's name joined from the room list (the readings response has no names). */
  private readonly named = computed(() => {
    const names = this.property.tenantNameByRoomId();
    const d = this.data();
    if (!names.size) return d;
    const join = (rows: ReviewRow[]) => rows.map((r) => ({ ...r, tenantName: names.get(r.roomId) ?? r.tenantName }));
    return { ELECTRICITY: join(d.ELECTRICITY), WATER: join(d.WATER) };
  });

  readonly kind = signal<MeterKind>('ELECTRICITY');
  readonly rows = computed(() => this.named()[this.kind()]);

  /** The rows of one meter kind, whatever kind the review screen shows. */
  rowsOf(kind: MeterKind): ReviewRow[] {
    return this.named()[kind];
  }

  readonly counts = computed(() => {
    const c: Record<ReviewState, number> = { pending: 0, abnormal: 0, unreadable: 0, unrecorded: 0, approved: 0 };
    for (const row of this.rows()) c[stateOf(row)]++;
    return c;
  });

  /** Abnormal and unreadable readings across electricity and water, problems first. */
  readonly problems = computed(() =>
    (Object.entries(this.named()) as [MeterKind, ReviewRow[]][])
      .flatMap(([kind, rows]) => rows.filter((r) => PRIORITY[stateOf(r)] < PRIORITY.pending).map((row) => ({ kind, row })))
      .sort((a, b) => PRIORITY[stateOf(a.row)] - PRIORITY[stateOf(b.row)]),
  );

  /** Readings still to act on across electricity and water, for the nav badge. */
  readonly attentionAll = computed(() => Object.values(this.named()).flat().filter(needsAction).length);

  /** Pending readings inside the normal band: the default bulk selection. */
  readonly normalPendingIds = computed(() => this.rows().filter(isNormalPending).map((row) => row.reading!.readingId));

  constructor() {
    // Closing or reloading the tab inside the undo window would lose the approval (a root service is only
    // destroyed with the whole app, never then), so the browser asks before leaving while one is held.
    inject(UnsavedChanges).register(() => this.lastApproved?.timer != null || this.committing.size > 0);
    // The app itself ending (tests, hot reload) still saves it.
    inject(DestroyRef).onDestroy(() => {
      this.loading?.unsubscribe();
      this.commitPending();
    });
    this.load(true);
    // Capture (or another screen) changed the readings: this copy and the nav badge are old, read again.
    if (this.api) {
      effect(() => {
        if (this.property.readingsRevision() !== this.seenRevision) untracked(() => this.load(true));
      });
    }
  }

  /**
   * (Re)loads the readings of the current period from the API; does nothing with sample data.
   * With `shared` the request another screen already made for the same readings is reused; otherwise the server is asked again.
   * A load still running is cancelled, so an older answer cannot overwrite a newer one.
   */
  load(shared = false): void {
    const api = this.api;
    if (!api) return;
    this.loading?.unsubscribe();
    if (!shared) this.property.invalidateReadings();
    this.seenRevision = this.property.readingsRevision();
    this.status.set('loading');
    this.error.set(null);
    this.loading = this.property
      .loadedReadings()
      .subscribe({
        next: (rooms) => {
          this.propertyId.set(this.property.propertyId());
          this.data.set(this.withPending({ ELECTRICITY: toReviewRows(rooms, 'ELECTRICITY'), WATER: toReviewRows(rooms, 'WATER') }));
          this.status.set('ready');
        },
        error: (err: unknown) => {
          this.error.set(err instanceof Error ? err.message : 'Không tải được chỉ số.');
          this.status.set('error');
        },
      });
  }

  /**
   * Approves the given readings. `value` is the number the landlord typed over the AI's, for a single reading:
   * it is applied together with the approval, so undoing puts the original number back too.
   */
  approve(readingIds: string[], value?: number): number {
    this.commitPending();
    const ids = new Set(readingIds);
    const kind = this.kind();
    const before = new Map<string, MeterReading>();
    const approved = new Map<string, MeterReading>();
    this.patch(kind, (row) => {
      if (!row.reading || !ids.has(row.reading.readingId)) return row;
      before.set(row.reading.readingId, row.reading);
      const typed = value != null && value !== row.reading.value ? { value, editedByLandlord: true } : {};
      const reading: MeterReading = { ...row.reading, ...typed, status: 'CONFIRMED', needsReview: false, confirmedAt: new Date().toISOString() };
      approved.set(reading.readingId, reading);
      return { ...row, reading };
    });
    const pending: PendingApproval = { kind, before, approved, timer: null };
    if (this.api) pending.timer = setTimeout(() => this.commitPending(), UNDO_SECONDS * 1000);
    this.lastApproved = pending;
    return ids.size;
  }

  /** Reverts the most recent approve() while its undo toast is visible; with the API nothing has been sent yet. */
  undoApprove(): void {
    const last = this.lastApproved;
    if (!last) return;
    if (last.timer) clearTimeout(last.timer);
    this.patch(last.kind, (row) => (row.reading && last.before.has(row.reading.readingId) ? { ...row, reading: last.before.get(row.reading.readingId)! } : row));
    this.lastApproved = null;
  }

  /** Landlord corrects the number read from the photo, without approving it yet. */
  setValue(readingId: string, value: number): void {
    this.patch(this.kind(), (row) =>
      row.reading?.readingId === readingId ? { ...row, reading: { ...row.reading, value, editedByLandlord: true } } : row,
    );
  }

  /** Sends the approval still inside its undo window, if any. */
  private commitPending(): void {
    const last = this.lastApproved;
    this.lastApproved = null;
    if (!last || !this.api) return;
    if (last.timer) clearTimeout(last.timer);
    const api = this.api;
    const approved = [...last.approved.values()];
    if (!approved.length) return;
    this.committing.add(last);
    forkJoin(approved.map((r) => api.patch<MeterReadingResponse>(`/meter-readings/${r.readingId}`, { value: r.value, confirm: true })))
      .pipe(map((saved) => saved.map(toReading)))
      .subscribe({
        next: (saved) => {
          this.committing.delete(last);
          const byId = new Map(saved.map((r) => [r.readingId, r]));
          this.patch(last.kind, (row) => (row.reading && byId.has(row.reading.readingId) ? { ...row, reading: byId.get(row.reading.readingId)! } : row));
          // The readings other screens hold (capture) are old now; this copy is already current.
          this.property.invalidateReadings();
          this.seenRevision = this.property.readingsRevision();
          // A load that began before this answer carries the old state: drop it and read once more.
          const reload = !!this.loading && !this.loading.closed;
          this.loading?.unsubscribe();
          if (reload) this.load(true);
        },
        error: (err: unknown) => {
          this.committing.delete(last);
          // Some PATCHes may have gone through: reload the truth, then keep the message on screen.
          this.load();
          this.error.set(`Chưa lưu được duyệt chỉ số: ${err instanceof Error ? err.message : 'lỗi không rõ'}`);
        },
      });
  }

  /** Puts the approval still held in its undo window over rows just read from the server, which do not know it yet. */
  private withPending(d: Record<MeterKind, ReviewRow[]>): Record<MeterKind, ReviewRow[]> {
    let out = d;
    for (const held of [...this.committing, ...(this.lastApproved ? [this.lastApproved] : [])]) {
      const base = out;
      out = {
        ...base,
        [held.kind]: base[held.kind].map((row) => {
          const reading = row.reading && held.approved.get(row.reading.readingId);
          return reading ? { ...row, reading } : row;
        }),
      };
    }
    return out;
  }

  private patch(kind: MeterKind, fn: (row: ReviewRow) => ReviewRow): void {
    this.data.update((d) => ({ ...d, [kind]: d[kind].map(fn) }));
  }
}
