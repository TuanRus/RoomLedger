import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from '../api/api.config';
import type { paths } from '../../generated/api-types';

type MeResponse = paths['/me']['get']['responses']['200']['content']['application/json'];

export type CurrentUser = MeResponse['data'];

@Injectable({
  providedIn: 'root',
})
export class MeApiService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getMe() {
    return this.http.get<MeResponse>(`${this.apiBaseUrl}/me`);
  }
}
