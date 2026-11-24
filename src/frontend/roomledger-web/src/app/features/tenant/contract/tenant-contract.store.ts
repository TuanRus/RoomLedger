import { Service, signal } from '@angular/core';

/**
 * The tenant's contract (TN-10). Sample data until the API is connected: GET /tenancies/{id} gives the
 * version with its terms and files; POST /tenancies/{id}/versions/{v}/confirm sends CONFIRM or REJECT + reason.
 */
export interface TenantContract {
  roomCode: string;
  propertyName: string;
  scanUrl: string;
  pages: number;
  monthlyRent: number;
  deposit: number;
  startDate: string;
  endDate: string;
  electricityRate: number;
  waterRate: number;
}

export type ContractDecision =
  | { status: 'AWAITING_CONFIRM' }
  | { status: 'CONFIRMED'; at: string }
  | { status: 'REJECTED'; at: string; reason: string };

export const REASON_MIN = 10;

/** Today as dd/MM/yyyy, for the sample confirmation stamp. */
function today(): string {
  const d = new Date();
  return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
}

@Service()
export class TenantContractStore {
  readonly contract = signal<TenantContract>({
    roomCode: 'P.204',
    propertyName: 'Nhà trọ Cô Lan',
    scanUrl: 'sample/contract-p1.jpg',
    pages: 2,
    monthlyRent: 2_500_000,
    deposit: 3_000_000,
    startDate: '01/03/2026',
    endDate: '28/02/2027',
    electricityRate: 3_500,
    waterRate: 18_000,
  });

  readonly decision = signal<ContractDecision>({ status: 'AWAITING_CONFIRM' });

  confirm(): void {
    this.decision.set({ status: 'CONFIRMED', at: today() });
  }

  /** Returns false when the reason is too short (BR: at least 10 characters). */
  reject(reason: string): boolean {
    const text = reason.trim();
    if (text.length < REASON_MIN) return false;
    this.decision.set({ status: 'REJECTED', at: today(), reason: text });
    return true;
  }

  /** Sample switch for ?sample=confirmed: the contract as confirmed on move-in day. */
  showConfirmed(): void {
    this.decision.set({ status: 'CONFIRMED', at: '01/03/2026' });
  }

  reset(): void {
    this.decision.set({ status: 'AWAITING_CONFIRM' });
  }
}
