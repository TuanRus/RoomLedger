import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, input, linkedSignal, signal } from '@angular/core';

import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, StampPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { ZoomPhoto } from '../../../shared/ui/zoom-photo';
import { DayPipe } from '../../../shared/pipes/format.pipes';
import { LandlordInvoicesStore } from '../invoices/data/invoices.store';
import { LandlordDispute } from './data/disputes.sample';
import { LandlordDisputesStore } from './data/disputes.store';

/**
 * LL-08 (computer) and its phone layout (list, then detail): answer a tenant's dispute.
 * Either correct the reading (the invoice is recalculated) or keep it with a required reply.
 */
@Component({
  selector: 'app-landlord-disputes-page',
  imports: [NgTemplateOutlet, ZoomPhoto, Icon, MeterDigits, VndPipe, NumPipe, StampPipe, DayPipe],
  templateUrl: './disputes.page.html',
  styleUrl: './disputes.page.scss',
})
export class LandlordDisputesPage {
  /** Query parameter ?invoice= from the invoices page: opens that invoice's dispute. */
  readonly invoice = input<string>();

  protected readonly store = inject(LandlordDisputesStore);
  private readonly invoices = inject(LandlordInvoicesStore);
  protected readonly viewport = inject(Viewport);

  protected readonly selectedId = linkedSignal<{ list: LandlordDispute[]; invoice: string | undefined }, string | null>({
    source: () => ({ list: this.store.ordered(), invoice: this.invoice() }),
    computation: ({ list, invoice }, previous) => {
      if (previous?.value && list.some((d) => d.disputeId === previous.value)) return previous.value;
      const fromInvoice = invoice ? list.find((d) => d.invoiceId === invoice) : undefined;
      if (fromInvoice) return fromInvoice.disputeId;
      return this.viewport.isPhone() ? null : (list[0]?.disputeId ?? null);
    },
  });
  protected readonly selected = computed(() => this.store.disputes().find((d) => d.disputeId === this.selectedId()) ?? null);

  /** The disputed invoice, when it is in this landlord's invoice list (kỳ 11). */
  protected readonly invoiceOf = computed(() => {
    const d = this.selected();
    return d ? (this.invoices.find(d.invoiceId) ?? null) : null;
  });

  /** Starts empty: prefilling the tenant's number would nudge the landlord to accept it. */
  protected readonly value = linkedSignal<string | null, number | null>({ source: this.selectedId, computation: () => null });
  protected readonly reply = linkedSignal({ source: this.selectedId, computation: () => '' });
  protected readonly replyError = linkedSignal({ source: this.selectedId, computation: () => false });
  protected readonly valueError = computed(() => {
    const r = this.selected()?.reading;
    const v = this.value();
    return !!r && v != null && v < r.previous;
  });
  protected readonly recalc = computed(() => {
    const d = this.selected();
    const v = this.value();
    return d && v != null && !this.valueError() ? this.store.recalc(d, v) : null;
  });

  protected readonly toast = signal<string | null>(null);
  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearToast());
  }

  protected unit(d: LandlordDispute): string {
    return d.reading?.kind === 'ELECTRICITY' ? 'kWh' : 'm³';
  }

  protected title(d: LandlordDispute): string {
    const period = `${d.period.slice(5)}/${d.period.slice(0, 4)}`;
    if (!d.reading) return `${d.roomCode} · hóa đơn kỳ ${period}`;
    return `${d.roomCode} · chỉ số ${d.reading.kind === 'ELECTRICITY' ? 'điện' : 'nước'} kỳ ${period}`;
  }

  protected short(d: LandlordDispute): string {
    if (!d.reading) return `${d.roomCode} · hóa đơn kỳ ${+d.period.slice(5)}`;
    return `${d.roomCode} · chỉ số ${d.reading.kind === 'ELECTRICITY' ? 'điện' : 'nước'}`;
  }

  protected onValueInput(event: Event): void {
    const digits = (event.target as HTMLInputElement).value.replace(/\D/g, '');
    this.value.set(digits === '' ? null : Number(digits));
  }

  protected onReplyInput(event: Event): void {
    this.reply.set((event.target as HTMLTextAreaElement).value);
    if (this.replyError() && this.reply().trim()) this.replyError.set(false);
  }

  protected correct(d: LandlordDispute): void {
    const v = this.value();
    if (v == null || this.valueError()) return;
    if (this.store.correct(d.disputeId, v) === 'ok') this.showToast(`Đã sửa chỉ số ${d.roomCode} và tính lại hóa đơn. Người thuê được báo.`);
  }

  protected keep(d: LandlordDispute): void {
    const result = this.store.keep(d.disputeId, this.reply());
    if (result === 'empty-reply') {
      this.replyError.set(true);
      return;
    }
    if (result === 'ok') this.showToast(`Đã trả lời ${d.tenantName} (${d.roomCode}).`);
  }

  private showToast(text: string): void {
    this.clearToast();
    this.toast.set(text);
    this.toastTimer = setTimeout(() => this.toast.set(null), 6000);
  }

  private clearToast(): void {
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = null;
  }
}
