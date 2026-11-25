import { DOCUMENT, NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MeterKind } from '../../../core/data/models';
import { TenantInvoiceStore } from '../../../core/data/tenant-invoice.store';
import { Viewport } from '../../../core/layout/viewport';
import { PeriodPipe, StampPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterPhoto } from '../../../shared/ui/meter-photo';
import { Receipt } from '../../../shared/ui/receipt';
import { StatusChip } from '../../../shared/ui/status-chip';

type CopyTarget = 'account' | 'note';

/**
 * TD-02 (computer) and TN-09 (phone): one invoice and how to pay it.
 * Payment is outside the app. On a computer the tenant scans the landlord's QR code with a banking app;
 * on a phone there is no QR code (they cannot scan their own screen), only a button that copies the account number.
 * TN-22: once the landlord marks the invoice paid, the payment panel gives way to a "Đã thanh toán" confirmation.
 */
@Component({
  selector: 'app-tenant-invoice-page',
  imports: [MeterPhoto, NgOptimizedImage, NgTemplateOutlet, RouterLink, Icon, Receipt, StatusChip, VndPipe, PeriodPipe, StampPipe],
  templateUrl: './invoice.page.html',
  styleUrl: './invoice.page.scss',
})
export class TenantInvoicePage {
  /** Route parameter :invoiceId (component input binding). */
  readonly invoiceId = input.required<string>();

  private readonly store = inject(TenantInvoiceStore);
  private readonly window = inject(DOCUMENT).defaultView;
  protected readonly viewport = inject(Viewport);

  protected readonly invoice = computed(() => this.store.detail(this.invoiceId()));
  protected readonly history = this.store.history;
  protected readonly payment = this.store.payment;
  protected readonly isPaid = computed(() => this.invoice()?.status === 'PAID');
  /** TN-22: what comes after a paid invoice (the next invoice, or when the meters are read). */
  protected readonly next = computed(() => this.store.nextAfter(this.invoiceId()));
  /** "Báo tôi" on the next-period hint; sample only, the email preference is not in the API yet. */
  protected readonly notify = signal(false);

  /** "2026-12-10" -> "10/12/2026". */
  protected readonly dueDate = computed(() => this.invoice()?.dueDate.split('-').reverse().join('/') ?? '');

  protected readonly copied = signal<CopyTarget | null>(null);
  protected readonly photoKind = signal<MeterKind | null>(null);
  private readonly photoDialog = viewChild<ElementRef<HTMLDialogElement>>('photoDialog');

  protected readonly photoLine = computed(() => {
    const inv = this.invoice();
    const kind = this.photoKind();
    if (!inv || !kind) return null;
    return { kind, line: kind === 'ELECTRICITY' ? inv.electricity : inv.water, name: kind === 'ELECTRICITY' ? 'Công tơ điện' : 'Đồng hồ nước', unit: kind === 'ELECTRICITY' ? 'kWh' : 'm³' };
  });

  protected async copy(target: CopyTarget): Promise<void> {
    const p = this.payment();
    const text = target === 'account' ? p.accountNumber.replace(/\s/g, '') : p.transferNote;
    try {
      await this.window?.navigator.clipboard.writeText(text);
      this.copied.set(target);
      this.window?.setTimeout(() => this.copied.set(null), 2500);
    } catch {
      this.copied.set(null);
    }
  }

  protected openPhoto(kind: MeterKind): void {
    this.photoKind.set(kind);
    this.photoDialog()?.nativeElement.showModal();
  }

  protected closePhoto(): void {
    this.photoDialog()?.nativeElement.close();
    this.photoKind.set(null);
  }
}
