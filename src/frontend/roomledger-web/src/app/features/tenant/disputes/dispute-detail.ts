import { NgOptimizedImage } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { NumPipe, PeriodPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { TenantDispute, dmy, shortStamp, subjectLabel, unitOf, viewOf } from './tenant-dispute.store';

/** The status chip of a dispute: icon plus text, never colour alone. */
@Component({
  selector: 'app-dispute-chip',
  imports: [Icon],
  template: `
    @switch (view()) {
      @case ('waiting') { <span class="chip warn"><app-icon name="i-clock" />Đang chờ</span> }
      @case ('corrected') { <span class="chip ok"><app-icon name="i-check" />Đã sửa</span> }
      @case ('kept') { <span class="chip neu"><app-icon name="i-info" />Giữ nguyên</span> }
    }
  `,
  host: { style: 'display:inline-flex' },
})
export class DisputeChip {
  readonly dispute = input.required<TenantDispute>();
  protected readonly view = computed(() => viewOf(this.dispute()));
}

/** TN-20 / TD-04 timeline of one dispute, with the "waiting" and "kept" states the mockups do not draw. */
@Component({
  selector: 'app-dispute-detail',
  imports: [NgOptimizedImage, RouterLink, Icon, MeterDigits, DisputeChip, VndPipe, NumPipe, PeriodPipe],
  templateUrl: './dispute-detail.html',
  styleUrl: './dispute-detail.scss',
})
export class DisputeDetail {
  readonly dispute = input.required<TenantDispute>();
  /** Hide the title row (the phone shows it in the app bar). */
  readonly compact = input(false);
  /** The tenant wants to see the original meter photo again. */
  readonly photo = output<TenantDispute>();

  protected readonly view = computed(() => viewOf(this.dispute()));
  protected readonly unit = computed(() => unitOf(this.dispute().subject));
  protected readonly title = computed(() => `${subjectLabel(this.dispute().subject)} · kỳ`);
  protected readonly dateLabel = computed(() => dmy(this.dispute().resolvedAt ?? this.dispute().createdAt));
  protected readonly shortStamp = shortStamp;

  /** Corrected usage and amount on the recalculated invoice. */
  protected readonly corrected = computed(() => {
    const d = this.dispute();
    if (d.correctedValue == null || d.previousValue == null || d.rate == null) return null;
    const usage = d.correctedValue - d.previousValue;
    return { usage, amount: usage * d.rate };
  });
  protected readonly saving = computed(() => {
    const d = this.dispute();
    return d.totalBefore != null && d.totalAfter != null ? d.totalBefore - d.totalAfter : null;
  });
  protected readonly kindOf = computed(() => (this.dispute().subject === 'ELECTRICITY' ? 'ELECTRICITY' : 'WATER'));
  protected readonly newDisputeParams = computed(() => {
    const d = this.dispute();
    return d.readingId ? { invoice: d.invoiceId, reading: d.readingId } : { invoice: d.invoiceId };
  });
}
