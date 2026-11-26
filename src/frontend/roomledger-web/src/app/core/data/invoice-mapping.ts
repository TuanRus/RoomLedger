import type { components } from '../../generated/api-types';
import { InvoiceDetail, InvoiceDispute, PaymentInfo, UtilityLine } from './models';

export type InvoiceDetailResponse = components['schemas']['InvoiceDetailResponse'];
type DisputeResponse = components['schemas']['DisputeResponse'];

export function lineOf(l: components['schemas']['UtilityLine'] | undefined): UtilityLine {
  return {
    readingId: l?.readingId ?? '',
    previous: l?.previous ?? 0,
    current: l?.current ?? 0,
    usage: l?.usage ?? 0,
    rate: l?.rate ?? 0,
    amount: l?.amount ?? 0,
    photoUrl: l?.photoUrl ?? null,
  };
}

export function toDispute(d: DisputeResponse): InvoiceDispute {
  return {
    disputeId: d.disputeId ?? '',
    readingId: d.readingId ?? null,
    reason: d.reason ?? '',
    status: d.status ?? 'OPEN',
    resolution: (d.resolution as InvoiceDispute['resolution']) ?? null,
    response: d.response ?? null,
    createdAt: d.createdAt ?? '',
    resolvedAt: d.resolvedAt ?? null,
  };
}

export function toPayment(p: InvoiceDetailResponse['payment']): PaymentInfo | null {
  return p
    ? { bankName: p.bankName ?? '', accountNumber: p.accountNumber ?? '', accountHolder: p.accountHolder ?? '', transferNote: p.transferNote ?? '', qrImageUrl: p.qrUrl ?? null }
    : null;
}

/** InvoiceDetailResponse -> the screen model. dueDate is not in the API yet (API-GAPS.md). */
export function toInvoiceDetail(d: InvoiceDetailResponse): InvoiceDetail {
  return {
    invoiceId: d.invoiceId ?? '',
    roomCode: d.roomCode ?? '',
    propertyName: d.propertyName ?? '',
    period: d.period ?? '',
    status: d.status ?? 'SENT',
    roomRent: d.roomRent ?? 0,
    electricity: lineOf(d.electricity),
    water: lineOf(d.water),
    serviceFee: d.serviceFee ?? 0,
    total: d.total ?? 0,
    sentAt: d.sentAt ?? null,
    paidAt: d.paidAt ?? null,
    dueDate: null,
    disputes: (d.disputes ?? []).map(toDispute),
    payment: toPayment(d.payment),
  };
}
