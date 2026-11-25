import { Service, computed, signal } from '@angular/core';

import { OVERVIEW_SAMPLE, OverviewData } from './overview.sample';

/** Whole percent of rooms recorded, clamped to 0–100. */
export function capturePercent(recorded: number, total: number): number {
  return total <= 0 ? 0 : Math.min(100, Math.max(0, Math.round((recorded / total) * 100)));
}

/** 0.64 -> "+64%", -0.2 -> "−20%". */
export function formatChange(change: number | null): string {
  if (change == null) return '—';
  const p = Math.round(change * 100);
  return p > 0 ? `+${p}%` : p < 0 ? `−${Math.abs(p)}%` : '0%';
}

/** Landlord dashboard (LL-02), sample-backed until the API is connected. */
@Service()
export class OverviewStore {
  private readonly data = signal<OverviewData>(OVERVIEW_SAMPLE);
  readonly overview = this.data.asReadonly();

  readonly capturePercent = computed(() => {
    const c = this.data().capture;
    return capturePercent(c.recorded, c.total);
  });

  readonly overdueTotal = computed(() => this.data().overdue.reduce((sum, i) => sum + i.amount, 0));
}
