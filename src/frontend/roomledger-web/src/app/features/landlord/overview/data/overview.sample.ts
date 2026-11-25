import { ReviewState } from '../../../../core/data/models';

/**
 * Landlord dashboard (LL-02) on 02/12/2026, 09:00. Not one API response yet: today it would take
 * the meter-readings, rooms, invoices, tenancies and disputes lists (see report, API gaps).
 */
export interface OverviewData {
  landlordName: string;
  /** YYYY-MM */
  period: string;
  captureWindow: string;
  paymentDue: string;
  capture: { recorded: number; total: number; deadline: string };
  attention: {
    total: number;
    pending: number;
    abnormal: number;
    unreadable: number;
    waterApproved: number;
    rooms: { roomCode: string; kind: 'điện' | 'nước'; state: Extract<ReviewState, 'abnormal' | 'unreadable'>; change: number | null }[];
  };
  unrecorded: { roomCode: string; tenantName: string; meters: string }[];
  overdue: { roomCode: string; period: string; amount: number; daysLate: number }[];
  summary: {
    rooms: { total: number; occupied: number; vacant: number };
    lastPeriod: { period: string; paid: number; total: number; overdue: number };
    contracts: { needAction: number; note: string };
    disputes: { open: number; note: string };
  };
}

export const OVERVIEW_SAMPLE: OverviewData = {
  landlordName: 'cô Lan',
  period: '2026-11',
  captureWindow: '01–03/12/2026',
  paymentDue: '10/12/2026',
  capture: { recorded: 27, total: 32, deadline: '03/12' },
  attention: {
    total: 24,
    pending: 19,
    abnormal: 3,
    unreadable: 2,
    waterApproved: 27,
    rooms: [
      { roomCode: 'P.305', kind: 'điện', state: 'abnormal', change: 0.64 },
      { roomCode: 'P.114', kind: 'điện', state: 'abnormal', change: 0.48 },
      { roomCode: 'P.306', kind: 'điện', state: 'unreadable', change: null },
    ],
  },
  unrecorded: [
    { roomCode: 'P.307', tenantName: 'Đỗ Thị Hà', meters: 'cả hai' },
    { roomCode: 'P.308', tenantName: 'Lý Văn Tâm', meters: 'cả hai' },
    { roomCode: 'P.113', tenantName: 'Vương Thu Nga', meters: 'cả hai' },
    { roomCode: 'P.214', tenantName: 'Hồ Minh Khoa', meters: 'cả hai' },
    { roomCode: 'P.110', tenantName: 'Trịnh Văn An', meters: 'cả hai' },
  ],
  overdue: [
    { roomCode: 'P.110', period: '2026-10', amount: 3_175_000, daysLate: 22 },
    { roomCode: 'P.215', period: '2026-10', amount: 3_310_000, daysLate: 22 },
  ],
  summary: {
    rooms: { total: 36, occupied: 32, vacant: 4 },
    lastPeriod: { period: '2026-10', paid: 30, total: 32, overdue: 2 },
    contracts: { needAction: 1, note: 'P.305 báo “Không đúng”' },
    disputes: { open: 0, note: 'kỳ 10 đã trả lời hết' },
  },
};
