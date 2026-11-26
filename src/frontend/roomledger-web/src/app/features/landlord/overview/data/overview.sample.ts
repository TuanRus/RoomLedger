/**
 * Landlord dashboard (LL-02) on 02/12/2026, 09:00. Not one API response: with the API it is built from the other
 * landlord stores (me, property, invoices, contracts, disputes, capture). Fields the API has no value for are null.
 */
export interface OverviewData {
  landlordName: string;
  /** YYYY-MM */
  period: string;
  /** Not in the API: null there. */
  captureWindow: string | null;
  /** Not in the API: null there. */
  paymentDue: string | null;
  capture: { recorded: number; total: number; deadline: string | null };
  /** daysLate needs a due date, which the API does not have: null there. */
  overdue: { roomCode: string; period: string; amount: number; daysLate: number | null }[];
  summary: {
    rooms: { total: number; occupied: number; vacant: number };
    /** null when the previous period has no sent invoice. */
    lastPeriod: { period: string; paid: number; total: number; overdue: number } | null;
    contracts: { needAction: number; note: string };
    disputes: { open: number; note: string | null };
  };
}

export const OVERVIEW_SAMPLE: OverviewData = {
  landlordName: 'cô Lan',
  period: '2026-11',
  captureWindow: '01–03/12/2026',
  paymentDue: '10/12/2026',
  capture: { recorded: 27, total: 32, deadline: '03/12' },
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
