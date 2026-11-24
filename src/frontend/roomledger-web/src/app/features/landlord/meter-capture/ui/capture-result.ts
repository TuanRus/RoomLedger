import { Component, ElementRef, Injector, afterNextRender, computed, inject, input, linkedSignal } from '@angular/core';

import { Icon } from '../../../../shared/ui/icon';
import { MeterDigits } from '../../../../shared/ui/meter-digits';
import { NumPipe, VndPipe } from '../../../../shared/pipes/format.pipes';
import { CAPTURE_PERIOD } from '../data/capture-round.sample';
import { MeterCaptureStore, costOf } from '../data/meter-capture.store';
import { KIND_LABEL, KIND_METER, KIND_UNIT, previousPeriod, shortStamp } from './capture-util';

/**
 * LM-08 (AI read a value: check, edit, approve and move on) and LM-09 (AI could not read the
 * photo: tips, retake, or type the value from the meter). Used full-screen on a phone and inside
 * the side panel on a computer.
 */
@Component({
  selector: 'app-capture-result',
  imports: [Icon, MeterDigits, NumPipe, VndPipe],
  templateUrl: './capture-result.html',
  styleUrl: './capture-result.scss',
})
export class CaptureResult {
  readonly layout = input<'phone' | 'desk'>('phone');

  protected readonly store = inject(MeterCaptureStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected readonly kindLabel = KIND_LABEL;
  protected readonly kindUnit = KIND_UNIT;
  protected readonly kindMeter = KIND_METER;
  protected readonly shortStamp = shortStamp;
  protected readonly prevPeriod = previousPeriod(CAPTURE_PERIOD);

  /** LM-09 "Tự nhập số từ đồng hồ" switches the unreadable screen to a manual entry form. */
  protected readonly manual = linkedSignal(() => {
    this.store.active();
    return false;
  });
  /** On LM-08 the digits turn into a text field once tapped. */
  protected readonly editing = linkedSignal(() => {
    this.store.active();
    return false;
  });
  protected readonly value = linkedSignal(() => this.store.activeJob()?.aiValue ?? null);

  protected readonly cost = computed(() => {
    const slot = this.store.activeSlot();
    return slot ? costOf(slot.kind, slot.previousValue, this.value()) : null;
  });
  protected readonly tooLow = computed(() => {
    const v = this.value();
    const slot = this.store.activeSlot();
    return v != null && !!slot && v < slot.previousValue;
  });

  protected confidence(c: number | null): string {
    return c == null ? '' : `${Math.round(c * 100)}%`;
  }

  protected onInput(event: Event): void {
    const digits = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 7);
    this.value.set(digits === '' ? null : Number(digits));
  }

  protected startEdit(): void {
    this.editing.set(true);
    this.focusValue();
  }

  protected startManual(): void {
    this.value.set(null);
    this.manual.set(true);
    this.focusValue();
  }

  protected approve(): void {
    const v = this.value();
    if (v == null || this.tooLow()) return;
    this.store.approve(v);
  }

  private focusValue(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLInputElement>('input.val')?.focus(), { injector: this.injector });
  }
}
