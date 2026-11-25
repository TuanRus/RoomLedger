import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MeterKind } from '../../core/data/models';
import { NumPipe } from '../pipes/format.pipes';
import { Icon } from './icon';
import { ZoomPhoto } from './zoom-photo';

/**
 * The original meter photo with its reading, used inside the invoice photo dialogs (tenant and landlord).
 * Tap the photo to zoom in and pan; the tenant also gets "Khiếu nại chỉ số này" for that reading.
 */
@Component({
  selector: 'app-meter-photo',
  imports: [RouterLink, Icon, NumPipe, ZoomPhoto],
  template: `
    @if (photoUrl(); as url) {
      <app-zoom-photo [src]="url" [alt]="alt()" />
    } @else {
      <p class="muted">Chưa có ảnh cho chỉ số này.</p>
    }
    <p class="reading">
      {{ viewer() === 'tenant' ? 'Chủ nhà chụp và duyệt.' : 'Bạn chụp và duyệt.' }}
      Chỉ số: <b>{{ previous() | num }} → {{ current() | num }} {{ unit() }}</b>
    </p>
    @if (viewer() === 'tenant') {
      <p class="small muted">Ảnh lưu nguyên bản, không ai sửa được sau khi tải lên. So số trên ảnh với đồng hồ trong phòng nếu bạn thấy lạ.</p>
      @if (dispute(); as q) {
        <a class="btn sec block" routerLink="/tenant/disputes/new" [queryParams]="q"><app-icon name="i-talk" />Khiếu nại chỉ số này</a>
      }
    }
  `,
  styles: `
    :host { display: grid; gap: 10px; }
    .reading { margin: 0; font-size: var(--t-16); }
    p.small { margin: 0; }
  `,
})
export class MeterPhoto {
  readonly kind = input.required<MeterKind>();
  readonly photoUrl = input<string | null>(null);
  readonly previous = input.required<number>();
  readonly current = input.required<number>();
  readonly roomCode = input.required<string>();
  /** Who is looking: changes the wording, and only the tenant can dispute. */
  readonly viewer = input<'tenant' | 'landlord'>('tenant');
  /** Query params for /tenant/disputes/new, e.g. { invoice, reading }; omit to hide the link. */
  readonly dispute = input<Record<string, string> | null>(null);

  protected readonly unit = computed(() => (this.kind() === 'ELECTRICITY' ? 'kWh' : 'm³'));
  protected readonly alt = computed(
    () => `Ảnh gốc ${this.kind() === 'ELECTRICITY' ? 'công tơ điện' : 'đồng hồ nước'} ${this.roomCode()}, chỉ số ${this.current()} ${this.unit()}`,
  );
}
