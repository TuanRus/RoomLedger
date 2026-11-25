import { NgOptimizedImage } from '@angular/common';
import { Component, computed, input, linkedSignal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MeterKind } from '../../core/data/models';
import { NumPipe } from '../pipes/format.pipes';
import { Icon } from './icon';

/**
 * The original meter photo with its reading, used inside the invoice photo dialogs (tenant and landlord).
 * Tap the photo to zoom in and pan; the tenant also gets "Khiếu nại chỉ số này" for that reading.
 */
@Component({
  selector: 'app-meter-photo',
  imports: [NgOptimizedImage, RouterLink, Icon, NumPipe],
  template: `
    @if (photoUrl(); as url) {
      <div class="wrap">
        <div class="frame" [class.zoomed]="zoomed()" [attr.tabindex]="zoomed() ? 0 : null" [attr.aria-label]="zoomed() ? 'Ảnh phóng to, kéo để xem' : null">
          <img [ngSrc]="url" width="640" height="480" [alt]="alt()" />
        </div>
        <button type="button" class="zoom" [attr.aria-pressed]="zoomed()" (click)="zoomed.set(!zoomed())">
          <app-icon name="i-search" /><span>{{ zoomed() ? 'Thu nhỏ' : 'Phóng to' }}</span>
        </button>
      </div>
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
    .wrap { position: relative; }
    .frame { border-radius: 12px; overflow: hidden; background: #1c1b19; }
    .frame img { width: 100%; height: auto; display: block; }
    .frame.zoomed { overflow: auto; max-height: min(70vh, 640px); }
    .frame.zoomed img { width: 220%; max-width: none; }
    .frame:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
    .zoom { position: absolute; right: 10px; bottom: 10px; display: inline-flex; align-items: center; gap: 6px;
      min-height: 44px; padding: 0 14px; border: 0; border-radius: var(--r-full); background: rgb(18 17 16 / .78); color: #fff;
      font: 500 var(--t-14) var(--sans); cursor: pointer; }
    .zoom:focus-visible { outline: 2px solid #fff; outline-offset: 2px; }
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
  /** Starts unzoomed for every new photo. */
  protected readonly zoomed = linkedSignal({ source: this.photoUrl, computation: () => false });
}
