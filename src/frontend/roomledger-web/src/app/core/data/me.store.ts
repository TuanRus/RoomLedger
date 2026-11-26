import { Service, computed, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import type { components } from '../../generated/api-types';
import { injectApi } from '../api/api-client';

type MeResponse = components['schemas']['MeResponse'];

/** First letter of the last two words of the name: "Trần Minh Anh" -> "MA". */
export function initialsOf(fullName: string): string {
  return fullName
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .slice(-2)
    .map((w) => w.charAt(0).toUpperCase())
    .join('');
}

/** The signed-in person (GET /me). With sample data `me` stays null: the shells and pages keep their sample text. */
@Service()
export class MeStore {
  private readonly api = injectApi();

  readonly me = signal<MeResponse | null>(null);
  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>(this.api ? 'idle' : 'ready');
  readonly error = signal<string | null>(null);
  readonly initials = computed(() => initialsOf(this.me()?.fullName ?? ''));

  private loading: Subscription | null = null;

  constructor() {
    this.load();
  }

  /** (Re)loads the person from the API; does nothing with sample data. */
  load(): void {
    const api = this.api;
    if (!api) return;
    // A load still running is cancelled, so an older answer cannot overwrite a newer one.
    this.loading?.unsubscribe();
    this.status.set('loading');
    this.error.set(null);
    this.loading = api.get<MeResponse>('/me').subscribe({
      next: (me) => {
        this.me.set(me);
        this.status.set('ready');
      },
      error: (err: unknown) => {
        this.error.set(err instanceof Error ? err.message : 'Không tải được thông tin tài khoản.');
        this.status.set('error');
      },
    });
  }
}
