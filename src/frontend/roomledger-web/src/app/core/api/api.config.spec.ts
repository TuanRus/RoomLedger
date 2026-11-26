import { provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { devAuthInterceptor } from '../auth/dev-auth.interceptor';
import { DevSession } from '../dev/dev-session';
import { DEV_LANDLORD, devUserHeader } from '../dev/dev-users';
import { API_BASE_URL, isApiRequest } from './api.config';

describe('isApiRequest', () => {
  const base = 'http://api.test/api/v1';

  it('matches the base path itself and anything below it', () => {
    expect(isApiRequest('http://api.test/api/v1', base)).toBe(true);
    expect(isApiRequest('http://api.test/api/v1/me', base)).toBe(true);
    expect(isApiRequest('http://api.test/api/v1/invoices?limit=1', base)).toBe(true);
  });

  it('does not match a path that only starts with the base path, another origin or a presigned URL', () => {
    expect(isApiRequest('http://api.test/api/v10/me', base)).toBe(false);
    expect(isApiRequest('http://api.test/api/v1-old/me', base)).toBe(false);
    expect(isApiRequest('http://other.test/api/v1/me', base)).toBe(false);
    expect(isApiRequest('https://bucket.s3.amazonaws.com/api/v1/file?sig=1', base)).toBe(false);
  });

  it('works with a path base served through the dev proxy, with or without a trailing slash', () => {
    expect(isApiRequest('/api/v1/me', '/api/v1')).toBe(true);
    expect(isApiRequest('/api/v1/me', '/api/v1/')).toBe(true);
    expect(isApiRequest('/api/v2/me', '/api/v1')).toBe(false);
  });
});

describe('devAuthInterceptor', () => {
  const BASE = 'http://api.test/api/v1';
  let http: HttpTestingController;
  let client: HttpClient;

  beforeEach(() => {
    localStorage.setItem('roomledger.devRole', 'landlord');
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([devAuthInterceptor])), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: BASE }],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => {
    http.verify();
    localStorage.removeItem('roomledger.devRole');
  });

  it('adds X-Dev-User to API requests only', () => {
    client.get(`${BASE}/me`).subscribe();
    expect(http.expectOne(`${BASE}/me`).request.headers.get('X-Dev-User')).toBe(devUserHeader(DEV_LANDLORD));

    client.put('https://bucket.s3.amazonaws.com/file?sig=1', 'x').subscribe();
    expect(http.expectOne('https://bucket.s3.amazonaws.com/file?sig=1').request.headers.has('X-Dev-User')).toBe(false);

    client.get('http://api.test/api/v10/me').subscribe();
    expect(http.expectOne('http://api.test/api/v10/me').request.headers.has('X-Dev-User')).toBe(false);
  });

  it('sends no header before a person is chosen', () => {
    TestBed.inject(DevSession).user.set(null);
    client.get(`${BASE}/me`).subscribe();
    expect(http.expectOne(`${BASE}/me`).request.headers.has('X-Dev-User')).toBe(false);
  });
});
