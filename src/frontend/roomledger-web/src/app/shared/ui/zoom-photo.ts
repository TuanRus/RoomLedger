import { NgOptimizedImage } from '@angular/common';
import { Component, input, linkedSignal } from '@angular/core';

import { Icon } from './icon';

/**
 * An original meter photo that can be zoomed to 220% and panned, the same for landlord and tenant,
 * so both sides check the digits the same way. Optional tag in the top corner, e.g. "Ảnh gốc · bạn chụp".
 */
@Component({
  selector: 'app-zoom-photo',
  imports: [NgOptimizedImage, Icon],
  template: `
    <div
      class="frame"
      [class.zoomed]="zoomed()"
      [attr.tabindex]="zoomed() ? 0 : null"
      [attr.aria-label]="zoomed() ? 'Ảnh phóng to, kéo để xem' : null"
    >
      <img [ngSrc]="src()" width="640" height="480" [alt]="alt()" />
    </div>
    @if (tag(); as t) {
      <span class="tag">{{ t }}</span>
    }
    <button
      type="button"
      class="zoom"
      [attr.aria-pressed]="zoomed()"
      [attr.aria-keyshortcuts]="shortcut()"
      (click)="toggle()"
    >
      <app-icon name="i-search" /><span>{{ zoomed() ? 'Thu nhỏ' : 'Phóng to' }}</span>
    </button>
  `,
  styles: `
    :host {
      display: block;
      position: relative;
      border-radius: 12px;
      overflow: hidden;
      background: #1c1b19;
    }
    .frame img {
      width: 100%;
      height: auto;
      display: block;
    }
    .frame.zoomed {
      overflow: auto;
      max-height: min(70vh, 640px);
    }
    .frame.zoomed img {
      width: 220%;
      max-width: none;
    }
    .frame:focus-visible {
      outline: 2px solid var(--focus);
      outline-offset: -2px;
    }
    .tag {
      position: absolute;
      left: 10px;
      top: 10px;
      background: rgb(255 255 255 / 0.94);
      border-radius: 8px;
      padding: 5px 9px;
      font: 500 13px var(--sans);
      color: var(--ink);
      pointer-events: none;
    }
    .zoom {
      position: absolute;
      right: 10px;
      bottom: 10px;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      min-height: 44px;
      padding: 0 14px;
      border: 0;
      border-radius: var(--r-full);
      background: rgb(18 17 16 / 0.78);
      color: #fff;
      font: 500 var(--t-14) var(--sans);
      cursor: pointer;
    }
    .zoom:focus-visible {
      outline: 2px solid #fff;
      outline-offset: 2px;
    }
  `,
})
export class ZoomPhoto {
  readonly src = input.required<string>();
  readonly alt = input.required<string>();
  readonly tag = input<string | null>(null);
  /** Keyboard shortcut announced on the zoom button when the page provides one, e.g. "Z". */
  readonly shortcut = input<string | null>(null);

  /** Starts unzoomed for every new photo. */
  readonly zoomed = linkedSignal({ source: this.src, computation: () => false });

  toggle(): void {
    this.zoomed.update((z) => !z);
  }
}
