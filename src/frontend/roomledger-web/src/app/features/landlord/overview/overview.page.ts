import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MeterReviewStore, deltaOf, stateOf } from '../../../core/data/meter-review.store';
import { Viewport } from '../../../core/layout/viewport';
import { PeriodPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { LandlordDisputesStore } from '../disputes/data/disputes.store';
import { LandlordInvoicesStore } from '../invoices/data/invoices.store';
import { OverviewStore, formatChange } from './data/overview.store';

/** One thing waiting on the landlord this period, with where to go to do it. */
export interface QueueItem {
  id: string;
  icon: string;
  title: string;
  detail: string;
  /** Short figure on the right: a count or "27/32"; use `money` for an amount. */
  value?: string;
  money?: number;
  link: string;
  tone: 'warn' | 'err' | 'neu';
  /** 0–100, drawn as a thin bar under the detail (meter capture). */
  progress?: number;
}

/** Up to three room codes, then "…", e.g. "P.307, P.308, P.113…". */
function roomList(codes: string[]): string {
  return codes.slice(0, 3).join(', ') + (codes.length > 3 ? '…' : '');
}

/**
 * LL-02: the landlord's work queue for this period, in the order the work is done. Each line is one job
 * and opens the screen that does it; a job with nothing waiting is left out. Counts match the nav badges.
 */
@Component({
  selector: 'app-landlord-overview-page',
  imports: [RouterLink, Icon, PeriodPipe, VndPipe],
  templateUrl: './overview.page.html',
  styleUrl: './overview.page.scss',
})
export class LandlordOverviewPage {
  protected readonly store = inject(OverviewStore);
  protected readonly viewport = inject(Viewport);
  private readonly meters = inject(MeterReviewStore);
  private readonly invoices = inject(LandlordInvoicesStore);
  private readonly disputes = inject(LandlordDisputesStore);

  protected readonly queue = computed<QueueItem[]>(() => {
    const o = this.store.overview();
    const items: QueueItem[] = [];

    const toReview = this.meters.attentionAll();
    if (toReview > 0) {
      const problems = this.meters.problems().map(({ kind, row }) => {
        const meter = kind === 'ELECTRICITY' ? 'điện' : 'nước';
        return `${row.roomCode} ${meter} ${stateOf(row) === 'abnormal' ? formatChange(deltaOf(row)) : 'ảnh mờ'}`;
      });
      const normal = toReview - problems.length;
      items.push({
        id: 'review',
        icon: 'i-meter',
        title: 'Duyệt chỉ số',
        detail: [problems.join(', '), normal > 0 ? `${normal} chỉ số bình thường chờ duyệt` : '']
          .filter(Boolean)
          .join(' · '),
        value: `${toReview}`,
        link: '/landlord/meter-readings',
        tone: problems.length ? 'warn' : 'neu',
      });
    }

    const { recorded, total, deadline } = o.capture;
    if (recorded < total) {
      items.push({
        id: 'capture',
        icon: 'i-camera',
        title: this.viewport.isPhone() ? 'Ghi chỉ số' : 'Ghi chỉ số trên điện thoại',
        detail: `${total - recorded} phòng chưa ghi: ${roomList(o.unrecorded.map((r) => r.roomCode))} · hạn ${deadline}`,
        value: `${recorded}/${total}`,
        link: '/landlord/meter-capture',
        tone: 'neu',
        progress: this.store.capturePercent(),
      });
    }

    const open = this.disputes.ordered().filter((d) => d.status === 'OPEN');
    if (open.length) {
      items.push({
        id: 'disputes',
        icon: 'i-talk',
        title: 'Trả lời khiếu nại',
        detail: `${roomList(open.map((d) => d.roomCode))} đang chờ bạn xem lại ảnh gốc`,
        value: `${open.length}`,
        link: '/landlord/disputes',
        tone: 'warn',
      });
    }

    const drafts = this.invoices.drafts().length;
    if (drafts) {
      items.push({
        id: 'send',
        icon: 'i-bill',
        title: 'Gửi hóa đơn',
        // Drafts exist only for approved readings; say so while review is still open, so sending early is a choice.
        detail: toReview
          ? `${drafts} hóa đơn nháp từ chỉ số đã duyệt · ${toReview} chỉ số còn chờ duyệt có hóa đơn sau`
          : `${drafts} hóa đơn nháp, người thuê chưa thấy`,
        money: this.invoices.draftTotal(),
        link: '/landlord/invoices',
        tone: 'neu',
      });
    }

    if (o.overdue.length) {
      items.push({
        id: 'overdue',
        icon: 'i-flag',
        title: 'Nhắc tiền quá hạn',
        detail: o.overdue
          .map((i) => `${i.roomCode} kỳ ${+i.period.slice(5)}, quá ${i.daysLate} ngày`)
          .join(' · '),
        money: this.store.overdueTotal(),
        link: '/landlord/invoices',
        tone: 'err',
      });
    }

    const c = o.summary.contracts;
    if (c.needAction) {
      items.push({
        id: 'contracts',
        icon: 'i-doc',
        title: 'Hợp đồng',
        detail: c.note,
        value: `${c.needAction}`,
        link: '/landlord/contracts',
        tone: 'neu',
      });
    }
    return items;
  });
}
