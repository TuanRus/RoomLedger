import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PeriodPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { OverviewStore, formatChange } from './data/overview.store';

/**
 * LL-02: what needs the landlord's attention this period. On a phone the same cards stack
 * in priority order: meter readings to handle, rooms not yet recorded, overdue invoices, then the summary.
 */
@Component({
  selector: 'app-landlord-overview-page',
  imports: [RouterLink, Icon, PeriodPipe, VndPipe],
  templateUrl: './overview.page.html',
  styleUrl: './overview.page.scss',
})
export class LandlordOverviewPage {
  protected readonly store = inject(OverviewStore);
  protected readonly formatChange = formatChange;
}
