import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { API_BASE_URL, DATA_SOURCE } from '../../../core/api/api.config';
import { InvitePage, expiryLabel, toInviteView } from './invite.page';

const BASE = 'http://api.test/api/v1';

function open(token: string) {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(InvitePage);
  fixture.componentRef.setInput('token', token);
  fixture.detectChanges();
  return { fixture, http, el: fixture.nativeElement as HTMLElement };
}

describe('toInviteView / expiryLabel', () => {
  it('maps the preview and leaves out what the API does not have', () => {
    const v = toInviteView({ propertyName: 'Nhà trọ Hoa', roomCode: 'P.5', emailMasked: 'a***b@x.vn', expiresAt: '2026-12-01T10:00:00Z' });
    expect(v).toEqual({ propertyName: 'Nhà trọ Hoa', roomCode: 'P.5', emailMasked: 'a***b@x.vn', expiresAt: '2026-12-01T10:00:00Z' });
    expect(v.landlordName).toBeUndefined();
  });

  it('formats the expiry and survives garbage', () => {
    expect(expiryLabel('2026-12-01T10:00:00')).toBe('01/12/2026, 10:00');
    expect(expiryLabel('nope')).toBe('');
  });
});

describe('InvitePage with the API', () => {
  it('shows the invite from GET /invites/{token}, with only what the API has', async () => {
    const { fixture, http, el } = open('abc');
    expect(el.textContent).toContain('Đang kiểm tra lời mời');
    http.expectOne(`${BASE}/invites/abc`).flush({ success: true, message: 'OK', data: { propertyName: 'Nhà trọ Hoa', roomCode: 'P.5', emailMasked: 'a***b@x.vn', expiresAt: '2026-12-01T10:00:00' } });
    fixture.detectChanges();

    expect(el.querySelector('h1')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Bạn được mời thuê phòng P.5');
    expect(el.textContent).toContain('Nhà trọ Hoa');
    expect((el.querySelector('#inv-email') as HTMLInputElement).value).toBe('a***b@x.vn');
    expect(el.textContent).not.toContain('Lê Thị Lan');
    expect(el.textContent).not.toContain('Cô Lan');
    http.verify();
  });

  it('does not fake accepting the invite: the button is off and the page says why', () => {
    const { fixture, http, el } = open('abc');
    http.expectOne(`${BASE}/invites/abc`).flush({ success: true, message: 'OK', data: { propertyName: 'Nhà trọ Hoa', roomCode: 'P.5', emailMasked: 'a***b@x.vn', expiresAt: '2026-12-01T10:00:00' } });
    fixture.detectChanges();
    expect(el.textContent).toContain('Chưa nhận lời mời được trên hệ thống: đăng nhập thật đang chờ hoàn thiện.');
    const submit = el.querySelector('button[type=submit]') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    // Submitting by Enter does not navigate or show a fake success either.
    el.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
    expect(el.textContent).not.toContain('Đang tạo tài khoản');
    http.verify();
  });

  it('says the link no longer works on 410, with the API message', () => {
    const { fixture, http, el } = open('old');
    http.expectOne(`${BASE}/invites/old`).flush({ success: false, message: 'Lời mời không còn hiệu lực', errorCode: 'INVITE_INVALID', traceId: 't' }, { status: 410, statusText: 'Gone' });
    fixture.detectChanges();
    expect(el.textContent).toContain('Không mở được lời mời');
    expect(el.textContent).toContain('Lời mời không còn hiệu lực');
    http.verify();
  });

  it('offers a retry on a network failure', () => {
    const { fixture, http, el } = open('abc');
    http.expectOne(`${BASE}/invites/abc`).error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();
    expect(el.querySelector('[role=alert]')?.textContent).toContain('Không kết nối được máy chủ');
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Thử lại')) as HTMLButtonElement).click();
    http.expectOne(`${BASE}/invites/abc`);
    http.verify();
  });
});
