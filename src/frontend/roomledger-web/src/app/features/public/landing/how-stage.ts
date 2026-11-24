import { NgOptimizedImage } from '@angular/common';
import { Component } from '@angular/core';

import { ReviewState } from '../../../core/data/models';
import { TENANT_INVOICE } from '../../../core/data/sample-ledger';
import { NumPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { Receipt } from '../../../shared/ui/receipt';
import { StatusChip } from '../../../shared/ui/status-chip';

interface StageRow {
  room: string;
  previous: number;
  read: number | null;
  state: ReviewState;
}

/** Sample rows of the "duyệt cả dãy" step (LP-01). */
const STAGE_ROWS: StageRow[] = [
  { room: 'P.203', previous: 1810, read: 1954, state: 'approved' },
  { room: 'P.204', previous: 1102, read: 1284, state: 'pending' },
  { room: 'P.205', previous: 2402, read: 2590, state: 'pending' },
  { room: 'P.206', previous: 1330, read: null, state: 'unrecorded' },
];

/** LP-01 "how it works": photo → review the whole row → receipt, drawn with the real UI components. Stacks on narrow screens. */
@Component({
  selector: 'app-how-stage',
  imports: [NgOptimizedImage, Icon, MeterDigits, Receipt, StatusChip, NumPipe],
  template: `
    <h2 class="sr">Cách RoomLedger tính hóa đơn</h2>
    <div class="step">
      <div class="step-lbl"><b>1</b>Chủ nhà chụp</div>
      <div class="ph">
        <div class="photo"><img ngSrc="sample/elec-204.jpg" width="640" height="480" alt="Ảnh mẫu công tơ điện phòng P.204, hiển thị 01284 kWh"><span class="tag">Ảnh mẫu</span></div>
        <div class="read">
          <span class="chip info"><app-icon name="i-ai" />AI đọc</span>
          <div class="val"><app-meter-digits kind="ELECTRICITY" [value]="1284" size="lg" /><span class="muted">kWh</span></div>
          <p class="small muted">Điện · P.204 · kỳ 11/2026</p>
        </div>
      </div>
    </div>
    <div class="arrow" aria-hidden="true"><app-icon name="i-arrow" /></div>
    <div class="step">
      <div class="step-lbl"><b>2</b>Chủ nhà duyệt cả dãy</div>
      <div class="tablewrap">
        <table class="table">
          <thead><tr><th scope="col">Phòng</th><th scope="col" class="num prev">Kỳ trước</th><th scope="col" class="num">AI đọc</th><th scope="col" class="num prev">Tiêu thụ</th><th scope="col">Trạng thái</th></tr></thead>
          <tbody>
            @for (r of rows; track r.room) {
              <tr [class.sel]="r.room === 'P.204'">
                <td>@if (r.room === 'P.204') { <b>{{ r.room }}</b> } @else { {{ r.room }} }</td>
                <td class="num prev">{{ r.previous | num }}</td>
                <td class="num">@if (r.read != null) { <app-meter-digits kind="ELECTRICITY" [value]="r.read" size="sm" /> } @else { — }</td>
                <td class="num prev">@if (r.read != null) { {{ r.read - r.previous }} kWh } @else { — }</td>
                <td><app-status-chip [review]="r.state" /></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
    <div class="arrow" aria-hidden="true"><app-icon name="i-arrow" /></div>
    <div class="step">
      <div class="step-lbl"><b>3</b>Hóa đơn gửi người thuê</div>
      <app-receipt [invoice]="invoice" />
    </div>
  `,
  host: { role: 'region', 'aria-label': 'Cách hoạt động' },
  styles: `
    :host { margin: 64px var(--pad) 0; background: var(--soft); border-radius: 28px; padding: 48px; scroll-margin-top: 16px;
      display: grid; grid-template-columns: 280px 40px minmax(0, 1fr) 40px 360px; align-items: start; --notch: var(--soft); }
    .step { min-width: 0; }
    .arrow { display: grid; place-items: center; color: var(--ink-3); align-self: center; }
    .step-lbl { font: 500 15px/1.3 var(--sans); color: var(--ink-2); margin-bottom: 12px; display: flex; gap: 10px; align-items: baseline;
      border-top: 1px solid rgb(27 26 24 / .18); padding-top: 10px; }
    .step-lbl b { font: 500 28px/1 var(--sans); letter-spacing: -.04em; color: var(--ink); }
    .ph { background: #fff; border-radius: 28px; padding: 12px; box-shadow: var(--shadow); border: 1px solid var(--line); }
    .ph .photo { height: 250px; }
    .read { padding: 14px 6px 4px; }
    .val { margin: 10px 0 4px; display: flex; align-items: baseline; gap: 8px; }
    .tablewrap { box-shadow: var(--shadow); }
    @media (max-width: 1240px) {
      :host { grid-template-columns: minmax(0, 1fr); gap: 12px; padding: 32px; max-width: 640px; margin-inline: auto; }
      .arrow { transform: rotate(90deg); }
    }
    @media (max-width: 767px) {
      :host { margin: 40px var(--pad) 0; padding: 20px 16px; border-radius: 20px; }
      .ph .photo { height: 200px; }
      .prev { display: none; }
    }
  `,
})
export class HowStage {
  protected readonly rows = STAGE_ROWS;
  protected readonly invoice = TENANT_INVOICE;
}
