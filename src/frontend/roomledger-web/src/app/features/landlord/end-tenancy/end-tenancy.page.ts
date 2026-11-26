import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, linkedSignal, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';

import { RoomsStore } from '../rooms/data/rooms.store';

import { MeterKind } from '../../../core/data/models';
import { Viewport } from '../../../core/layout/viewport';
import { DayPipe, NumPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { DateField } from '../../../shared/ui/date-field';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { LoadState } from '../shared/load-state';
import { EndKind, FinalReading, StepId } from './data/end-tenancy.models';
import { DEMO_FINAL } from './data/end-tenancy.sample';
import { EndTenancyApiStore, KINDS } from './data/end-tenancy.api.store';
import { EndTenancyStore, blockers, currentStep, finalInvoice, stepsDone } from './data/end-tenancy.store';

const STEP_TITLES: Record<StepId, string> = { 1: 'Hình thức và ngày', 2: 'Chỉ số cuối', 3: 'Hóa đơn cuối', 4: 'Xác nhận' };

/** Digits typed in an input as a number, null when empty. */
function digitsOf(event: Event): number | null {
  const digits = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 10);
  return digits === '' ? null : Number(digits);
}

/** "2026-12-15" -> "15/12/2026". */
function day(iso: string): string {
  return iso.split('-').reverse().join('/');
}

/**
 * LL-10: end a tenancy or move the tenant to another room. Four steps: kind and date, final readings,
 * final invoice (the landlord types last month's rent, no day split), confirm. On a phone one step shows at a time.
 */
@Component({
  selector: 'app-end-tenancy-page',
  imports: [NgOptimizedImage, NgTemplateOutlet, RouterLink, Icon, LoadState, MeterDigits, DateField, DayPipe, NumPipe, VndPipe],
  templateUrl: './end-tenancy.page.html',
  styleUrl: './end-tenancy.page.scss',
})
export class EndTenancyPage {
  /** Route parameter :roomId (component input binding). */
  readonly roomId = input.required<string>();

  protected readonly store = inject(EndTenancyStore);
  protected readonly flow = inject(EndTenancyApiStore);
  protected readonly kinds = KINDS;
  protected readonly viewport = inject(Viewport);
  protected readonly titles = STEP_TITLES;
  protected readonly stepIds: StepId[] = [1, 2, 3, 4];
  protected readonly day = day;


  private readonly rooms = inject(RoomsStore);

  protected readonly draft = computed(() => this.store.draft(this.roomId()));
  /** API mode: the form can start when the date is valid, not before the tenancy start, and a MOVE has a target room. */
  protected readonly canStart = computed(() => {
    const date = this.flow.endDate();
    const start = this.flow.tenancyStart();
    return /^\d{4}-\d{2}-\d{2}$/.test(date) && (!start || date >= start) && (this.flow.kind() === 'LEAVE' || !!this.flow.targetRoomId());
  });
  protected readonly roomStatus = this.rooms.status;
  protected readonly roomError = this.rooms.error;
  protected readonly invoice = computed(() => {
    const d = this.draft();
    return d ? finalInvoice(d) : null;
  });
  protected readonly blockers = computed(() => {
    const d = this.draft();
    return d ? blockers(d) : [];
  });
  protected readonly done = computed(() => {
    const d = this.draft();
    return d ? stepsDone(d) : { 1: false, 2: false, 3: false, 4: false };
  });
  protected readonly current = computed<StepId>(() => {
    const d = this.draft();
    return d ? currentStep(d) : 1;
  });
  protected readonly target = computed(() => this.store.target(this.draft()?.targetRoomId ?? null));
  protected readonly readings = computed<FinalReading[]>(() => {
    const d = this.draft();
    return d ? [d.electricity, d.water] : [];
  });
  /** "01–15/12/2026": the final period, from the 1st of the month to the end date. */
  protected readonly finalPeriod = computed(() => {
    const d = this.draft();
    if (!d) return '';
    const [y, m, dd] = d.endDate.split('-');
    return `01–${dd}/${m}/${y}`;
  });
  protected readonly confirmLabel = computed(() => {
    const d = this.draft();
    if (!d) return '';
    const t = this.target();
    return d.kind === 'MOVE' && t ? `Kết thúc thuê và chuyển sang ${t.code}` : `Kết thúc thuê ${d.roomCode}`;
  });

  /** Phone: the step on screen, starting at the first unfinished one. */
  protected readonly phoneStep = linkedSignal<string, StepId>({
    source: this.roomId,
    computation: () => untracked(this.current),
  });

  protected readonly toast = signal<string | null>(null);
  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.toastTimer && clearTimeout(this.toastTimer));
    effect(() => {
      const id = this.roomId();
      if (!this.store.sample) untracked(() => this.flow.open(id));
    });
  }


  protected onFlowTarget(event: Event): void {
    this.flow.targetRoomId.set((event.target as HTMLSelectElement).value || null);
  }

  protected onFlowRent(event: Event): void {
    this.flow.lastRent.set(digitsOf(event));
  }

  protected onStart(event: Event, meter: 'e' | 'w'): void {
    (meter === 'e' ? this.flow.startElectricity : this.flow.startWater).set(digitsOf(event));
  }

  protected onFlowPhoto(event: Event, kind: MeterKind): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.flow.uploadPhoto(kind, file);
    input.value = '';
  }

  protected approveFlow(kind: MeterKind, raw: string, replaced = false): void {
    const digits = raw.replace(/\D/g, '');
    if (digits) this.flow.approve(kind, Number(digits), replaced);
  }

  protected onReplaced(kind: MeterKind, replaced: boolean): void {
    this.flow.replacedNow.update((all) => ({ ...all, [kind]: replaced }));
  }

  protected readonly confirmingCancel = signal(false);

  protected cancelEnd(): void {
    this.confirmingCancel.set(false);
    this.flow.cancel();
  }

  protected kindLabel(kind: MeterKind): string {
    return kind === 'ELECTRICITY' ? 'Điện' : 'Nước';
  }

  protected demoValue(r: FinalReading): number {
    return r.previous + DEMO_FINAL[r.kind];
  }

  protected unit(kind: MeterKind): string {
    return kind === 'ELECTRICITY' ? 'kWh' : 'm³';
  }

  protected setKind(kind: EndKind): void {
    this.store.setKind(this.roomId(), kind);
  }

  protected onTarget(event: Event): void {
    this.store.setTarget(this.roomId(), (event.target as HTMLSelectElement).value);
  }

  protected onRent(event: Event): void {
    const digits = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 10);
    this.store.setRent(this.roomId(), digits === '' ? null : Number(digits));
  }

  protected onPhoto(event: Event, kind: MeterKind): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    this.store.receivePhoto(this.roomId(), kind);
    input.value = '';
    this.showToast(`Đã nhận ảnh ${kind === 'ELECTRICITY' ? 'điện' : 'nước'}. Kiểm tra số rồi duyệt.`);
  }

  protected approve(kind: MeterKind): void {
    this.store.approve(this.roomId(), kind);
  }

  protected recordDemo(kind: MeterKind): void {
    this.store.recordDemo(this.roomId(), kind);
  }

  protected saveDraft(): void {
    this.showToast('Đã lưu nháp. Bạn có thể quay lại sau.');
  }

  protected finish(): void {
    if (this.store.finish(this.roomId()) && this.viewport.isPhone()) this.phoneStep.set(4);
  }

  protected go(step: StepId): void {
    this.phoneStep.set(step);
  }

  private showToast(text: string): void {
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toast.set(text);
    this.toastTimer = setTimeout(() => this.toast.set(null), 4000);
  }
}
