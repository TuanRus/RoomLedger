import { InjectionToken } from '@angular/core';
import { environment } from '../../../environments/environment';

export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => environment.apiBaseUrl,
});

/** Where the screens get their data (see Environment.dataSource). The default build and the tests use sample data. */
export const DATA_SOURCE = new InjectionToken<'sample' | 'api'>('DATA_SOURCE', {
  providedIn: 'root',
  factory: () => environment.dataSource,
});

/**
 * True when the URL points at the RoomLedger API: an absolute base, or a path base served through the dev proxy.
 * The path has to be the base path itself or sit below it ("/api/v1" or "/api/v1/..."), not just start with it ("/api/v10").
 */
export function isApiRequest(url: string, apiBaseUrl: string): boolean {
  const origin = globalThis.location?.origin ?? 'http://localhost';
  const base = new URL(apiBaseUrl, origin);
  const target = new URL(url, origin);
  if (target.origin !== base.origin) return false;
  const basePath = base.pathname.replace(/\/+$/, '');
  return target.pathname === basePath || target.pathname.startsWith(basePath + '/');
}
