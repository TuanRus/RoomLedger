import { Component, computed, input } from '@angular/core';

/** An icon from the design-system sprite (public/icons.svg), e.g. name="i-check". Decorative unless the parent labels it. */
@Component({
  selector: 'app-icon',
  template: `<svg class="i" [class.sm]="size() === 'sm'" aria-hidden="true" focusable="false"><use [attr.href]="href()" /></svg>`,
  host: { style: 'display:contents' },
})
export class Icon {
  readonly name = input.required<string>();
  readonly size = input<'md' | 'sm'>('md');
  protected readonly href = computed(() => `icons.svg#${this.name()}`);
}
