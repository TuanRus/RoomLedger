import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../api/api.config';
import { MeStore, initialsOf } from './me.store';

const BASE = 'http://api.test/api/v1';

describe('initialsOf', () => {
  it('takes the last two words', () => {
    expect(initialsOf('Trần Minh Anh')).toBe('MA');
    expect(initialsOf('Lan')).toBe('L');
    expect(initialsOf('  ')).toBe('');
  });
});

describe('MeStore', () => {
  function setup(source: 'sample' | 'api') {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: source }, { provide: API_BASE_URL, useValue: BASE }],
    });
    return { store: TestBed.inject(MeStore), http: TestBed.inject(HttpTestingController) };
  }

  it('loads GET /me', () => {
    const { store, http } = setup('api');
    expect(store.status()).toBe('loading');
    http.expectOne(`${BASE}/me`).flush({ success: true, message: 'OK', data: { userId: 'u1', fullName: 'Lê Thị Lan', email: 'lan@example.com', role: 'LANDLORD' } });
    expect(store.status()).toBe('ready');
    expect(store.me()?.fullName).toBe('Lê Thị Lan');
    expect(store.initials()).toBe('TL');
    http.verify();
  });

  it('shows the error message when the call fails', () => {
    const { store, http } = setup('api');
    http.expectOne(`${BASE}/me`).flush({ success: false, message: 'Chưa đăng nhập.', errorCode: 'UNAUTHORIZED' }, { status: 401, statusText: 'Unauthorized' });
    expect(store.status()).toBe('error');
    expect(store.error()).toBe('Chưa đăng nhập.');
    http.verify();
  });

  it('makes no request with sample data', () => {
    const { store, http } = setup('sample');
    expect(store.me()).toBeNull();
    expect(store.status()).toBe('ready');
    http.verify();
  });
});
