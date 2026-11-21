import { computed, inject, Injectable, signal } from '@angular/core';

import { MeApiService, type CurrentUser } from './me-api.service';

@Injectable({
  providedIn: 'root',
})
export class SessionService {
  private readonly meApi = inject(MeApiService);

  private readonly userState = signal<CurrentUser | null>(null);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<unknown | null>(null);

  readonly user = this.userState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();

  readonly isLoaded = computed(() => this.userState() !== null);

  loadMe(): void {
    this.loadingState.set(true);
    this.errorState.set(null);

    this.meApi.getMe().subscribe({
      next: (response) => {
        this.userState.set(response.data);
        this.loadingState.set(false);
      },
      error: (error: unknown) => {
        this.errorState.set(error);
        this.loadingState.set(false);
      },
    });
  }

  clear(): void {
    this.userState.set(null);
    this.errorState.set(null);
    this.loadingState.set(false);
  }
}
