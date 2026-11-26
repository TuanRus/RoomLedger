import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, linkedSignal, signal, untracked } from '@angular/core';

import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, StampPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { MeterDigits } from '../../../shared/ui/meter-digits';
import { ZoomPhoto } from '../../../shared/ui/zoom-photo';
import { DayPipe } from '../../../shared/pipes/format.pipes';
import { LoadState } from '../shared/load-state';
import { LandlordInvoicesStore } from '../invoices/data/invoices.store';
import { LandlordDispute } from './data/disputes.sample';
import { LandlordDisputesStore } from './data/disputes.store';

/** Seconds the landlord has to undo a resolution before the tenant is notified. */
const UNDO_SECONDS = 10;
/** Seconds a plain notice (API data, nothing to undo) stays on screen. */
const INFO_SECONDS = 6;

/**
 * LL-08 (computer) and its phone layout (list, then detail): answer a tenant's dispute.
 * Either correct the reading (the invoice is recalculated) or keep it with a required reply.
 */
@Component({
  selector: 'app-landlord-disputes-page',
  imports: [NgTemplateOutlet, LoadState, ZoomPhoto, Icon, MeterDigits, VndPipe, NumPipe, StampPipe, DayPipe],
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

  /** Result of the last resolution, with an undo countdown before the tenant is told. */
  protected readonly toast = signal<{ text: string; seconds: number; undo: boolean } | null>(null);
  private toastTimer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearToast());
    // Disputes already answered live in invoices that are only read when needed: read them now this screen is open.
    effect(() => {
      if (this.store.status() === 'ready') untracked(() => this.store.loadAnswered());
    });
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

  /** The tenant's name, or a plain "Người thuê" when the room list has no name for the room. */
  protected who(d: LandlordDispute): string {
    return d.tenantName ?? 'Người thuê';
  }

  protected correct(d: LandlordDispute): void {
    const v = this.value();
    if (v == null || this.valueError()) return;
    const done = (result: string) => {
      if (result === 'ok') this.resolved(`Đã sửa chỉ số ${d.roomCode} và tính lại hóa đơn.`, this.store.sample);
    };
    if (this.store.sample) done(this.store.correct(d.disputeId, v));
    else this.store.correctByApi(d.disputeId, v).subscribe(done);
  }

  protected keep(d: LandlordDispute): void {
    if (!this.reply().trim()) {
      this.replyError.set(true);
      return;
    }
    const done = (result: string) => {
      if (result === 'empty-reply') this.replyError.set(true);
      else if (result === 'ok') this.resolved(`Đã trả lời ${this.who(d)} (${d.roomCode}).`, this.store.sample);
    };
    if (this.store.sample) done(this.store.keep(d.disputeId, this.reply()));
    else this.store.keepByApi(d.disputeId, this.reply()).subscribe(done);
  }

  /** Puts the dispute and its invoice back while the toast is showing; the tenant has not been told yet. */
  protected undo(): void {
    const id = this.store.undoResolve();
    if (id) this.selectedId.set(id);
    this.closeToast();
  }

  protected closeToast(): void {
    this.clearToast();
    this.toast.set(null);
  }

  /**
   * Shows the result and moves on to the next dispute still waiting, if any. Sample data: with a "Hoàn tác" countdown
   * (the tenant is told when it runs out). API: the answer is already sent, so a plain notice without undo.
   */
  private resolved(text: string, undo: boolean): void {
    this.clearToast();
    this.toast.set({ text: undo ? text : `${text} Người thuê được báo.`, seconds: undo ? UNDO_SECONDS : INFO_SECONDS, undo });
    this.toastTimer = setInterval(() => {
      const t = this.toast();
      if (!t || t.seconds <= 1) this.closeToast();
      else this.toast.set({ ...t, seconds: t.seconds - 1 });
    }, 1000);
    const next = this.store.ordered().find((x) => x.status === 'OPEN');
    if (next) this.selectedId.set(next.disputeId);
  }

  private clearToast(): void {
    if (this.toastTimer) clearInterval(this.toastTimer);
    this.toastTimer = null;
  }
}
