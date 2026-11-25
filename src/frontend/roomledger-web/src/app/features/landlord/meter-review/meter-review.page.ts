import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, Injector, afterNextRender, computed, inject, linkedSignal, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { MeterKind, ReviewRow, ReviewState } from '../../../core/data/models';
import { MeterReviewStore, byPriority, checkValue, deltaOf, needsAction, stateOf, usageOf } from '../../../core/data/meter-review.store';
import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe, StampPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { StatusChip } from '../../../shared/ui/status-chip';
import { ZoomPhoto } from '../../../shared/ui/zoom-photo';

type Filter = 'all' | ReviewState;

/** `empty` says what an empty filter means for this period, not just that the list is empty. */
const FILTERS: { id: Filter; label: string; empty: string }[] = [
  { id: 'all', label: 'Tất cả', empty: 'Chưa có phòng nào trong kỳ này.' },
  { id: 'pending', label: 'Chờ duyệt', empty: 'Không còn chỉ số bình thường chờ duyệt.' },
  { id: 'abnormal', label: 'Bất thường', empty: 'Không có chỉ số bất thường kỳ này.' },
  { id: 'unreadable', label: 'Không đọc được', empty: 'AI đọc được số trên mọi ảnh kỳ này.' },
  { id: 'unrecorded', label: 'Chưa ghi', empty: 'Đã ghi đủ mọi phòng kỳ này.' },
  { id: 'approved', label: 'Đã duyệt', empty: 'Chưa duyệt chỉ số nào. Bắt đầu từ phòng đầu danh sách.' },
];

const UNDO_SECONDS = 10;

/** LL-04 (computer) and LM-01/LM-02 (phone): review the meter readings of one period. */
@Component({
  selector: 'app-meter-review-page',
  imports: [NgOptimizedImage, NgTemplateOutlet, RouterLink, Icon, MeterDigits, StatusChip, ZoomPhoto, NumPipe, PeriodPipe, StampPipe],
  host: { '(document:keydown)': 'onKey($event)' },
  templateUrl: './meter-review.page.html',
  styleUrl: './meter-review.page.scss',
})
export class MeterReviewPage {
  protected readonly store = inject(MeterReviewStore);
  protected readonly viewport = inject(Viewport);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly photo = viewChild(ZoomPhoto);

  protected readonly filters = FILTERS;
  protected readonly emptyText = computed(() => FILTERS.find((f) => f.id === this.filter())?.empty ?? '');
  protected readonly filter = signal<Filter>('all');
  protected readonly stateOf = stateOf;
  protected readonly usageOf = usageOf;
  protected readonly deltaOf = deltaOf;

  /** Problems first, approved last, so the landlord works down the list from the top. */
  protected readonly visibleRows = computed(() => {
    const f = this.filter();
    const rows = byPriority(this.store.rows());
    return f === 'all' ? rows : rows.filter((r) => stateOf(r) === f);
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
  /** Room approved on its own, so undo can bring the landlord back to it. */
  private undoRoomId: string | null = null;
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
    this.undoRoomId = null;
    this.showToast(this.store.approve([...this.checked()]));
  }

  protected approveSelected(): void {
    const row = this.selected();
    const value = this.editValue();
    if (!row?.reading || value == null || !this.canApprove()) return;
    const at = this.visibleRows().findIndex((r) => r.roomId === row.roomId);
    if (value !== row.reading.value) this.store.setValue(row.reading.readingId, value);
    this.showToast(this.store.approve([row.reading.readingId]));
    this.undoRoomId = row.roomId;
    // Straight on to the next room below that needs a decision, wrapping to the top; on a phone, back to the list when none is left.
    const rows = this.visibleRows();
    const next = rows.slice(Math.max(at, 0)).find(needsAction) ?? rows.find(needsAction);
    if (next) this.select(next.roomId);
    else if (this.viewport.isPhone()) this.selectedRoomId.set(null);
  }

  protected undo(): void {
    this.store.undoApprove();
    if (this.undoRoomId) this.select(this.undoRoomId);
    this.undoRoomId = null;
    this.closeToast();
  }

  protected retake(row: ReviewRow): void {
    void this.router.navigate(['/landlord/meter-capture'], { queryParams: { room: row.roomId, meter: this.store.kind() } });
  }

  /**
   * Computer shortcuts: J/K next and previous room, A approve, R retake, Z zoom the photo; Enter in the number field approves.
   * Ignored while typing elsewhere, with a modifier held, or when a dialog is open.
   */
  protected onKey(e: KeyboardEvent): void {
    if (this.viewport.isPhone() || e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
    const target = e.target instanceof HTMLElement ? e.target : null;
    if (e.key === 'Enter' && target?.classList.contains('value-input')) {
      e.preventDefault();
      this.approveSelected();
      return;
    }
    const typing = !!target?.closest('input:not([type=checkbox]):not([type=radio]), textarea, select, [contenteditable="true"]');
    if (typing || document.querySelector('dialog[open]')) return;
    const row = this.selected();
    switch (e.key.toLowerCase()) {
      case 'j':
        this.step(1);
        break;
      case 'k':
        this.step(-1);
        break;
      case 'a':
        if (!row) return;
        if (this.canApprove()) this.approveSelected();
        else document.getElementById('v-' + row.roomId)?.focus();
        break;
      case 'r':
        if (!row || stateOf(row) === 'approved') return;
        this.retake(row);
        break;
      case 'z':
        if (!this.photo()) return;
        this.photo()!.toggle();
        break;
      default:
        return;
    }
    e.preventDefault();
  }

  private step(dir: 1 | -1): void {
    const rows = this.visibleRows();
    if (!rows.length) return;
    const at = rows.findIndex((r) => r.roomId === this.selectedRoomId());
    const next = at < 0 ? 0 : Math.min(rows.length - 1, Math.max(0, at + dir));
    this.select(rows[next].roomId);
  }

  /** Selects a room and keeps it in view: its table row on a computer, the top of the detail on a phone. */
  private select(roomId: string): void {
    this.selectedRoomId.set(roomId);
    const phone = this.viewport.isPhone();
    afterNextRender(() => (phone ? window.scrollTo({ top: 0 }) : document.querySelector('tr.sel')?.scrollIntoView({ block: 'nearest' })), {
      injector: this.injector,
    });
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
