import { Service, computed, inject } from '@angular/core';

import { injectApi } from '../../../core/api/api-client';
import { InvoiceDetail, MeterKind, MeterReading } from '../../../core/data/models';
import { TenantInvoiceStore, nextPeriod } from '../../../core/data/tenant-invoice.store';

/**
 * One reading as the tenant sees it. Not in the API yet: there is no tenant endpoint that lists a room's
 * readings with capturedAt, digitCropUrl, usage and rate (see the report / API gaps).
 */
export type TenantReading = Pick<MeterReading, 'readingId' | 'kind' | 'period' | 'status' | 'value' | 'previousValue' | 'photoUrl' | 'confirmedAt'> & {
  usage: number | null;
  rate: number;
  amount: number | null;
  digitCropUrl: string | null;
  /** When the landlord photographed the meter. */
  capturedAt: string | null;
};

export const UNIT: Record<MeterKind, string> = { ELECTRICITY: 'kWh', WATER: 'm³' };
export const KIND_NAME: Record<MeterKind, string> = { ELECTRICITY: 'Điện', WATER: 'Nước' };

/** The tenant only sees a number and its photo once the landlord has approved it (PRODUCT.md). */
export function tenantView(r: TenantReading): TenantReading {
  return r.status === 'CONFIRMED' ? r : { ...r, value: null, usage: null, amount: null, photoUrl: null, digitCropUrl: null, confirmedAt: null };
}

/** ISO date-time -> "02/12" or "02/12/2026", read from the string so the browser time zone does not shift it. */
export function datePart(iso: string | null | undefined, withYear = false): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso ?? '');
  if (!m) return '—';
  return withYear ? `${m[3]}/${m[2]}/${m[1]}` : `${m[3]}/${m[2]}`;
}


/**
 * With the API only what the invoice line carries is known: digit crop, capture time and approval time
 * are not in it (API-GAPS.md), so they stay null. Sample data invents them for the mockups.
 */
function readingOf(inv: InvoiceDetail, kind: MeterKind, sample: boolean): TenantReading {
  const line = kind === 'ELECTRICITY' ? inv.electricity : inv.water;
  const next = nextPeriod(inv.period);
  const minute = kind === 'ELECTRICITY' ? '01' : '02';
  const isCurrent = sample && inv.period === '2026-11';
  return {
    readingId: line.readingId,
    kind,
    period: inv.period,
    status: 'CONFIRMED',
    value: line.current,
    previousValue: line.previous,
    usage: line.usage,
    rate: line.rate,
    amount: line.amount,
    photoUrl: line.photoUrl,
    // Only kỳ 11 has a matching digit crop in the sample images.
    digitCropUrl: isCurrent ? `sample/d-${kind === 'ELECTRICITY' ? 'elec' : 'water'}-204.jpg` : null,
    capturedAt: sample ? `${next}-01T08:${minute}:00+07:00` : null,
    confirmedAt: sample ? `${next}-02T09:20:00+07:00` : null,
  };
}

/** Before the landlord approves: the photo exists but the tenant sees neither it nor the number. */
function pending(r: TenantReading): TenantReading {
  return tenantView({ ...r, status: 'PENDING_CONFIRM' });
}

/** The room's readings, newest period first, derived from the invoices so numbers always match. */
@Service()
export class TenantReadingsStore {
  private readonly invoices = inject(TenantInvoiceStore);
  private readonly sample = !injectApi();

  readonly status = this.invoices.status;
  readonly error = this.invoices.error;
  readonly listReady = this.invoices.listReady;
  /** Older periods are read after the screen opens: the screen shows what it has and says so meanwhile. */
  readonly loadingMore = computed(() => this.invoices.detailsLoading() > 0);
  readonly moreError = this.invoices.detailsError;
  retryMore(): void {
    this.invoices.retryFailedDetails();
  }
  readonly nextCapture = this.invoices.nextCapture;

  private readonly all = computed(() =>
    this.invoices
      .history()
      .map((h) => this.invoices.detail(h.invoiceId))
      .filter((d): d is InvoiceDetail => d != null)
      .flatMap((d) => [readingOf(d, 'ELECTRICITY', this.sample), readingOf(d, 'WATER', this.sample)]),
  );

  /**
   * Readings of one kind. With beforeApproval the newest period (kỳ 11/2026) is shown as on 01/12 evening:
   * photographed but not yet approved.
   */
  list(kind: MeterKind, beforeApproval = false): TenantReading[] {
    const rows = this.all().filter((r) => r.kind === kind);
    return beforeApproval ? rows.map((r, i) => (i === 0 ? pending(r) : r)) : rows.map(tenantView);
  }

  load(): void {
    this.invoices.load();
  }

  /** API: reads the invoices (and so the readings) of the older periods, which are not read at start-up. */
  ensureAll(): void {
    this.invoices.ensureAllDetails();
  }

  /** Newest reading of each kind. */
  latest(beforeApproval = false): TenantReading[] {
    return [this.list('ELECTRICITY', beforeApproval)[0], this.list('WATER', beforeApproval)[0]].filter((r) => r != null);
  }
}
