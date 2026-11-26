import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, ElementRef, computed, effect, inject, linkedSignal, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MeterKind } from '../../../core/data/models';
import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe, StampPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterPhoto } from '../../../shared/ui/meter-photo';
import { Receipt } from '../../../shared/ui/receipt';
import { StatusChip } from '../../../shared/ui/status-chip';
import { DayPipe } from '../../../shared/pipes/format.pipes';
import { LoadState } from '../shared/load-state';
import { LandlordInvoice } from './data/invoices.sample';
import { InvoiceGroup, LandlordInvoicesStore, PAID_UNDO_SECONDS } from './data/invoices.store';

type Filter = 'all' | InvoiceGroup;

/** `empty` says what an empty filter means for this landlord, not just that the list is empty. */
const FILTERS: { id: Filter; label: string; empty: string }[] = [
  { id: 'all', label: 'Tất cả', empty: 'Chưa có hóa đơn kỳ này. Hóa đơn được tạo khi bạn duyệt chỉ số.' },
  { id: 'draft', label: 'Nháp', empty: 'Đã gửi hết hóa đơn nháp cho người thuê.' },
  { id: 'unpaid', label: 'Chưa thanh toán', empty: 'Không ai còn nợ hóa đơn đã gửi.' },
  { id: 'paid', label: 'Đã thanh toán', empty: 'Chưa ai trả tiền kỳ này. Chọn hóa đơn rồi bấm "Đánh dấu đã thanh toán" khi nhận được tiền.' },
  { id: 'overdue', label: 'Quá hạn (kỳ trước)', empty: 'Không có hóa đơn kỳ trước nào còn nợ.' },
];

const INFO_SECONDS = 6;

interface Toast {
  text: string;
  /** Invoice to unmark when the landlord presses "Hoàn tác". */
  undoId: string | null;
  seconds: number;
}

/**
 * LL-05 / LL-05b / LL-05c / LL-05d (computer) and the phone layout of the same route:
 * send the period's drafts, see who has paid, mark an invoice paid (with undo) and remind tenants.
 * Payment happens outside the app; only the landlord marks an invoice paid.
 */
@Component({
  selector: 'app-landlord-invoices-page',
  imports: [MeterPhoto, NgTemplateOutlet, RouterLink, Icon, LoadState, Receipt, StatusChip, VndPipe, NumPipe, PeriodPipe, StampPipe, DayPipe],
  templateUrl: './invoices.page.html',
  styleUrl: './invoices.page.scss',
})
export class LandlordInvoicesPage {
  protected readonly store = inject(LandlordInvoicesStore);
  protected readonly viewport = inject(Viewport);

  private readonly vnd = new VndPipe();
  protected readonly filters = FILTERS;
  protected readonly emptyText = computed(() => FILTERS.find((f) => f.id === this.filter())?.empty ?? '');
  protected readonly filter = signal<Filter>('all');

  protected readonly visible = computed(() => {
    const f = this.filter();
    const list = this.store.invoices();
    return f === 'all' ? list : list.filter((i) => this.store.groupOf(i) === f);
  });

  /** Phone list: one section per group, in the order of the filters. */
  protected readonly groups = computed(() =>
    FILTERS.slice(1)
      .map((f) => ({ ...f, items: this.visible().filter((i) => this.store.groupOf(i) === f.id) }))
      .filter((g) => g.items.length > 0),
  );

  /** Header action after sending: remind every unpaid invoice of this period (LL-05d). */
  protected readonly unpaidThisPeriod = computed(() => this.store.unpaid().filter((i) => i.period === this.store.period));

  protected readonly selectedId = linkedSignal<LandlordInvoice[], string | null>({
    source: this.store.invoices,
    computation: (list, previous) => {
      if (previous?.value && list.some((i) => i.invoiceId === previous.value)) return previous.value;
      if (this.viewport.isPhone()) return null;
      // The sample opens on P.204 (the disputed invoice of the mockups); with the API the first invoice of the list.
      return (this.store.sample ? list.find((i) => i.roomCode === 'P.204') : undefined)?.invoiceId ?? list[0]?.invoiceId ?? null;
    },
  });
  protected readonly selected = computed(() => this.store.invoices().find((i) => i.invoiceId === this.selectedId()) ?? null);

  protected readonly photo = signal<{ inv: LandlordInvoice; kind: MeterKind } | null>(null);
  private readonly photoDialog = viewChild<ElementRef<HTMLDialogElement>>('photoDialog');
  private readonly sendDialog = viewChild<ElementRef<HTMLDialogElement>>('sendDialog');

  protected readonly toast = signal<Toast | null>(null);
  private toastTimer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearToastTimer());
    // With the API a list row has no lines yet: read the one the landlord opens.
    effect(() => {
      const inv = this.selected();
      if (inv?.detailPending) untracked(() => this.store.ensureDetail(inv.invoiceId));
    });
  }

  protected count(f: Filter): number {
    const c = this.store.counts();
    return f === 'all' ? c.all : c[f];
  }

  protected overdue(inv: LandlordInvoice): number {
    return this.store.groupOf(inv) === 'overdue' ? this.store.overdueDays(inv) : 0;
  }

  protected openSend(): void {
    this.sendDialog()?.nativeElement.showModal();
  }

  protected closeSend(): void {
    this.sendDialog()?.nativeElement.close();
  }

  protected reviewDrafts(): void {
    this.closeSend();
    this.filter.set('draft');
  }

  protected sendAll(): void {
    this.closeSend();
    this.filter.set('all');
    if (this.store.sample) {
      const count = this.store.send();
      if (count > 0) this.showToast(`Đã gửi ${count} hóa đơn kỳ ${this.periodText()}.`);
      return;
    }
    this.store.sendByApi().subscribe((count) => {
      if (count > 0) this.showToast(`Đã gửi ${count} hóa đơn kỳ ${this.periodText()}.`);
    });
  }

  protected sendOne(inv: LandlordInvoice): void {
    const to = inv.tenantName ?? inv.roomCode;
    if (this.store.sample) {
      if (this.store.send([inv.invoiceId])) this.showToast(`Đã gửi hóa đơn ${inv.roomCode} cho ${to}.`);
      return;
    }
    this.store.sendByApi([inv.invoiceId]).subscribe((count) => {
      if (count > 0) this.showToast(`Đã gửi hóa đơn ${inv.roomCode} cho ${to}.`);
    });
  }

  /** API: creates the draft invoices of the period from the approved readings (POST invoices:generate). */
  protected generate(): void {
    this.store.generate().subscribe({
      next: (res) => this.showToast(`Đã lập ${res.created?.length ?? 0} hóa đơn nháp kỳ ${this.periodText()}.`),
      error: (err: unknown) => this.store.error.set(err instanceof Error ? err.message : 'Chưa lập được hóa đơn nháp.'),
    });
  }

  protected markPaid(inv: LandlordInvoice): void {
    if (!this.store.markPaid(inv.invoiceId)) return;
    this.showToast(`${inv.roomCode} đã thanh toán ${this.money(inv.total)}.`, inv.invoiceId);
  }

  protected unmarkPaid(inv: LandlordInvoice): void {
    this.store.unmarkPaid(inv.invoiceId);
    this.showToast(`${inv.roomCode} trở về “Chưa thanh toán”. Người thuê được báo.`);
  }

  protected remind(inv: LandlordInvoice): void {
    if (this.store.remind([inv.invoiceId])) this.showToast(`Đã nhắc ${inv.tenantName ?? inv.roomCode} (${inv.roomCode}) qua email.`);
  }

  protected remindAll(): void {
    const n = this.store.remind(this.unpaidThisPeriod().map((i) => i.invoiceId));
    if (n) this.showToast(`Đã nhắc ${n} phòng chưa thanh toán qua email.`);
  }

  protected openPhoto(inv: LandlordInvoice, kind: MeterKind): void {
    this.photo.set({ inv, kind });
    this.photoDialog()?.nativeElement.showModal();
  }

  protected closePhoto(): void {
    this.photoDialog()?.nativeElement.close();
  }

  protected undo(): void {
    const id = this.toast()?.undoId;
    if (id) this.store.unmarkPaid(id);
    this.closeToast();
  }

  protected closeToast(): void {
    this.clearToastTimer();
    this.toast.set(null);
  }

  protected kindName(kind: MeterKind): string {
    return kind === 'ELECTRICITY' ? 'Điện' : 'Nước';
  }

  protected unit(kind: MeterKind): string {
    return kind === 'ELECTRICITY' ? 'kWh' : 'm³';
  }

  private periodText(): string {
    const [y, m] = this.store.period.split('-');
    return `${m}/${y}`;
  }

  private money(value: number): string {
    return this.vnd.transform(value);
  }

  private showToast(text: string, undoId: string | null = null): void {
    this.clearToastTimer();
    this.toast.set({ text, undoId, seconds: undoId ? PAID_UNDO_SECONDS : INFO_SECONDS });
    this.toastTimer = setInterval(() => {
      const t = this.toast();
      if (!t || t.seconds <= 1) this.closeToast();
      else this.toast.set({ ...t, seconds: t.seconds - 1 });
    }, 1000);
  }

  private clearToastTimer(): void {
    if (this.toastTimer) clearInterval(this.toastTimer);
    this.toastTimer = null;
  }
}
