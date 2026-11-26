import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { API_BASE_URL, DATA_SOURCE } from '../../../core/api/api.config';
import { TenantInvoicePage } from './invoice.page';

const BASE = 'http://api.test/api/v1';
const ok = (data: unknown) => ({ success: true, message: 'OK', data });

function detail(qrUrl: string) {
  const line = { readingId: 'r', previous: 100, current: 160, usage: 60, rate: 3500, amount: 210000, photoUrl: null };
  return {
    invoiceId: 'b', tenancyId: 't', roomCode: 'P.204', propertyName: 'Nhà trọ', period: '2026-10', status: 'SENT', roomRent: 3500000,
    electricity: line, water: { ...line, rate: 20000, amount: 1200000 }, serviceFee: 100000, total: 5010000, sentAt: '2026-11-02T01:00:00Z', paidAt: null, disputes: [],
    payment: { bankName: 'Vietcombank', accountNumber: '0123456789', accountHolder: 'LE THI LAN', transferNote: 'P204 T10', qrUrl },
  };
}

describe('TenantInvoicePage with the API', () => {
  beforeEach(() => {
    window.matchMedia ??= ((q: string) => ({ matches: false, media: q, addEventListener: () => undefined, removeEventListener: () => undefined })) as unknown as typeof window.matchMedia;
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: DATA_SOURCE, useValue: 'api' }, { provide: API_BASE_URL, useValue: BASE }],
    });
  });

  it('reads the invoice again once when the QR image fails, to get a fresh link', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TenantInvoicePage);
    fixture.componentRef.setInput('invoiceId', 'b');
    fixture.detectChanges();
    http.expectOne(`${BASE}/invoices?limit=24`).flush(ok({ items: [{ invoiceId: 'b', period: '2026-10', status: 'SENT', total: 5010000, paidAt: null }] }));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('https://s3.test/old.png')));
    fixture.detectChanges();
    await fixture.whenStable();

    const img = (fixture.nativeElement as HTMLElement).querySelector<HTMLImageElement>('img.qr');
    expect(img?.getAttribute('src')).toContain('old.png');
    img?.dispatchEvent(new Event('error'));
    http.expectOne(`${BASE}/invoices/b`).flush(ok(detail('https://s3.test/fresh.png')));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('img.qr')?.getAttribute('src')).toContain('fresh.png');

    // A second failure does not start a loop of reads.
    (fixture.nativeElement as HTMLElement).querySelector('img.qr')?.dispatchEvent(new Event('error'));
    http.expectNone(`${BASE}/invoices/b`);
    http.verify();
  });
});
