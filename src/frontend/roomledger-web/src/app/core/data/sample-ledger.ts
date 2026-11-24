import { InvoiceDetail, InvoiceSummary, MeterKind, MeterReading, PaymentInfo, ReviewRow } from './models';

/**
 * Sample data for the frontend while the API is wired up separately (Tuấn).
 * Same story as the design mockups: Nhà trọ Cô Lan, kỳ 11/2026, review on 02/12/2026.
 * Every name, number, bank detail and QR code here is a sample.
 */
export const SAMPLE_PERIOD = '2026-11';

function reading(
  roomId: string,
  kind: MeterKind,
  previousValue: number,
  aiValue: number | null,
  opts: Partial<MeterReading> = {},
): MeterReading {
  return {
    readingId: `${roomId}-${kind}-${SAMPLE_PERIOD}`,
    roomId,
    kind,
    period: SAMPLE_PERIOD,
    status: 'PENDING_CONFIRM',
    value: aiValue,
    aiValue,
    aiConfidence: aiValue == null ? null : 0.95,
    previousValue,
    needsReview: false,
    reviewReason: null,
    editedByLandlord: false,
    confirmedAt: null,
    photoUrl: `sample/${kind === 'ELECTRICITY' ? 'elec' : 'water'}-${roomId.slice(2)}.jpg`,
    ...opts,
  };
}

function hist(...usages: number[]): ReviewRow['history'] {
  return usages.map((usage, i) => ({ period: `2026-${String(8 + i).padStart(2, '0')}`, usage }));
}

const at = (time: string) => `2026-12-01T${time}:00+07:00`;

export const ELECTRICITY_REVIEW: ReviewRow[] = [
  { roomId: 'r-101', roomCode: 'P.101', tenantName: 'Đặng Văn Long', reading: reading('r-101', 'ELECTRICITY', 2064, 2210), digitCropUrl: 'sample/d-elec-101.jpg', averageUsage: 140, capturedAt: at('07:42'), history: hist(138, 141, 141, 146) },
  { roomId: 'r-102', roomCode: 'P.102', tenantName: 'Mai Thị Hồng', reading: reading('r-102', 'ELECTRICITY', 1730, 1876), digitCropUrl: 'sample/d-elec-102.jpg', averageUsage: 149, capturedAt: at('07:44'), history: hist(150, 147, 150, 146) },
  { roomId: 'r-103', roomCode: 'P.103', tenantName: 'Cao Minh Đức', reading: reading('r-103', 'ELECTRICITY', 840, 964), digitCropUrl: 'sample/d-elec-103.jpg', averageUsage: 120, capturedAt: at('07:46'), history: hist(118, 121, 121, 124) },
  { roomId: 'r-105', roomCode: 'P.105', tenantName: 'Hà Minh Tú', reading: reading('r-105', 'ELECTRICITY', 1360, 1502), digitCropUrl: 'sample/d-elec-105.jpg', averageUsage: 141, capturedAt: at('07:49'), history: hist(139, 143, 141, 142) },
  { roomId: 'r-112', roomCode: 'P.112', tenantName: 'Vũ Ngọc Mai', reading: reading('r-112', 'ELECTRICITY', 1618, 1730), digitCropUrl: 'sample/d-elec-112.jpg', averageUsage: 119, capturedAt: at('07:55'), history: hist(117, 122, 118, 112) },
  { roomId: 'r-201', roomCode: 'P.201', tenantName: 'Nguyễn Hữu Phước', reading: reading('r-201', 'ELECTRICITY', 2960, 3120), digitCropUrl: 'sample/d-elec-201.jpg', averageUsage: 148, capturedAt: at('07:58'), history: hist(146, 150, 148, 160) },
  { roomId: 'r-202', roomCode: 'P.202', tenantName: 'Tạ Thu Hà', reading: reading('r-202', 'ELECTRICITY', 1300, 1440), digitCropUrl: 'sample/d-elec-202.jpg', averageUsage: 136, capturedAt: at('08:00'), history: hist(134, 137, 137, 140) },
  { roomId: 'r-204', roomCode: 'P.204', tenantName: 'Trần Minh Anh', reading: reading('r-204', 'ELECTRICITY', 1102, 1284), digitCropUrl: 'sample/d-elec-204.jpg', averageUsage: 167, capturedAt: at('08:01'), history: hist(162, 171, 168, 182) },
  {
    roomId: 'r-305', roomCode: 'P.305', tenantName: 'Phạm Quốc Huy',
    reading: reading('r-305', 'ELECTRICITY', 2620, 2988, { status: 'NEEDS_REVIEW', needsReview: true, reviewReason: 'USAGE_SPIKE', aiConfidence: 0.92 }),
    digitCropUrl: 'sample/d-elec-305.jpg', averageUsage: 224, capturedAt: at('08:14'), history: hist(214, 231, 227, 368),
  },
  {
    roomId: 'r-306', roomCode: 'P.306', tenantName: 'Lâm Gia Bảo',
    reading: reading('r-306', 'ELECTRICITY', 1940, null, { status: 'NEEDS_REVIEW', needsReview: true, reviewReason: 'AI_UNREADABLE', photoUrl: 'sample/elec-blurry.jpg' }),
    digitCropUrl: 'sample/d-elec-blurry.jpg', averageUsage: 151, capturedAt: at('08:20'), history: hist(149, 152, 151, 0),
  },
  { roomId: 'r-307', roomCode: 'P.307', tenantName: 'Đỗ Thị Hà', reading: null, digitCropUrl: null, averageUsage: 128, capturedAt: null, history: hist(126, 130, 128, 0) },
  { roomId: 'r-308', roomCode: 'P.308', tenantName: 'Lý Văn Tâm', reading: null, digitCropUrl: null, averageUsage: 133, capturedAt: null, history: hist(131, 135, 133, 0) },
  { roomId: 'r-113', roomCode: 'P.113', tenantName: 'Vương Thu Nga', reading: null, digitCropUrl: null, averageUsage: 117, capturedAt: null, history: hist(115, 119, 117, 0) },
];

export const WATER_REVIEW: ReviewRow[] = [
  { roomId: 'r-204', roomCode: 'P.204', tenantName: 'Trần Minh Anh', reading: reading('r-204', 'WATER', 61, 67), digitCropUrl: 'sample/d-water-204.jpg', averageUsage: 6, capturedAt: at('08:02'), history: hist(6, 6, 5, 6) },
  {
    roomId: 'r-112', roomCode: 'P.112', tenantName: 'Vũ Ngọc Mai',
    reading: reading('r-112', 'WATER', 136, 148, { status: 'NEEDS_REVIEW', needsReview: true, reviewReason: 'USAGE_SPIKE', aiConfidence: 0.9 }),
    digitCropUrl: 'sample/d-water-112.jpg', averageUsage: 7, capturedAt: at('07:56'), history: hist(7, 6, 8, 12),
  },
];

export const TENANT_INVOICE: InvoiceDetail = {
  invoiceId: 'inv-204-2026-11',
  roomCode: 'P.204',
  propertyName: 'Nhà trọ Cô Lan',
  period: SAMPLE_PERIOD,
  status: 'SENT',
  roomRent: 2_500_000,
  electricity: { readingId: 'r-204-ELECTRICITY-2026-11', previous: 1102, current: 1284, usage: 182, rate: 3_500, amount: 637_000, photoUrl: 'sample/elec-204.jpg' },
  water: { readingId: 'r-204-WATER-2026-11', previous: 61, current: 67, usage: 6, rate: 18_000, amount: 108_000, photoUrl: 'sample/water-204.jpg' },
  serviceFee: 0,
  total: 3_245_000,
  sentAt: '2026-12-02T16:06:00+07:00',
  paidAt: null,
  dueDate: '2026-12-10',
};

export const TENANT_INVOICE_HISTORY: InvoiceSummary[] = [
  { invoiceId: 'inv-204-2026-11', period: '2026-11', status: 'SENT', total: 3_245_000, paidAt: null },
  { invoiceId: 'inv-204-2026-10', period: '2026-10', status: 'PAID', total: 3_168_000, paidAt: '2026-11-08T19:20:00+07:00' },
  { invoiceId: 'inv-204-2026-09', period: '2026-09', status: 'PAID', total: 3_201_000, paidAt: '2026-10-09T20:05:00+07:00' },
  { invoiceId: 'inv-204-2026-08', period: '2026-08', status: 'PAID', total: 3_122_000, paidAt: '2026-09-07T18:40:00+07:00' },
];

export const LANDLORD_PAYMENT_INFO: PaymentInfo = {
  bankName: 'Ngân hàng mẫu',
  accountNumber: '0000 1234 5678',
  accountHolder: 'LE THI LAN',
  transferNote: 'P204 T11',
  qrImageUrl: 'sample/qr-mau.png',
};
