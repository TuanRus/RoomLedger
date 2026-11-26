import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ApiClient, injectApi } from './api-client';
import { API_BASE_URL, DATA_SOURCE } from './api.config';

const BASE = 'http://api.test/api/v1';

function setup(source: 'sample' | 'api') {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: source }, { provide: API_BASE_URL, useValue: BASE }],
  });
}

describe('ApiClient.getAll', () => {
  it('follows nextCursor with limit 100 and joins the pages', () => {
    setup('api');
    const http = TestBed.inject(HttpTestingController);
    let result: number[] | null = null;
    TestBed.inject(ApiClient)
      .getAll<number>('/things', { period: '2026-10' })
      .subscribe((r) => (result = r));

    http.expectOne(`${BASE}/things?period=2026-10&limit=100`).flush({ success: true, message: 'OK', data: { items: [1, 2], nextCursor: 'c2' } });
    http.expectOne(`${BASE}/things?period=2026-10&limit=100&cursor=c2`).flush({ success: true, message: 'OK', data: { items: [3] } });

    expect(result).toEqual([1, 2, 3]);
    http.verify();
  });
});

describe('injectApi', () => {
  it('is null with sample data', () => {
    setup('sample');
    expect(TestBed.runInInjectionContext(() => injectApi())).toBeNull();
  });

  it('is the client in API mode', () => {
    setup('api');
    expect(TestBed.runInInjectionContext(() => injectApi())).toBeInstanceOf(ApiClient);
  });
});
