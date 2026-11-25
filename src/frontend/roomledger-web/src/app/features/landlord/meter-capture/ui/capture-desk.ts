import { Component, computed, inject } from '@angular/core';

import { MeterKind } from '../../../../core/data/models';
import { Icon } from '../../../../shared/ui/icon';
import { MeterCaptureStore } from '../data/meter-capture.store';
import { CaptureResult } from './capture-result';
import { KIND_LABEL, KIND_METER, KIND_UNIT, mb, pickedFile } from './capture-util';

/**
 * Computer side panel for one meter: "Tải ảnh lên từ máy" runs the same upload → AI reading →
 * result flow as the phone camera (LM-06/LM-07 states, then LM-08/LM-09 via CaptureResult).
 */
@Component({
  selector: 'app-capture-desk',
  imports: [Icon, CaptureResult],
  templateUrl: './capture-desk.html',
  styleUrl: './capture-desk.scss',
  host: { class: 'desk' },
})
export class CaptureDesk {
  protected readonly store = inject(MeterCaptureStore);
  protected readonly kindLabel = KIND_LABEL;
  protected readonly kindUnit = KIND_UNIT;
  protected readonly kindMeter = KIND_METER;
  protected readonly mb = mb;

  protected readonly pct = computed(() => Math.round(this.store.activeJob()?.progress ?? 0));

  protected setKind(kind: MeterKind): void {
    this.store.setKind(kind);
  }

  protected onPick(event: Event): void {
    const file = pickedFile(event);
    if (file) this.store.capture(file);
  }
}
