import { InvoiceSummary } from '../../../core/data/models';
import { matchesFilter } from './invoices-list.page';

describe('matchesFilter', () => {
  const inv = (status: InvoiceSummary['status']): InvoiceSummary => ({ invoiceId: 'x', period: '2026-11', status, total: 1, paidAt: null });

  it('splits paid from unpaid (sent or disputed)', () => {
    expect(matchesFilter(inv('PAID'), 'paid')).toBe(true);
    expect(matchesFilter(inv('SENT'), 'paid')).toBe(false);
    expect(matchesFilter(inv('SENT'), 'unpaid')).toBe(true);
    expect(matchesFilter(inv('DISPUTED'), 'unpaid')).toBe(true);
    expect(matchesFilter(inv('PAID'), 'all')).toBe(true);
  });
});
