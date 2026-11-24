import { InvoiceDetail, InvoiceStatus, MeterKind } from '../../../../core/data/models';

/**
 * One invoice as the landlord sees it (LL-05). InvoiceDetailResponse plus fields the contract
 * does not carry yet: tenantName, dueDate, remindedAt and the correction made after a dispute.
 */
export interface LandlordInvoice extends InvoiceDetail {
  roomId: string;
  tenantName: string;
  remindedAt: string | null;
  correction: { kind: MeterKind; from: number; to: number; at: string } | null;
}

export const ELECTRICITY_RATE = 3_500;
export const WATER_RATE = 18_000;

/**
 * Sample data for the landlord's invoices, same story as the mockups (00-system "Sổ tiền mẫu"):
 * kỳ 11/2026 at 02/12/2026 16:00. 27 invoices: 7 sent (2 paid), 20 drafts totalling 61.920.000 đ.
 * 5 rooms have no invoice yet because their readings are not approved. 2 invoices from kỳ 10 are overdue.
 * Invoices with an open dispute are DISPUTED (BR-24): P.112, P.204, P.207.
 */
export const INVOICE_PERIOD = '2026-11';
export const INVOICE_DUE_DATE = '2026-12-10';
export const ROOMS_WITHOUT_INVOICE = ['P.110', 'P.113', 'P.214', 'P.307', 'P.308'];

/** Sample clock: the moments the mockups show for each action. */
export const SAMPLE_TODAY = '2026-12-02';
export const SAMPLE_SENT_AT = '2026-12-02T16:06:00+07:00';
export const SAMPLE_REMINDED_AT = '2026-12-02T16:10:00+07:00';
export const SAMPLE_PAID_AT = '2026-12-06T08:12:00+07:00';

const ELEC_PHOTOS = new Set(['101', '102', '103', '105', '112', '201', '202', '204', '305']);
const WATER_PHOTOS = new Set(['112', '204']);

function inv(
  num: string,
  tenantName: string,
  status: InvoiceStatus,
  [ePrev, eUse]: [number, number],
  [wPrev, wUse]: [number, number],
  opts: Partial<LandlordInvoice> = {},
): LandlordInvoice {
  const period = opts.period ?? INVOICE_PERIOD;
  const electricity = {
    readingId: `r-${num}-ELECTRICITY-${period}`, previous: ePrev, current: ePrev + eUse, usage: eUse,
    rate: ELECTRICITY_RATE, amount: eUse * ELECTRICITY_RATE, photoUrl: ELEC_PHOTOS.has(num) ? `sample/elec-${num}.jpg` : null,
  };
  const water = {
    readingId: `r-${num}-WATER-${period}`, previous: wPrev, current: wPrev + wUse, usage: wUse,
    rate: WATER_RATE, amount: wUse * WATER_RATE, photoUrl: WATER_PHOTOS.has(num) ? `sample/water-${num}.jpg` : null,
  };
  const roomRent = 2_500_000;
  const sent = status !== 'DRAFT';
  return {
    invoiceId: `inv-${num}-${period}`,
    roomId: `r-${num}`,
    roomCode: `P.${num}`,
    propertyName: 'Nhà trọ Cô Lan',
    tenantName,
    period,
    status,
    roomRent,
    electricity,
    water,
    serviceFee: 0,
    total: roomRent + electricity.amount + water.amount,
    sentAt: sent ? '2026-12-02T10:30:00+07:00' : null,
    paidAt: status === 'PAID' ? '2026-12-02T14:20:00+07:00' : null,
    dueDate: INVOICE_DUE_DATE,
    remindedAt: null,
    correction: null,
    ...opts,
  };
}

const K10 = { period: '2026-10', dueDate: '2026-11-10', sentAt: '2026-11-02T16:00:00+07:00' };

export const LANDLORD_INVOICES: LandlordInvoice[] = [
  inv('101', 'Đặng Văn Long', 'DRAFT', [2064, 146], [88, 5]),
  inv('102', 'Mai Thị Hồng', 'DRAFT', [1730, 146], [92, 6]),
  inv('103', 'Cao Minh Đức', 'PAID', [840, 144], [70, 4]),
  inv('104', 'Bùi Thanh Tâm', 'DRAFT', [1512, 130], [81, 5]),
  inv('105', 'Hà Minh Tú', 'PAID', [1360, 142], [77, 5]),
  inv('106', 'Lê Hoàng Nam', 'DRAFT', [1806, 138], [95, 6]),
  inv('107', 'Ngô Bảo Châu', 'DRAFT', [1220, 124], [64, 3]),
  inv('108', 'Đinh Thị Lụa', 'DRAFT', [1675, 130], [83, 5]),
  inv('109', 'Kiều Minh Khôi', 'DRAFT', [1433, 132], [79, 5]),
  inv('111', 'Hồ Thị Yến', 'DRAFT', [1098, 118], [58, 5]),
  inv('112', 'Vũ Ngọc Mai', 'DISPUTED', [1618, 112], [136, 12]),
  inv('201', 'Nguyễn Hữu Phước', 'DRAFT', [2960, 160], [112, 5]),
  inv('202', 'Tạ Thu Hà', 'SENT', [1300, 140], [74, 7]),
  inv('203', 'Lương Văn Sơn', 'DRAFT', [1544, 126], [69, 5]),
  inv('204', 'Trần Minh Anh', 'DISPUTED', [1102, 182], [61, 6], { sentAt: '2026-12-02T10:30:00+07:00' }),
  inv('205', 'Phùng Gia Hân', 'DRAFT', [987, 128], [52, 3]),
  inv('206', 'Tôn Nữ Diệp', 'DRAFT', [1721, 134], [90, 5]),
  inv('207', 'Phan Thu Trang', 'DISPUTED', [1390, 132], [66, 5]),
  inv('208', 'Âu Minh Quân', 'DRAFT', [1265, 126], [71, 5]),
  inv('209', 'Châu Ngọc Lan', 'DRAFT', [1489, 134], [86, 5]),
  inv('210', 'Quách Văn Hải', 'DRAFT', [2105, 140], [98, 5]),
  inv('211', 'Mạc Thị Thanh', 'SENT', [1356, 136], [73, 6]),
  inv('212', 'Dương Khánh Linh', 'DRAFT', [1147, 122], [60, 5]),
  inv('301', 'Võ Thành Đạt', 'DRAFT', [1630, 132], [77, 4]),
  inv('302', 'Thái Mỹ Duyên', 'DRAFT', [1402, 128], [68, 5]),
  inv('305', 'Phạm Quốc Huy', 'DRAFT', [2620, 368], [101, 4]),
  inv('306', 'Lâm Gia Bảo', 'DRAFT', [1940, 150], [84, 5]),
  inv('110', 'Trịnh Văn An', 'SENT', [1785, 162], [93, 6], K10),
  inv('308', 'Lý Văn Tâm', 'SENT', [1610, 130], [80, 6], K10),
];
