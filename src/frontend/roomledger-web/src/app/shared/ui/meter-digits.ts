import { Component, computed, input } from '@angular/core';

import { MeterKind } from '../../core/data/models';

/**
 * A reading written the way the meter shows it: electricity as a green LCD, water as black drum cells.
 * Screen readers get the plain number.
 */
@Component({
  selector: 'app-meter-digits',
  template: `
    @if (kind() === 'ELECTRICITY') {
      <span class="lcd" [class.sm]="size() === 'sm'" [class.lg]="size() === 'lg'" role="img" [attr.aria-label]="label()">{{ padded() }}</span>
    } @else {
      <span class="drum" [class.sm]="size() === 'sm'" [class.lg]="size() === 'lg'" role="img" [attr.aria-label]="label()">
        @for (d of digits(); track $index) {
          <i aria-hidden="true">{{ d }}</i>
        }
      </span>
    }
  `,
  host: { style: 'display:inline-flex' },
})
export class MeterDigits {
  readonly kind = input.required<MeterKind>();
  readonly value = input.required<number>();
  readonly size = input<'sm' | 'md' | 'lg'>('md');

  protected readonly padded = computed(() => String(Math.trunc(this.value())).padStart(5, '0'));
  protected readonly digits = computed(() => this.padded().split(''));
  protected readonly label = computed(() => `${this.value()} ${this.kind() === 'ELECTRICITY' ? 'kWh' : 'm³'}`);
}
