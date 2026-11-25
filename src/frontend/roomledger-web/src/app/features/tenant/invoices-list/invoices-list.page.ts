import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { InvoiceSummary } from '../../../core/data/models';
import { TenantInvoiceStore } from '../../../core/data/tenant-invoice.store';
import { Viewport } from '../../../core/layout/viewport';
import { PeriodPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { StatusChip } from '../../../shared/ui/status-chip';
import { DayPipe } from '../../../shared/pipes/format.pipes';

export type InvoiceFilter = 'all' | 'unpaid' | 'paid';

export function matchesFilter(inv: InvoiceSummary, filter: InvoiceFilter): boolean {
  if (filter === 'all') return true;
  return filter === 'paid' ? inv.status === 'PAID' : inv.status !== 'PAID';
}

/** TN-08: the tenant's invoices, newest first, filterable by paid / unpaid. Same list on a computer, as a table. */
@Component({
  selector: 'app-tenant-invoices-list-page',
  imports: [RouterLink, Icon, StatusChip, VndPipe, PeriodPipe, DayPipe],
  templateUrl: './invoices-list.page.html',
  styleUrl: './invoices-list.page.scss',
})
export class TenantInvoicesListPage {
  private readonly store = inject(TenantInvoiceStore);
  protected readonly viewport = inject(Viewport);

  protected readonly filter = signal<InvoiceFilter>('all');
  protected readonly filters = computed(() => [
    { id: 'all' as const, label: 'Tất cả', count: null },
    { id: 'unpaid' as const, label: 'Chưa thanh toán', count: this.store.unpaidCount() },
    { id: 'paid' as const, label: 'Đã thanh toán', count: null },
  ]);
  protected readonly rows = computed(() =>
    this.store
      .history()
      .filter((h) => matchesFilter(h, this.filter()))
      .map((h) => ({ ...h, dueDate: this.store.detail(h.invoiceId)?.dueDate ?? null })),
  );
}
