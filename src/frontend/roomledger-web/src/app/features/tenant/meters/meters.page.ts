import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, computed, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MeterKind } from '../../../core/data/models';
import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe, StampPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { StatusChip } from '../../../shared/ui/status-chip';
import { DayPipe } from '../../../shared/pipes/format.pipes';
import { KIND_NAME, TenantReadingsStore, UNIT } from './tenant-readings';

/**
 * TD-03 (computer): table of periods with a side viewer. TN-04 + TN-03 (phone): list per meter, tapping a
 * period opens the photo viewer full screen. View only: the landlord photographs and records every reading.
 * Query params: ?kind=ELECTRICITY|WATER, ?reading=<readingId> (opens it), ?sample=pending (kỳ 11 not approved yet).
 */
@Component({
  selector: 'app-tenant-meters-page',
  imports: [NgOptimizedImage, NgTemplateOutlet, RouterLink, Icon, MeterDigits, StatusChip, NumPipe, PeriodPipe, StampPipe, VndPipe, DayPipe],
  templateUrl: './meters.page.html',
  styleUrl: './meters.page.scss',
})
export class TenantMetersPage {
  readonly kind = input<string>();
  readonly reading = input<string>();
  readonly sample = input<string>(); // ?sample= (not ?state=, which the OIDC client reads as a sign-in callback)

  private readonly store = inject(TenantReadingsStore);
  protected readonly viewport = inject(Viewport);
  protected readonly next = this.store.nextCapture;

  protected readonly current = linkedSignal<MeterKind>(() => (this.kind() === 'WATER' ? 'WATER' : 'ELECTRICITY'));
  protected readonly unit = computed(() => UNIT[this.current()]);
  protected readonly kindName = computed(() => KIND_NAME[this.current()]);
  protected readonly rows = computed(() => this.store.list(this.current(), this.sample() === 'pending'));

  /** The open reading: the one in the URL, else the newest approved one of this meter. */
  protected readonly selectedId = linkedSignal(() => {
    const rows = this.rows();
    const wanted = this.reading();
    return (
      rows.find((r) => r.readingId === wanted && r.status === 'CONFIRMED')?.readingId ??
      rows.find((r) => r.status === 'CONFIRMED')?.readingId ??
      null
    );
  });
  protected readonly selected = computed(() => this.rows().find((r) => r.readingId === this.selectedId()) ?? null);
  /** Phone: the TN-03 viewer replaces the list. Opens straight away when a reading is in the URL. */
  protected readonly viewerOpen = linkedSignal(() => !!this.reading() && this.selected()?.readingId === this.reading());

  private readonly zoomDialog = viewChild<ElementRef<HTMLDialogElement>>('zoomDialog');
  protected readonly zoomed = signal(false);

  protected setKind(kind: MeterKind): void {
    this.current.set(kind);
  }

  protected open(readingId: string): void {
    this.selectedId.set(readingId);
    this.viewerOpen.set(true);
  }

  protected zoom(): void {
    this.zoomed.set(true);
    this.zoomDialog()?.nativeElement.showModal();
  }

  protected closeZoom(): void {
    this.zoomDialog()?.nativeElement.close();
  }
}
