import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MeterKind, ReviewRow, ReviewState } from '../../../core/data/models';
import { MeterReviewStore, checkValue, deltaOf, stateOf, usageOf } from '../../../core/data/meter-review.store';
import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe, StampPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { StatusChip } from '../../../shared/ui/status-chip';

type Filter = 'all' | ReviewState;

const FILTERS: { id: Filter; label: string }[] = [
  { id: 'all', label: 'Tất cả' },
  { id: 'pending', label: 'Chờ duyệt' },
  { id: 'abnormal', label: 'Bất thường' },
  { id: 'unreadable', label: 'Không đọc được' },
  { id: 'unrecorded', label: 'Chưa ghi' },
  { id: 'approved', label: 'Đã duyệt' },
];

const UNDO_SECONDS = 10;

/** LL-04 (computer) and LM-01/LM-02 (phone): review the meter readings of one period. */
@Component({
  selector: 'app-meter-review-page',
  imports: [NgOptimizedImage, NgTemplateOutlet, RouterLink, Icon, MeterDigits, StatusChip, NumPipe, PeriodPipe, StampPipe],
  templateUrl: './meter-review.page.html',
  styleUrl: './meter-review.page.scss',
})
export class MeterReviewPage {
  protected readonly store = inject(MeterReviewStore);
  protected readonly viewport = inject(Viewport);

  protected readonly filters = FILTERS;
  protected readonly filter = signal<Filter>('all');
  protected readonly stateOf = stateOf;
  protected readonly usageOf = usageOf;
  protected readonly deltaOf = deltaOf;

  protected readonly visibleRows = computed(() => {
    const f = this.filter();
    return f === 'all' ? this.store.rows() : this.store.rows().filter((r) => stateOf(r) === f);
  });

  protected readonly attention = computed(() => {
    const c = this.store.counts();
    return c.pending + c.abnormal + c.unreadable;
  });

  /** Bulk selection starts as the normal pending readings and resets when the data changes. */
  protected readonly checked = linkedSignal(() => new Set(this.store.normalPendingIds()));

  protected readonly selectedRoomId = linkedSignal<ReviewRow[], string | null>({
    source: this.store.rows,
    computation: (rows, previous) => {
      if (previous?.value && rows.some((r) => r.roomId === previous.value)) return previous.value;
      return this.viewport.isPhone() ? null : (rows.find((r) => stateOf(r) === 'abnormal') ?? rows[0])?.roomId ?? null;
    },
  });
  protected readonly selected = computed(() => this.store.rows().find((r) => r.roomId === this.selectedRoomId()) ?? null);

  /** Value typed in the detail panel, in case the photo shows a different number than AI read. */
  protected readonly editValue = linkedSignal(() => this.selected()?.reading?.value ?? null);

  /** Live check of the value about to be approved: usage, % against 3 periods, and whether it can be approved. */
  protected readonly check = computed(() => {
    const row = this.selected();
    return row ? checkValue(row, this.editValue()) : null;
  });
  /** An abnormal value needs "Tôi đã đối chiếu với ảnh" ticked; resets when the room or the value changes. */
  protected readonly confirmed = linkedSignal({ source: this.editValue, computation: () => false });
  protected readonly canApprove = computed(() => {
    const c = this.check();
    if (this.editValue() == null || !c || c.problem === 'below-previous') return false;
    return c.problem !== 'abnormal' || this.confirmed();
  });
  /** Unreadable photo: retake comes first; typing the number is opened on purpose. */
  protected readonly manualEntry = linkedSignal({ source: this.selectedRoomId, computation: () => false });

  protected readonly toast = signal<{ count: number; seconds: number } | null>(null);
  private toastTimer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearToastTimer());
  }

  protected setKind(kind: MeterKind): void {
    this.store.kind.set(kind);
    this.filter.set('all');
  }

  protected toggle(readingId: string): void {
    this.checked.update((set) => {
      const next = new Set(set);
      if (next.has(readingId)) next.delete(readingId);
      else next.add(readingId);
      return next;
    });
  }

  protected clearChecked(): void {
    this.checked.set(new Set());
  }

  protected confidence(value: number): string {
    return `${Math.round(value * 100)}%`;
  }

  protected approveChecked(): void {
    this.showToast(this.store.approve([...this.checked()]));
  }

  protected approveSelected(): void {
    const row = this.selected();
    const value = this.editValue();
    if (!row?.reading || value == null || !this.canApprove()) return;
    if (value !== row.reading.value) this.store.setValue(row.reading.readingId, value);
    this.showToast(this.store.approve([row.reading.readingId]));
    if (this.viewport.isPhone()) this.selectedRoomId.set(null);
  }

  protected undo(): void {
    this.store.undoApprove();
    this.closeToast();
  }

  protected closeToast(): void {
    this.clearToastTimer();
    this.toast.set(null);
  }

  protected onValueInput(event: Event): void {
    const digits = (event.target as HTMLInputElement).value.replace(/\D/g, '');
    this.editValue.set(digits === '' ? null : Number(digits));
  }

  protected percent(row: ReviewRow): string {
    return this.formatDelta(deltaOf(row));
  }

  protected formatDelta(d: number | null): string {
    if (d == null) return '—';
    const p = Math.round(d * 100);
    return p > 0 ? `+${p}%` : p < 0 ? `−${Math.abs(p)}%` : '0%';
  }

  protected unit(): string {
    return this.store.kind() === 'ELECTRICITY' ? 'kWh' : 'm³';
  }

  private showToast(count: number): void {
    this.clearToastTimer();
    this.toast.set({ count, seconds: UNDO_SECONDS });
    this.toastTimer = setInterval(() => {
      const t = this.toast();
      if (!t || t.seconds <= 1) this.closeToast();
      else this.toast.set({ ...t, seconds: t.seconds - 1 });
    }, 1000);
  }

  private clearToastTimer(): void {
    if (this.toastTimer) clearInterval(this.toastTimer);
    this.toastTimer = null;
  }
}
