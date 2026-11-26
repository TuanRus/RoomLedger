import { MeterKind } from '../../../../core/data/models';

export type EndKind = 'LEAVE' | 'MOVE';

/** missing: no photo yet · pending: photo in, AI read a value, landlord has not approved · approved. */
export type FinalReadingState = 'missing' | 'pending' | 'approved';

export interface FinalReading {
  kind: MeterKind;
  /** Last approved reading (the start of the final period). */
  previous: number;
  value: number | null;
  state: FinalReadingState;
  /** YYYY-MM-DD */
  approvedOn: string | null;
  photoUrl: string | null;
}

/** An empty room the tenant can move into, with its starting readings on the move date. */
export interface MoveTarget {
  roomId: string;
  code: string;
  monthlyRent: number;
  startElectricity: number;
  startWater: number;
}

/**
 * Draft end of tenancy for one room (LL-10). Not in the API contract yet (see report):
 * there is no end-tenancy / move endpoint, no final reading by date and no final invoice request.
 */
export interface EndTenancyDraft {
  roomId: string;
  roomCode: string;
  tenantName: string;
  /** YYYY-MM-DD */
  tenancyStart: string;
  /** YYYY-MM-DD, the leaving or move date. */
  endDate: string;
  monthlyRent: number;
  kind: EndKind | null;
  targetRoomId: string | null;
  electricity: FinalReading;
  water: FinalReading;
  /** Last month's rent typed by the landlord. Never split by days automatically. */
  lastRent: number | null;
  /** Unpaid earlier invoices carried into the final invoice. */
  unpaidPrevious: number;
  rates: Record<MeterKind, number>;
  finishedAt: string | null;
}

export interface InvoiceLine {
  usage: number;
  rate: number;
  amount: number;
}

export interface FinalInvoice {
  rent: number | null;
  electricity: InvoiceLine | null;
  water: InvoiceLine | null;
  unpaidPrevious: number;
  /** null until every line is known. */
  total: number | null;
}

/** What the API knows about the room being ended: the room, its tenant and rent. Everything else of the draft has no endpoint. */
export interface EndTenancyHeader {
  roomId: string;
  roomCode: string;
  tenantName: string;
  /** YYYY-MM-DD, null until the tenancy is loaded. */
  tenancyStart: string | null;
  monthlyRent: number;
}

export type StepId = 1 | 2 | 3 | 4;
