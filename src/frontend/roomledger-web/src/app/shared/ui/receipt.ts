import { Component, computed, input, output } from '@angular/core';

import { InvoiceDetail, MeterKind } from '../../core/data/models';
import { NumPipe, PeriodPipe, VndPipe } from '../pipes/format.pipes';
import { Icon } from './icon';
import { MeterDigits } from './meter-digits';
import { StatusChip } from './status-chip';

/** The invoice drawn as a paper receipt; each utility line carries its readings and opens the meter photo. */
@Component({
  selector: 'app-receipt',
  imports: [Icon, MeterDigits, StatusChip, VndPipe, NumPipe, PeriodPipe],
  template: `
    @let inv = invoice();
    <div class="receipt" [attr.aria-label]="'Hóa đơn ' + inv.roomCode + ' kỳ ' + (inv.period | period)">
      <div class="rh"><span>Hóa đơn · {{ inv.roomCode }}</span><span>Kỳ {{ inv.period | period }}</span></div>
      <div class="rl"><div>Tiền phòng</div><span>{{ inv.roomRent | vnd }}</span></div>
      @for (line of lines(); track line.kind) {
        <div class="rl">
          <div>
            {{ line.label }} · {{ line.data.usage | num }} {{ line.unit }} × {{ line.data.rate | vnd }}
            <small>
              <button type="button" class="rdl" (click)="photo.emit(line.kind)" [attr.aria-label]="'Xem ảnh ' + line.photoName + ': ' + line.data.previous + ' đến ' + line.data.current + ' ' + line.unit">
                <app-meter-digits [kind]="line.kind" [value]="line.data.previous" size="sm" />→<app-meter-digits [kind]="line.kind" [value]="line.data.current" size="sm" />
                <app-icon name="i-image" size="sm" />
              </button>
            </small>
          </div>
          <span>{{ line.data.amount | vnd }}</span>
        </div>
      }
      @if (inv.serviceFee > 0) {
        <div class="rl"><div>Phí dịch vụ</div><span>{{ inv.serviceFee | vnd }}</span></div>
      }
      <div class="tot">
        @if (inv.status === 'SENT') {
          <app-status-chip invoice="SENT" [text]="'Hạn ' + dueShort()" />
        } @else {
          <app-status-chip [invoice]="inv.status" />
        }
        <span class="sum"><span class="small muted">{{ inv.status === 'PAID' ? 'Đã thanh toán' : 'Tổng' }}</span><b>{{ inv.total | vnd }}</b></span>
      </div>
    </div>
  `,
  styles: `
    :host { display: block; }
    .rl > span { white-space: nowrap; flex: none; }
    .rdl { display: inline-flex; align-items: center; gap: 6px; min-height: 32px; padding: 2px 8px 2px 4px; margin-left: -4px;
      border-radius: 8px; color: inherit; background: none; border: 1px solid var(--line); font: inherit; cursor: pointer; }
    .rdl:hover { border-color: var(--line-strong); background: var(--soft); }
    .rdl app-icon { color: var(--info); }
    .sum { text-align: right; }
    .sum .small { display: block; }
  `,
})
export class Receipt {
  readonly invoice = input.required<InvoiceDetail>();
  /** Emits which meter photo the tenant wants to open. */
  readonly photo = output<MeterKind>();

  protected readonly lines = computed(() => {
    const inv = this.invoice();
    return [
      { kind: 'ELECTRICITY' as const, label: 'Điện', unit: 'kWh', photoName: 'công tơ điện', data: inv.electricity },
      { kind: 'WATER' as const, label: 'Nước', unit: 'm³', photoName: 'đồng hồ nước', data: inv.water },
    ];
  });
  protected readonly dueShort = computed(() => {
    const due = this.invoice().dueDate;
    if (!due) return '—';
    const [, m, d] = due.split('-');
    return `${d}/${m}`;
  });
}
