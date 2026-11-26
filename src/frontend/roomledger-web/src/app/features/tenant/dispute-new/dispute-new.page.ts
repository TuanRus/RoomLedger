import { NgOptimizedImage } from '@angular/common';
import { Component, ElementRef, computed, effect, inject, input, linkedSignal, signal, untracked, viewChild } from '@angular/core';
import { FormField, form, maxLength, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';

import { InvoiceDetail, UtilityLine } from '../../../core/data/models';
import { TenantInvoiceStore } from '../../../core/data/tenant-invoice.store';
import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { DisputeSubject, TenantDisputeStore, unitOf } from '../disputes/tenant-dispute.store';
import { REASON_MAX, REASON_MIN, proposedValueError, reasonError, reasonLength } from './dispute-rules';

/** Sample data: the invoice the page opens when the URL names none. */
const DEFAULT_INVOICE = 'inv-204-2026-11';

const SUBJECTS: { id: DisputeSubject; label: string }[] = [
  { id: 'ELECTRICITY', label: 'Chỉ số điện' },
  { id: 'WATER', label: 'Chỉ số nước' },
  { id: 'OTHER', label: 'Khác' },
];

/** Sample data: "r-204-WATER-2026-11" -> "WATER"; no reading -> water (the most common dispute in the sample story). */
export function subjectFromReading(readingId: string | undefined): DisputeSubject {
  if (readingId?.includes('ELECTRICITY')) return 'ELECTRICITY';
  return 'WATER';
}

/** API mode: the reading id of an invoice line says which meter it is; no match -> nothing preselected. */
export function subjectOfLine(inv: InvoiceDetail | null, readingId: string | undefined): DisputeSubject | null {
  if (!inv || !readingId) return null;
  if (inv.electricity.readingId === readingId) return 'ELECTRICITY';
  if (inv.water.readingId === readingId) return 'WATER';
  return null;
}

/** The reason as sent: the value the tenant reads is not a field of the API, so it goes at the end of the text. */
export function reasonWithValue(reason: string, value: string, unit: string): string {
  return `${reason.trim()} (Số tôi thấy: ${value.trim()} ${unit})`;
}

/**
 * TN-11: the tenant disputes one line of an invoice.
 * Query params (component input binding): ?invoice=<invoiceId>&reading=<readingId>.
 */
@Component({
  selector: 'app-tenant-dispute-new-page',
  imports: [NgOptimizedImage, FormField, RouterLink, Icon, MeterDigits, NumPipe, PeriodPipe],
  templateUrl: './dispute-new.page.html',
  styleUrl: './dispute-new.page.scss',
})
export class TenantDisputeNewPage {
  readonly invoice = input<string>();
  readonly reading = input<string>();

  private readonly router = inject(Router);
  private readonly invoices = inject(TenantInvoiceStore);
  private readonly disputes = inject(TenantDisputeStore);
  protected readonly status = this.invoices.status;
  protected readonly loadError = this.invoices.error;
  protected readonly viewport = inject(Viewport);

  /** "Khác" has no reading, and the API needs one, so only sample data offers it. */
  protected readonly subjects = this.invoices.sample ? SUBJECTS : SUBJECTS.filter((s) => s.id !== 'OTHER');
  protected readonly reasonMin = REASON_MIN;
  protected readonly reasonMax = REASON_MAX;

  /** The invoice in the URL; without one: sample data's P.204 invoice, or the newest invoice still open for a dispute. */
  private readonly invoiceId = computed(() => {
    const wanted = this.invoice();
    if (wanted) return wanted;
    if (this.invoices.sample) return DEFAULT_INVOICE;
    return this.invoices.history().find((i) => i.status === 'SENT' || i.status === 'DISPUTED')?.invoiceId ?? null;
  });
  protected readonly inv = computed(() => {
    const id = this.invoiceId();
    return id ? this.invoices.detail(id) : null;
  });
  /** Only the newest invoice is read at start-up: the one disputed is read when this page opens. */
  protected readonly detailStatus = computed(() => {
    const id = this.invoiceId();
    return id ? this.invoices.detailStatus(id) : 'missing';
  });
  protected readonly detailError = computed(() => this.invoices.detailError(this.invoiceId() ?? ''));
  protected readonly noInvoice = computed(() => !this.invoice() && !this.invoices.sample && !this.invoiceId());
  protected readonly error = signal<string | null>(null);

  protected readonly model = linkedSignal<{ subject: DisputeSubject | null; value: string; reason: string }>(() => ({
    subject: this.invoices.sample ? subjectFromReading(this.reading()) : subjectOfLine(this.inv(), this.reading()),
    value: '',
    reason: '',
  }));
  protected readonly f = form(this.model, (p) => {
    validate(p.subject, ({ value }) => (value() ? undefined : { kind: 'subject', message: 'Chọn điện hoặc nước' }));
    validate(p.value, ({ value, valueOf }) => {
      const line = this.lineFor(valueOf(p.subject));
      if (!line) return undefined;
      const err = proposedValueError(value(), line.previous, line.current);
      return err ? { kind: 'value', message: err } : undefined;
    });
    maxLength(p.reason, REASON_MAX, { message: `Tối đa ${REASON_MAX} ký tự` });
    validate(p.reason, ({ value }) => {
      const err = reasonError(value());
      return err ? { kind: 'reason', message: err } : undefined;
    });
  });

  protected readonly subject = computed(() => this.model().subject);
  protected readonly line = computed(() => this.lineFor(this.subject()));
  protected readonly unit = computed(() => unitOf(this.subject() ?? 'OTHER'));
  protected readonly kind = computed(() => (this.subject() === 'ELECTRICITY' ? 'ELECTRICITY' : 'WATER'));
  protected readonly meterName = computed(() => (this.subject() === 'ELECTRICITY' ? 'Công tơ điện' : 'Đồng hồ nước'));
  protected readonly reasonCount = computed(() => reasonLength(this.model().reason));

  protected readonly busy = signal(false);
  private readonly photoDialog = viewChild<ElementRef<HTMLDialogElement>>('photoDialog');

  constructor() {
    effect(() => {
      const id = this.invoiceId();
      if (id && this.invoices.listReady()) untracked(() => this.invoices.ensureDetail(id));
    });
  }

  protected retry(): void {
    this.invoices.load();
  }

  protected retryDetail(): void {
    const id = this.invoiceId();
    if (id) this.invoices.ensureDetail(id, true);
  }

  protected setSubject(subject: DisputeSubject): void {
    this.model.update((m) => ({ ...m, subject, value: '' }));
    this.f.value().reset();
  }

  protected openPhoto(): void {
    this.photoDialog()?.nativeElement.showModal();
  }

  protected closePhoto(): void {
    this.photoDialog()?.nativeElement.close();
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    const inv = this.inv();
    if (!inv) return;
    this.f().markAsTouched();
    if (this.f().invalid()) {
      const first = this.f.subject().invalid() ? 'dn-subject' : this.f.value().invalid() && this.line() ? 'dn-value' : 'dn-reason';
      (event.target as HTMLFormElement).querySelector<HTMLElement>(`#${first}`)?.focus();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const { subject, value, reason } = this.model();
    const line = this.line();
    if (!this.invoices.sample) {
      if (!line?.readingId) {
        this.busy.set(false);
        return;
      }
      this.disputes.send(inv.invoiceId, line.readingId, reasonWithValue(reason, value, this.unit())).subscribe({
        next: (disputeId) => {
          this.busy.set(false);
          void this.router.navigate(['/tenant/disputes'], { queryParams: { id: disputeId, sent: '1' } });
        },
        error: (err: unknown) => {
          this.busy.set(false);
          this.error.set(err instanceof Error ? err.message : 'Chưa gửi được khiếu nại, thử lại sau.');
        },
      });
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 400));
    const created = this.disputes.create({
      invoiceId: inv.invoiceId,
      readingId: line?.readingId ?? null,
      subject: subject ?? 'OTHER',
      period: inv.period,
      proposedValue: line ? Number(value) : null,
      recordedValue: line?.current ?? null,
      previousValue: line?.previous ?? null,
      photoUrl: line?.photoUrl ?? null,
      rate: line?.rate ?? null,
      totalBefore: inv.total,
      reason,
    });
    this.busy.set(false);
    await this.router.navigate(['/tenant/disputes'], { queryParams: { id: created.disputeId, sent: '1' } });
  }

  private lineFor(subject: DisputeSubject | null): UtilityLine | null {
    const inv = this.inv();
    if (!inv || !subject || subject === 'OTHER') return null;
    return subject === 'ELECTRICITY' ? inv.electricity : inv.water;
  }
}
