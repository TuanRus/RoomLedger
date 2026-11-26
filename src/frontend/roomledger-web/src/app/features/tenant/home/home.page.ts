import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { DevSession } from '../../../core/dev/dev-session';
import { MeStore } from '../../../core/data/me.store';
import { environment } from '../../../../environments/environment';
import { MeterKind } from '../../../core/data/models';
import { TenantHomeStore } from '../../../core/data/tenant-home.store';
import { TenantInvoiceStore } from '../../../core/data/tenant-invoice.store';
import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { Receipt } from '../../../shared/ui/receipt';
import { StatusChip } from '../../../shared/ui/status-chip';
import { TenantContractStore } from '../contract/tenant-contract.store';
import { TenantDisputeStore } from '../disputes/tenant-dispute.store';
import { DayPipe } from '../../../shared/pipes/format.pipes';
import { KIND_NAME, TenantReadingsStore, UNIT } from '../meters/tenant-readings';
import { HOME_SAMPLES, HomeSample, HomeState, homeStateOf } from './home-samples';

/** "Trần Minh Anh" -> "Minh Anh" (the name without the family name); one word stays as it is. */
export function givenName(fullName: string): string {
  const words = fullName.trim().split(/\s+/).filter(Boolean);
  return words.length > 1 ? words.slice(1).join(' ') : fullName.trim();
}

/**
 * TD-01 (computer) / TN-02 (phone): the tenant's home. With the API the state comes from the data:
 * unassigned (no room), moving (MOVING_OUT), new (no invoice yet), approved (has an invoice).
 * With sample data ?sample= picks a variant for review:
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
  /** Query parameter ?sample= (component input binding); ignored with the API. */
  readonly sample = input<string>(); // ?sample= (not ?state=, which the OIDC client reads as a sign-in callback)

  private readonly router = inject(Router);
  private readonly invoices = inject(TenantInvoiceStore);
  private readonly readingsStore = inject(TenantReadingsStore);
  private readonly homeStore = inject(TenantHomeStore);
  private readonly me = inject(MeStore);
  private readonly disputes = inject(TenantDisputeStore);
  private readonly contractStore = inject(TenantContractStore);
  protected readonly contract = this.contractStore.decision;
  protected readonly contractDetail = this.contractStore.contract;
  protected readonly viewport = inject(Viewport);
  protected readonly isSample = this.homeStore.sample;
  protected readonly localDev = environment.localDev;
  private readonly devSession = inject(DevSession);

  /** Loading / error of what the page needs (room, person, invoices). Sample data is always ready. */
  protected readonly status = computed<'loading' | 'error' | 'ready'>(() => {
    if (this.isSample) return 'ready';
    const all = [this.homeStore.status(), this.me.status(), this.invoices.status()];
    if (all.includes('error')) return 'error';
    return all.every((s) => s === 'ready') ? 'ready' : 'loading';
  });
  protected readonly error = computed(() => this.homeStore.error() ?? this.me.error() ?? this.invoices.error());

  protected readonly mode = computed<HomeState>(() => {
    if (this.isSample) return homeStateOf(this.sample());
    if (!this.homeStore.tenancyId()) return 'unassigned';
    if (this.homeStore.movingOut()) return 'moving';
    return this.invoices.history().length ? 'approved' : 'new';
  });
  protected readonly data = computed<HomeSample>(() => {
    if (this.isSample) return HOME_SAMPLES[this.mode()];
    const full = this.me.me()?.fullName ?? '';
    return { firstName: givenName(full), roomCode: this.homeStore.roomCode(), email: this.me.me()?.email ?? '' };
  });
  protected readonly unit = UNIT;
  protected readonly kindName = KIND_NAME;
  protected readonly next = this.invoices.nextCapture;

  /** kỳ 11/2026 readings; before approval they carry no number and no photo. */
  protected readonly readings = computed(() => this.readingsStore.latest(this.isSample && this.mode() === 'pending'));
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

  /** "Khiếu nại" line of the links card. */
  protected readonly disputeNote = computed(() => {
    const n = this.disputes.waitingCount();
    return n ? `${n} khiếu nại đang chờ chủ nhà` : 'Không có khiếu nại đang mở';
  });

  protected readonly notifyInvoice = signal(false);
  protected readonly showOpeningPhotos = signal(false);
  protected readonly checking = computed(() => this.rechecking() || (!this.isSample && this.homeStore.status() === 'loading'));
  protected readonly checked = signal(false);
  private readonly rechecking = signal(false);
  private readonly awaitingRecheck = signal(false);

  constructor() {
    // API: the recheck is done when the reload settles; only then can the page say the account still has no room.
    effect(() => {
      if (!this.awaitingRecheck() || this.homeStore.status() === 'loading' || this.homeStore.status() === 'idle') return;
      untracked(() => {
        this.awaitingRecheck.set(false);
        this.checked.set(this.homeStore.status() === 'ready');
      });
    });
  }

  /** LocalDev: forget the sample person and load the app afresh at the role picker (the stores would keep their data otherwise). */
  protected logout(): void {
    this.devSession.signOut();
  }

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

  /** Reloads every store the page reads; "Thử lại" on an error. */
  protected retry(): void {
    this.homeStore.load();
    this.me.load();
    this.invoices.load();
  }

  /** TN-17 "Kiểm tra lại": asks the server again (API), or a short fake wait with sample data (the account stays unassigned). */
  protected recheck(): void {
    this.checked.set(false);
    if (!this.isSample) {
      // "Vẫn chưa có phòng" is only said once the answer is in (the stores' status goes loading -> ready/error).
      this.retry();
      this.awaitingRecheck.set(true);
      return;
    }
    this.rechecking.set(true);
    setTimeout(() => {
      this.rechecking.set(false);
      this.checked.set(true);
    }, 700);
  }
}
