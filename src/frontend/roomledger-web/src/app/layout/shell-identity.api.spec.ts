import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL, DATA_SOURCE } from '../core/api/api.config';
import { landlordIdentity } from './landlord-shell';
import { tenantIdentity } from './tenant-shell';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

function setup(source: 'sample' | 'api') {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: source }, { provide: API_BASE_URL, useValue: BASE }],
  });
  return TestBed.inject(HttpTestingController);
}

describe('shell identity', () => {
  it('keeps the sample text with sample data', () => {
    const http = setup('sample');
    const landlord = TestBed.runInInjectionContext(() => landlordIdentity())();
    const tenant = TestBed.runInInjectionContext(() => tenantIdentity())();
    expect(landlord.place).toEqual({ title: 'Nhà trọ Cô Lan', sub: '36 phòng · 32 đang thuê' });
    expect(landlord.person).toEqual({ initials: 'LL', name: 'Lê Thị Lan', role: 'Chủ nhà' });
    expect(tenant.place).toEqual({ title: 'Phòng P.204', sub: 'Nhà trọ Cô Lan' });
    expect(tenant.person).toEqual({ initials: 'MA', name: 'Trần Minh Anh', role: 'Người thuê' });
    http.verify();
  });

  it('landlord: shows dashes while loading, then /me and /properties', () => {
    const http = setup('api');
    const identity = TestBed.runInInjectionContext(() => landlordIdentity());
    expect(identity().place.title).toBe('—');
    expect(identity().person.name).toBe('');

    http.expectOne(`${BASE}/me`).flush(ok({ userId: 'u', fullName: 'Nguyễn Văn Lan', email: 'l@x', role: 'LANDLORD' }));
    http.expectOne(`${BASE}/properties?limit=1`).flush(ok({ items: [{ propertyId: 'p1', name: 'Nhà trọ Hoa', roomCount: 3 }] }));
    http.expectOne(`${BASE}/properties/p1/rooms?limit=100`).flush(ok({ items: [{ roomId: 'a', status: 'OCCUPIED' }, { roomId: 'b', status: 'VACANT' }, { roomId: 'c', status: 'OCCUPIED' }] }));

    expect(identity().place).toEqual({ title: 'Nhà trọ Hoa', sub: '3 phòng · 2 đang thuê' });
    expect(identity().person).toEqual({ initials: 'VL', name: 'Nguyễn Văn Lan', role: 'Chủ nhà' });
    http.verify();
  });

  it('tenant: shows the room from /tenant/home', () => {
    const http = setup('api');
    const identity = TestBed.runInInjectionContext(() => tenantIdentity());
    expect(identity().place.title).toBe('—');

    http.expectOne(`${BASE}/me`).flush(ok({ userId: 'u', fullName: 'Trần Minh Anh', email: 'm@x', role: 'TENANT' }));
    http.expectOne(`${BASE}/tenant/home`).flush(ok({ tenancyId: 't1', roomCode: 'P.204', propertyName: 'Nhà trọ Hoa' }));

    expect(identity().place).toEqual({ title: 'Phòng P.204', sub: 'Nhà trọ Hoa' });
    expect(identity().person.initials).toBe('MA');
    http.verify();
  });

  it('tenant without a tenancy', () => {
    const http = setup('api');
    const identity = TestBed.runInInjectionContext(() => tenantIdentity());
    http.expectOne(`${BASE}/me`).flush(ok({ fullName: 'A B' }));
    http.expectOne(`${BASE}/tenant/home`).flush(ok({ tenancyId: null }));
    expect(identity().place).toEqual({ title: 'Chưa gắn phòng', sub: '' });
    http.verify();
  });
});
