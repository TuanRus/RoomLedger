import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { DisputeChip, DisputeDetail } from './dispute-detail';
import { TenantDispute, TenantDisputeStore, dmy, subjectLabel, unitOf } from './tenant-dispute.store';

/**
 * TD-04 (computer) / TN-20 (phone): the tenant's disputes and how each was answered.
 * Query params: ?id=<disputeId> selects one (on a phone it opens the detail), ?sent=1 confirms a dispute just sent.
 */
@Component({
  selector: 'app-tenant-disputes-page',
  imports: [NgOptimizedImage, NgTemplateOutlet, RouterLink, Icon, DisputeChip, DisputeDetail, PeriodPipe, NumPipe],
  templateUrl: './disputes.page.html',
  styleUrl: './disputes.page.scss',
})
export class TenantDisputesPage {
  /** Query param ?id (component input binding). */
  readonly id = input<string>();
  /** Query param ?sent=1 after the tenant submits a dispute. */
  readonly sent = input<string>();

  private readonly store = inject(TenantDisputeStore);
  protected readonly viewport = inject(Viewport);

  protected readonly disputes = this.store.disputes;
  protected readonly selected = computed(() => {
    const byId = this.store.byId(this.id());
    if (byId || this.viewport.isPhone()) return byId;
    return this.disputes()[0] ?? null;
  });
  protected readonly summary = computed(() => {
    const waiting = this.store.waitingCount();
    const answered = this.disputes().length - waiting;
    return [waiting ? `${waiting} đang chờ chủ nhà` : '', answered ? `${answered} đã trả lời` : ''].filter(Boolean).join(' · ');
  });
  protected readonly justSent = computed(() => this.sent() === '1' && this.selected()?.status === 'OPEN');

  protected readonly subjectLabel = subjectLabel;
  protected readonly dmy = dmy;

  protected readonly photoOf = signal<TenantDispute | null>(null);
  private readonly photoDialog = viewChild<ElementRef<HTMLDialogElement>>('photoDialog');
  protected readonly photoUnit = computed(() => unitOf(this.photoOf()?.subject ?? 'OTHER'));

  protected dateRange(d: TenantDispute): string {
    const from = dmy(d.createdAt).slice(0, 5);
    return d.resolvedAt ? `${from} → ${dmy(d.resolvedAt).slice(0, 5)}` : `Gửi ${from}`;
  }

  protected openPhoto(d: TenantDispute): void {
    this.photoOf.set(d);
    this.photoDialog()?.nativeElement.showModal();
  }

  protected closePhoto(): void {
    this.photoDialog()?.nativeElement.close();
  }
}
