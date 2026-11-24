import { Component, computed, input } from '@angular/core';

import { InvoiceStatus, ReviewState } from '../../core/data/models';
import { Icon } from './icon';

type Tone = 'ok' | 'warn' | 'err' | 'info' | 'neu';
const REVIEW: Record<ReviewState, [Tone, string, string]> = {
  pending: ['warn', 'i-clock', 'Chờ duyệt'],
  abnormal: ['warn', 'i-alert', 'Bất thường'],
  unreadable: ['err', 'i-blur', 'Không đọc được'],
  unrecorded: ['neu', 'i-camera', 'Chưa ghi'],
  approved: ['ok', 'i-check', 'Đã duyệt'],
};
const INVOICE: Record<InvoiceStatus, [Tone, string, string]> = {
  DRAFT: ['neu', 'i-edit', 'Nháp'],
  SENT: ['warn', 'i-clock', 'Chưa thanh toán'],
  PAID: ['ok', 'i-check', 'Đã thanh toán'],
  DISPUTED: ['info', 'i-talk', 'Đang khiếu nại'],
};

/** Status always shown as icon plus text, never colour alone (PRODUCT.md accessibility). */
@Component({
  selector: 'app-status-chip',
  imports: [Icon],
  template: `<span class="chip {{ spec()[0] }}"><app-icon [name]="spec()[1]" />{{ text() ?? spec()[2] }}</span>`,
  host: { style: 'display:inline-flex' },
})
export class StatusChip {
  readonly review = input<ReviewState>();
  readonly invoice = input<InvoiceStatus>();
  /** Overrides the default label, e.g. "Hạn 10/12". */
  readonly text = input<string>();

  protected readonly spec = computed(() => {
    const r = this.review();
    return r ? REVIEW[r] : INVOICE[this.invoice() ?? 'DRAFT'];
  });
}
