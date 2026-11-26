import { Service, computed, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import type { components } from '../../generated/api-types';
import { injectApi } from '../api/api-client';

export type TenantHomeResponse = components['schemas']['TenantHomeResponse'];

/**
 * The signed-in tenant's room (GET /tenant/home): room, property, tenancy status, latest invoice.
 * With sample data nothing is loaded; room and property fall back to the sample P.204 so the screens keep their text.
 */
@Service()
export class TenantHomeStore {
  private readonly api = injectApi();

  /** True with sample data: demo controls (?sample=) may show. */
  readonly sample = !this.api;
  readonly home = signal<TenantHomeResponse | null>(null);
  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>(this.api ? 'idle' : 'ready');
  readonly error = signal<string | null>(null);

  /** Null in API mode while loading or when the account has no room yet. */
  readonly tenancyId = computed(() => (this.api ? (this.home()?.tenancyId ?? null) : 'sample'));
  readonly roomCode = computed(() => (this.api ? (this.home()?.roomCode ?? '') : 'P.204'));
  readonly propertyName = computed(() => (this.api ? (this.home()?.propertyName ?? '') : 'Nhà trọ Cô Lan'));
  /** The tenancy is ending (the landlord is handling the move-out). */
  readonly movingOut = computed(() => this.home()?.status === 'MOVING_OUT');

  private loading: Subscription | null = null;

  constructor() {
    this.load();
  }

  /** (Re)loads the room from the API; does nothing with sample data. */
  load(): void {
    const api = this.api;
    if (!api) return;
    // A load still running is cancelled, so an older answer cannot overwrite a newer one.
    this.loading?.unsubscribe();
    this.status.set('loading');
    this.error.set(null);
    this.loading = api.get<TenantHomeResponse>('/tenant/home').subscribe({
      next: (home) => {
        this.home.set(home);
        this.status.set('ready');
      },
      error: (err: unknown) => {
        this.error.set(err instanceof Error ? err.message : 'Không tải được thông tin phòng.');
        this.status.set('error');
      },
    });
  }

  /** Alias of load(), for "Thử lại" and "Kiểm tra lại". */
  reload(): void {
    this.load();
  }
}
