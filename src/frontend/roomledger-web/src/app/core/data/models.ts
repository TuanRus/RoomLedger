import type { components } from '../../generated/api-types';

type Schemas = components['schemas'];

export type MeterKind = Schemas['MeterKind'];
export type MeterReadingStatus = Schemas['MeterReadingStatus'];
export type InvoiceStatus = Schemas['InvoiceStatus'];

/** MeterReadingResponse from the API contract, with the fields the screens rely on made required. */
export type MeterReading = Required<
  Pick<
    Schemas['MeterReadingResponse'],
    | 'readingId'
    | 'roomId'
    | 'kind'
    | 'period'
    | 'status'
    | 'value'
    | 'aiValue'
    | 'aiConfidence'
    | 'previousValue'
    | 'needsReview'
    | 'reviewReason'
    | 'editedByLandlord'
    | 'confirmedAt'
    | 'photoUrl'
  >
>;

/**
 * One room in the landlord's meter review (LL-04 / LM-01).
 * Not in RoomReadingsResponse yet (see API-GAPS.md): tenantName, digitCropUrl,
 * averageUsage (mean of the last 3 periods) and capturedAt.
 */
export interface ReviewRow {
  roomId: string;
  roomCode: string;
  tenantName: string;
  reading: MeterReading | null;
  digitCropUrl: string | null;
  averageUsage: number;
  capturedAt: string | null;
  history: { period: string; usage: number }[];
}

/** How the review screen groups a row. Derived from the reading, never stored. */
export type ReviewState = 'pending' | 'abnormal' | 'unreadable' | 'unrecorded' | 'approved';

export type UtilityLine = Required<Schemas['UtilityLine']>;

/** InvoiceDetailResponse with required fields, plus dueDate which the contract does not carry yet (null from the API). */
export interface InvoiceDetail {
  invoiceId: string;
  roomCode: string;
  propertyName: string;
  period: string;
  status: InvoiceStatus;
  roomRent: number;
  electricity: UtilityLine;
  water: UtilityLine;
  serviceFee: number;
  total: number;
  sentAt: string | null;
  paidAt: string | null;
  dueDate: string | null;
  /** Disputes raised on this invoice (DisputeResponse); absent in the sample data. */
  disputes?: InvoiceDispute[];
}

/** DisputeResponse with the optional fields made explicit. */
export interface InvoiceDispute {
  disputeId: string;
  readingId: string | null;
  reason: string;
  status: Schemas['DisputeStatus'];
  resolution: Schemas['DisputeResolution'] | null;
  response: string | null;
  createdAt: string;
  resolvedAt: string | null;
}

export interface InvoiceSummary {
  invoiceId: string;
  period: string;
  status: InvoiceStatus;
  total: number;
  paidAt: string | null;
}

/**
 * The landlord's bank transfer details shown on a tenant invoice. Not in the API contract yet.
 * On a computer the tenant sees qrImageUrl; on a phone only a button that copies accountNumber.
 */
export interface PaymentInfo {
  bankName: string;
  accountNumber: string;
  accountHolder: string;
  transferNote: string;
  qrImageUrl: string | null;
}
