import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { MeterKind } from '../../../core/data/models';
import { TenantInvoiceStore } from '../../../core/data/tenant-invoice.store';
import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { Receipt } from '../../../shared/ui/receipt';
import { StatusChip } from '../../../shared/ui/status-chip';
import { TenantContractStore } from '../contract/tenant-contract.store';
import { DayPipe } from '../../../shared/pipes/format.pipes';
import { KIND_NAME, TenantReadingsStore, UNIT } from '../meters/tenant-readings';
import { HOME_SAMPLES, HomeState, homeStateOf } from './home-samples';

/**
 * TD-01 (computer) / TN-02 (phone): the tenant's home. Special states for review, via ?sample=:
 *   approved (default: 03/12, readings approved and the kỳ 11 invoice sent),
 *   pending (01/12 evening: readings photographed, not approved, so no numbers),
 *   moving (TN-15: P.204 → P.302 on 15/12), new (TN-16: Ngọc Hân, P.209, no invoice yet),
 *   unassigned (TN-17: account not linked to a room).
 */
@Component({
  selector: 'app-tenant-home-page',
  imports: [NgOptimizedImage, NgTemplateOutlet, RouterLink, Icon, MeterDigits, Receipt, StatusChip, VndPipe, PeriodPipe, NumPipe, DayPipe],
  templateUrl: './home.page.html',
  styleUrl: './home.page.scss',
})
export class TenantHomePage {
  /** Query parameter ?sample= (component input binding). */
  readonly sample = input<string>(); // ?sample= (not ?state=, which the OIDC client reads as a sign-in callback)

  private readonly router = inject(Router);
  private readonly invoices = inject(TenantInvoiceStore);
  private readonly readingsStore = inject(TenantReadingsStore);
  protected readonly contract = inject(TenantContractStore).decision;
  protected readonly viewport = inject(Viewport);

  protected readonly mode = computed<HomeState>(() => homeStateOf(this.sample()));
  protected readonly data = computed(() => HOME_SAMPLES[this.mode()]);
  protected readonly unit = UNIT;
  protected readonly kindName = KIND_NAME;
  protected readonly next = this.invoices.nextCapture;

  /** kỳ 11/2026 readings; before approval they carry no number and no photo. */
  protected readonly readings = computed(() => this.readingsStore.latest(this.mode() === 'pending'));
  protected readonly readingsApproved = computed(() => this.readings().every((r) => r.status === 'CONFIRMED'));
  protected readonly readingsPeriod = computed(() => this.readings()[0]?.period ?? '');
  protected readonly approvedOn = computed(() => this.readings()[0]?.confirmedAt ?? null);

  /** Before approval the kỳ 11 invoice does not exist yet. */
  protected readonly history = computed(() => {
    const all = this.invoices.history();
    return this.mode() === 'pending' ? all.filter((h) => h.status === 'PAID') : all;
  });
  protected readonly current = computed(() => {
    const first = this.history()[0];
    return first ? this.invoices.detail(first.invoiceId) : null;
  });
  protected readonly previous = computed(() => this.history().filter((h) => h.invoiceId !== this.current()?.invoiceId));

  protected readonly notifyInvoice = signal(false);
  protected readonly showOpeningPhotos = signal(false);
  protected readonly checking = signal(false);
  protected readonly checked = signal(false);

  protected unitOf(kind: MeterKind): string {
    return UNIT[kind];
  }

  protected nameOf(kind: MeterKind): string {
    return KIND_NAME[kind];
  }

  protected openPhoto(kind: MeterKind): void {
    const r = this.readings().find((x) => x.kind === kind);
    void this.router.navigate(['/tenant/meters'], { queryParams: { kind, reading: r?.readingId } });
  }

  /** TN-17 "Kiểm tra lại": sample only, the account stays unassigned. */
  protected recheck(): void {
    this.checking.set(true);
    this.checked.set(false);
    setTimeout(() => {
      this.checking.set(false);
      this.checked.set(true);
    }, 700);
  }
}
