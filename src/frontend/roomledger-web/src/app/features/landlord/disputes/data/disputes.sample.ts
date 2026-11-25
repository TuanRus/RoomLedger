import { MeterKind } from '../../../../core/data/models';
import type { components } from '../../../../generated/api-types';

type Schemas = components['schemas'];

/**
 * One tenant dispute as the landlord handles it (LL-08). Built on DisputeResponse; the contract does not
 * carry yet: roomCode, tenantName, period, which meter it is about (kind), the reading values
 * (previous, AI, approved, tenant's) and the photo. P.207 and P.305 are about the whole invoice (no reading).
 */
export interface LandlordDispute {
  disputeId: string;
  invoiceId: string;
  readingId: string | null;
  roomCode: string;
  tenantName: string;
  period: string;
  reason: string;
  status: Schemas['DisputeStatus'];
  resolution: Schemas['DisputeResolution'] | null;
  response: string | null;
  createdAt: string;
  resolvedAt: string | null;
  /** Null when the dispute is about the invoice as a whole, not one reading. */
  reading: {
    kind: MeterKind;
    previous: number;
    aiValue: number;
    approved: number;
    approvedAt: string;
    tenantValue: number;
    rate: number;
    photoUrl: string;
    capturedAt: string;
    /** Value after "Sửa chỉ số". */
    corrected: number | null;
  } | null;
}

/** Sample clock: when the landlord answers in this session (00-system: P.204 corrected on 04/12). */
export const SAMPLE_RESOLVED_AT = '2026-12-04T09:30:00+07:00';

export const LANDLORD_DISPUTES: LandlordDispute[] = [
  {
    disputeId: 'dp-112-2026-11', invoiceId: 'inv-112-2026-11', readingId: 'r-112-WATER-2026-11',
    roomCode: 'P.112', tenantName: 'Vũ Ngọc Mai', period: '2026-11',
    reason: 'Đồng hồ ghi 142 m³ nhưng hóa đơn tính 148 m³. Phóng to ảnh gốc cũng thấy số 142.',
    status: 'OPEN', resolution: null, response: null, createdAt: '2026-12-02T19:40:00+07:00', resolvedAt: null,
    reading: { kind: 'WATER', previous: 136, aiValue: 148, approved: 148, approvedAt: '2026-12-01T09:10:00+07:00', tenantValue: 142, rate: 18_000, photoUrl: 'sample/water-112.jpg', capturedAt: '2026-12-01T07:56:00+07:00', corrected: null },
  },
  {
    disputeId: 'dp-204-2026-11', invoiceId: 'inv-204-2026-11', readingId: 'r-204-WATER-2026-11',
    roomCode: 'P.204', tenantName: 'Trần Minh Anh', period: '2026-11',
    reason: 'Em xem đồng hồ nước sáng nay chỉ mới 65 m³, hóa đơn ghi 67 m³. Số cuối trên ảnh bị lóa nên khó đọc.',
    status: 'OPEN', resolution: null, response: null, createdAt: '2026-12-03T07:15:00+07:00', resolvedAt: null,
    reading: { kind: 'WATER', previous: 61, aiValue: 67, approved: 67, approvedAt: '2026-12-02T09:20:00+07:00', tenantValue: 65, rate: 18_000, photoUrl: 'sample/water-204.jpg', capturedAt: '2026-12-01T08:02:00+07:00', corrected: null },
  },
  {
    disputeId: 'dp-207-2026-11', invoiceId: 'inv-207-2026-11', readingId: null,
    roomCode: 'P.207', tenantName: 'Phan Thu Trang', period: '2026-11',
    reason: 'Tháng 11 em về quê 10 ngày mà tiền điện vẫn 462.000 đ, gần bằng mọi tháng. Anh chị xem lại giúp em.',
    status: 'OPEN', resolution: null, response: null, createdAt: '2026-12-03T12:05:00+07:00', resolvedAt: null,
    reading: null,
  },
  {
    disputeId: 'dp-305-2026-10', invoiceId: 'inv-305-2026-10', readingId: null,
    roomCode: 'P.305', tenantName: 'Phạm Quốc Huy', period: '2026-10',
    reason: 'Tiền điện kỳ 10 cao hơn em nghĩ, anh chị kiểm tra lại chỉ số giúp em.',
    status: 'RESOLVED', resolution: 'KEPT',
    response: 'Chị đã xem lại ảnh công tơ ngày 01/11: số 2.620, kỳ trước 2.393, dùng 227 kWh, đúng như các tháng trước. Chị giữ nguyên hóa đơn.',
    createdAt: '2026-11-08T20:10:00+07:00', resolvedAt: '2026-11-09T08:30:00+07:00',
    reading: null,
  },
];
