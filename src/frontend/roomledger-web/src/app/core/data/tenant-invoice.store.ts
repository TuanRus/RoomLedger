import { Service, signal } from '@angular/core';

import { InvoiceDetail, InvoiceSummary, PaymentInfo } from './models';
import { LANDLORD_PAYMENT_INFO, TENANT_INVOICE, TENANT_INVOICE_HISTORY } from './sample-ledger';

/**
 * The signed-in tenant's invoices. Backed by sample data until the API is connected:
 * replace with GET /invoices and GET /invoices/{id}; payment details are not in the contract yet.
 */
@Service()
export class TenantInvoiceStore {
  readonly history = signal<InvoiceSummary[]>(TENANT_INVOICE_HISTORY);
  readonly payment = signal<PaymentInfo>(LANDLORD_PAYMENT_INFO);

  detail(invoiceId: string): InvoiceDetail | null {
    return invoiceId === TENANT_INVOICE.invoiceId ? TENANT_INVOICE : null;
  }
}
