import { DOCUMENT } from '@angular/common';
import { Service, inject, signal } from '@angular/core';

/** Width below which the web app switches to its phone layout (bottom navigation, single column). */
export const PHONE_MAX_WIDTH = 767;

/**
 * RoomLedger is one responsive web app (decided 2026-11-24): the same routes render the
 * desktop layout on a computer and the phone layout on a small screen.
 */
@Service()
export class Viewport {
  private readonly query = inject(DOCUMENT).defaultView?.matchMedia(`(max-width: ${PHONE_MAX_WIDTH}px)`);

  /** True on a phone-sized screen. */
  readonly isPhone = signal(this.query?.matches ?? false);

  constructor() {
    this.query?.addEventListener('change', (event) => this.isPhone.set(event.matches));
  }
}
