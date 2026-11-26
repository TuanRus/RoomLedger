import { DOCUMENT, NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MeterKind } from '../../../core/data/models';
import { TenantInvoiceStore } from '../../../core/data/tenant-invoice.store';
import { Viewport } from '../../../core/layout/viewport';
import { PeriodPipe, StampPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterPhoto } from '../../../shared/ui/meter-photo';
import { Receipt } from '../../../shared/ui/receipt';
import { StatusChip } from '../../../shared/ui/status-chip';

type CopyTarget = 'account' | 'note' | 'amount';

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
  /** Only the newest invoice is read at start-up: any other is read when it is opened. */
  protected readonly detailStatus = computed(() => this.store.detailStatus(this.invoiceId()));
  protected readonly detailError = computed(() => this.store.detailError(this.invoiceId()));
  protected readonly history = this.store.history;
  protected readonly payment = computed(() => this.store.paymentOf(this.invoiceId()));
  protected readonly isPaid = computed(() => this.invoice()?.status === 'PAID');
  /** TN-22: what comes after a paid invoice (the next invoice, or when the meters are read). */
  protected readonly next = computed(() => this.store.nextAfter(this.invoiceId()));
  /** "Báo tôi" on the next-period hint; sample data only, the email preference is not in the API. */
  protected readonly notify = signal(false);
  protected readonly sample = this.store.sample;
  protected readonly status = this.store.status;
  protected readonly error = this.store.error;

  /** "2026-12-10" -> "10/12/2026". */
  protected readonly dueDate = computed(() => this.invoice()?.dueDate?.split('-').reverse().join('/') ?? 'chưa có');
  /** Shown instead of the bank details when the landlord has not entered them. */
  protected readonly noPaymentText = 'Chủ nhà chưa thêm thông tin chuyển khoản. Hỏi chủ nhà số tài khoản để thanh toán.';

  protected readonly copied = signal<CopyTarget | null>(null);
  /** Set when the browser refuses the clipboard, so the tenant knows to select the text by hand. */
  protected readonly copyFailed = signal(false);
  protected readonly photoKind = signal<MeterKind | null>(null);
  private readonly photoDialog = viewChild<ElementRef<HTMLDialogElement>>('photoDialog');

  protected readonly photoLine = computed(() => {
    const inv = this.invoice();
    const kind = this.photoKind();
    if (!inv || !kind) return null;
    return { kind, line: kind === 'ELECTRICITY' ? inv.electricity : inv.water, name: kind === 'ELECTRICITY' ? 'Công tơ điện' : 'Đồng hồ nước', unit: kind === 'ELECTRICITY' ? 'kWh' : 'm³' };
  });

  constructor() {
    effect(() => {
      const id = this.invoiceId();
      if (this.store.listReady()) untracked(() => this.store.ensureDetail(id));
    });
  }

  protected retry(): void {
    this.store.load();
  }

  /** Invoices whose QR link was already refreshed once after the image failed (the link expires; the cached detail may be old). */
  private readonly qrRefreshed = new Set<string>();

  /** The QR image did not load (expired presigned link): read the invoice again once for a fresh link. */
  protected onQrError(): void {
    const id = this.invoiceId();
    if (this.qrRefreshed.has(id)) return;
    this.qrRefreshed.add(id);
    this.store.reloadInvoice(id).subscribe({ error: () => undefined });
  }

  protected retryDetail(): void {
    this.store.ensureDetail(this.invoiceId(), true);
  }

  protected async copy(target: CopyTarget): Promise<void> {
    const p = this.payment();
    if (!p && target !== 'amount') return;
    const text =
      target === 'account' ? p!.accountNumber.replace(/\s/g, '') : target === 'note' ? p!.transferNote : String(this.invoice()?.total ?? '');
    try {
      await this.window?.navigator.clipboard.writeText(text);
      this.copyFailed.set(false);
      this.copied.set(target);
      this.window?.setTimeout(() => this.copied.set(null), 2500);
    } catch {
      this.copied.set(null);
      this.copyFailed.set(true);
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
