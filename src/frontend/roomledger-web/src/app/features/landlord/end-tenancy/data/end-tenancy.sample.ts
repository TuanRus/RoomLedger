import { MoveTarget } from './end-tenancy.models';

/**
 * Sample for LL-10: P.204 (Trần Minh Anh) moves to P.302 on 15/12/2026, 08:10.
 * Rates match the sample ledger (điện 3.500 đ/kWh, nước 18.000 đ/m³).
 * The final electricity reading 1.396 was approved on 15/12; water (from 65) is still missing.
 */
export const END_DATE = '2026-12-15';

export const RATES = { ELECTRICITY: 3_500, WATER: 18_000 } as const;

/** Value the demo uses when the landlord "records" a missing final reading. */
export const DEMO_FINAL = { ELECTRICITY: 112, WATER: 6 } as const;

export const P204_FINAL_ELECTRICITY = 1396;

/** Starting readings of the empty rooms on 15/12/2026 (P.302 from LL-10, P.209 from LL-03). */
export const MOVE_TARGETS: MoveTarget[] = [
  { roomId: 'r-302', code: 'P.302', monthlyRent: 2_800_000, startElectricity: 2115, startWater: 95 },
  { roomId: 'r-209', code: 'P.209', monthlyRent: 2_600_000, startElectricity: 1508, startWater: 88 },
  { roomId: 'r-303', code: 'P.303', monthlyRent: 2_800_000, startElectricity: 984, startWater: 41 },
];
