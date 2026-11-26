import { Component, inject, input, output } from '@angular/core';

import { LandlordPropertyStore } from '../../../core/data/landlord-property.store';
import { Icon } from '../../../shared/ui/icon';

/**
 * What a landlord page shows while its data loads from the API: "Đang tải…", an error with "Thử lại",
 * or "Chưa có nhà trọ nào." when the landlord has no property. Renders nothing with sample data.
 */
@Component({
  selector: 'app-load-state',
  imports: [Icon],
  template: `
    @if (status() === 'loading' || props.status() === 'loading') {
      <p class="muted" role="status">Đang tải…</p>
    }
    @if (error(); as message) {
      <p class="banner warn" role="alert"><app-icon name="i-alert" /><span>{{ message }}</span><button type="button" class="btn sec sm" (click)="retry.emit()">Thử lại</button></p>
    } @else if (props.error(); as message) {
      <p class="banner warn" role="alert"><app-icon name="i-alert" /><span>{{ message }}</span><button type="button" class="btn sec sm" (click)="props.load()">Thử lại</button></p>
    }
    @if (props.missing()) {
      <p class="muted" role="status">Chưa có nhà trọ nào.</p>
    }
  `,
})
export class LoadState {
  readonly status = input<'idle' | 'loading' | 'ready' | 'error'>('ready');
  readonly error = input<string | null>(null);
  readonly retry = output<void>();

  protected readonly props = inject(LandlordPropertyStore);
}
