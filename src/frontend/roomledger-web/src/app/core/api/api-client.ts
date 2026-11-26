import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { EMPTY, Observable, catchError, expand, map, reduce, throwError } from 'rxjs';

import { ApiErrorResponse, ApiResponse, PagedResult } from '../../shared/models/api-response.model';
import { API_BASE_URL, DATA_SOURCE } from './api.config';

/** Error from the API in the standard {success:false, message, errorCode} shape, or a network failure. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly errorCode: string,
    message: string,
  ) {
    super(message);
  }
}

function toApiError(err: HttpErrorResponse): ApiError {
  if (err.status === 0) return new ApiError(0, 'NETWORK', 'Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.');
  const body = err.error as Partial<ApiErrorResponse> | null;
  return new ApiError(err.status, body?.errorCode ?? 'UNKNOWN', body?.message ?? 'Có lỗi xảy ra, thử lại sau.');
}

/** Thin wrapper over HttpClient: joins the API base URL and unwraps {success, message, data}. */
@Service()
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL).replace(/\/$/, '');

  get<T>(path: string, params?: Record<string, string | number | undefined>): Observable<T> {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(params ?? {})) if (v !== undefined) p = p.set(k, String(v));
    return this.unwrap(this.http.get<ApiResponse<T>>(this.base + path, { params: p }));
  }

  /** Every item of a paged list: follows nextCursor with the largest page the API allows (100). */
  getAll<T>(path: string, params?: Record<string, string | number | undefined>): Observable<T[]> {
    const page = (cursor?: string) => this.get<PagedResult<T>>(path, { ...params, limit: 100, cursor });
    return page().pipe(
      expand((p) => (p.nextCursor ? page(p.nextCursor) : EMPTY)),
      reduce((all, p) => all.concat(p.items), [] as T[]),
    );
  }

  post<T>(path: string, body: unknown = null): Observable<T> {
    return this.unwrap(this.http.post<ApiResponse<T>>(this.base + path, body));
  }

  patch<T>(path: string, body: unknown): Observable<T> {
    return this.unwrap(this.http.patch<ApiResponse<T>>(this.base + path, body));
  }

  private unwrap<T>(req: Observable<ApiResponse<T>>): Observable<T> {
    return req.pipe(
      map((res) => res.data as T),
      catchError((err: unknown) => throwError(() => (err instanceof HttpErrorResponse ? toApiError(err) : err))),
    );
  }
}

/** The API client in API mode, null with sample data. Call in an injection context. */
export function injectApi(): ApiClient | null {
  return inject(DATA_SOURCE) === 'api' ? inject(ApiClient) : null;
}
