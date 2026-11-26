import { Component, computed, inject } from '@angular/core';

import { MeterKind } from '../../../../core/data/models';
import { Icon } from '../../../../shared/ui/icon';
import { MeterCaptureStore } from '../data/meter-capture.store';
import { KIND_LABEL, KIND_METER, KIND_UNIT, mb, pickedFile } from './capture-util';

/**
 * Phone camera step (LM-05 ready, LM-06 uploading, LM-07 AI reading). The shutter is a real
 * file input with capture="environment", so the phone opens its own camera app; a live
 * viewfinder would need getUserMedia and is not built.
 */
@Component({
  selector: 'app-capture-camera',
  imports: [Icon],
  templateUrl: './capture-camera.html',
  styleUrl: './capture-camera.scss',
})
export class CaptureCamera {
  protected readonly store = inject(MeterCaptureStore);
  protected readonly kindLabel = KIND_LABEL;
  protected readonly kindUnit = KIND_UNIT;
  protected readonly kindMeter = KIND_METER;
  protected readonly mb = mb;

  /** "Phòng 27/32": the active room's place in the round; it already counts once its photo is taken. */
  protected readonly position = computed(() => {
    const room = this.store.activeRoom();
    const counted = room != null && this.store.groupOf(room) !== 'todo';
    return Math.min(this.store.recordedCount() + (counted ? 0 : 1), this.store.total());
  });
  protected readonly percent = computed(() => this.store.total() ? Math.round((this.store.recordedCount() / this.store.total()) * 100) : 0);
  protected readonly pct = computed(() => Math.round(this.store.activeJob()?.progress ?? 0));
  protected readonly uploaded = computed(() => {
    const j = this.store.activeJob();
    return j ? (j.sizeBytes * j.progress) / 100 : 0;
  });

  protected setKind(kind: MeterKind): void {
    this.store.setKind(kind);
  }

  protected onPick(event: Event): void {
    const file = pickedFile(event);
    if (file) this.store.capture(file);
  }
}
