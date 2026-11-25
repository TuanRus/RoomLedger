import { NgOptimizedImage } from '@angular/common';
import { Component, ElementRef, computed, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { FormField, form, maxLength, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';

import { UtilityLine } from '../../../core/data/models';
import { TenantInvoiceStore } from '../../../core/data/tenant-invoice.store';
import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { DisputeSubject, TenantDisputeStore, unitOf } from '../disputes/tenant-dispute.store';
import { REASON_MAX, REASON_MIN, proposedValueError, reasonError, reasonLength } from './dispute-rules';

const DEFAULT_INVOICE = 'inv-204-2026-11';

const SUBJECTS: { id: DisputeSubject; label: string }[] = [
  { id: 'ELECTRICITY', label: 'Chỉ số điện' },
  { id: 'WATER', label: 'Chỉ số nước' },
  { id: 'OTHER', label: 'Khác' },
];

/** "r-204-WATER-2026-11" -> "WATER"; no reading -> water (the most common dispute in the sample story). */
export function subjectFromReading(readingId: string | undefined): DisputeSubject {
  if (readingId?.includes('ELECTRICITY')) return 'ELECTRICITY';
  return 'WATER';
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
  protected readonly viewport = inject(Viewport);

  protected readonly subjects = SUBJECTS;
  protected readonly reasonMin = REASON_MIN;
  protected readonly reasonMax = REASON_MAX;

  protected readonly inv = computed(() => this.invoices.detail(this.invoice() ?? DEFAULT_INVOICE));

  protected readonly model = linkedSignal(() => ({ subject: subjectFromReading(this.reading()), value: '', reason: '' }));
  protected readonly f = form(this.model, (p) => {
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
  protected readonly unit = computed(() => unitOf(this.subject()));
  protected readonly kind = computed(() => (this.subject() === 'ELECTRICITY' ? 'ELECTRICITY' : 'WATER'));
  protected readonly meterName = computed(() => (this.subject() === 'ELECTRICITY' ? 'Công tơ điện' : 'Đồng hồ nước'));
  protected readonly reasonCount = computed(() => reasonLength(this.model().reason));

  protected readonly busy = signal(false);
  private readonly photoDialog = viewChild<ElementRef<HTMLDialogElement>>('photoDialog');

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
      const first = this.f.value().invalid() && this.line() ? 'dn-value' : 'dn-reason';
      (event.target as HTMLFormElement).querySelector<HTMLElement>(`#${first}`)?.focus();
      return;
    }
    this.busy.set(true);
    // TODO(API): POST /invoices/{invoiceId}/disputes. The contract has no proposed value or "Khác" subject yet.
    await new Promise((resolve) => setTimeout(resolve, 400));
    const { subject, value, reason } = this.model();
    const line = this.line();
    const created = this.disputes.create({
      invoiceId: inv.invoiceId,
      readingId: line?.readingId ?? null,
      subject,
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

  private lineFor(subject: DisputeSubject): UtilityLine | null {
    const inv = this.inv();
    if (!inv || subject === 'OTHER') return null;
    return subject === 'ELECTRICITY' ? inv.electricity : inv.water;
  }
}
