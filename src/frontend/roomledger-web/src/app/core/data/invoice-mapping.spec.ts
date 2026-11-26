import { toInvoiceDetail } from './invoice-mapping';

describe('toInvoiceDetail', () => {
  it('maps disputes and defaults to none', () => {
    expect(toInvoiceDetail({ invoiceId: 'i1' }).disputes).toEqual([]);
    const d = toInvoiceDetail({
      invoiceId: 'i1',
      status: 'DISPUTED',
      disputes: [{ disputeId: 'd1', readingId: 'r1', reason: 'Số sai', status: 'OPEN', createdAt: '2026-11-02T01:00:00Z' }],
    });
    expect(d.disputes).toEqual([
      { disputeId: 'd1', readingId: 'r1', reason: 'Số sai', status: 'OPEN', resolution: null, response: null, createdAt: '2026-11-02T01:00:00Z', resolvedAt: null },
    ]);
  });

  it('maps the payment block (qrUrl -> qrImageUrl) and is null without one', () => {
    expect(toInvoiceDetail({ invoiceId: 'i1' }).payment).toBeNull();
    const d = toInvoiceDetail({ invoiceId: 'i1', payment: { bankName: 'VCB', accountNumber: '0123456789', accountHolder: 'A', transferNote: 'P204 T11', qrUrl: 'https://x/qr.png' } });
    expect(d.payment).toEqual({ bankName: 'VCB', accountNumber: '0123456789', accountHolder: 'A', transferNote: 'P204 T11', qrImageUrl: 'https://x/qr.png' });
  });
});
