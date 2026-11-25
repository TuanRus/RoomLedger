import type { components } from '../../../../generated/api-types';
import { ReviewState } from '../../../../core/data/models';

type Schemas = components['schemas'];

export type RoomStatus = Schemas['RoomStatus'];
export type RoomDisplayStatus = Schemas['RoomDisplayStatus'];

/**
 * Contract column of LL-03. The API only has TenancyStatus / TenancyVersionStatus; this is the
 * landlord-facing summary of both (see API gaps in the report).
 */
export type ContractState = 'CONFIRMED' | 'REJECTED' | 'INVITED' | 'AWAITING_TENANT' | 'DRAFT' | 'NONE';

/** The latest approved reading of one meter. date is YYYY-MM-DD. */
export interface MeterSnapshot {
  value: number;
  date: string;
}

export interface PendingInvite {
  email: string;
  /** YYYY-MM-DD */
  startDate: string;
  /** ISO date-time with +07:00 offset. */
  expiresAt: string;
}

/**
 * One row of the landlord room list (LL-03).
 * Based on RoomResponse (roomId, code, monthlyRent, status, displayStatus, tenantName); every other
 * field is not in the contract yet: floor, deposit, tenancyStart, contract, reading, electricity, water, invite.
 */
export interface RoomRow {
  roomId: string;
  code: string;
  floor: number;
  monthlyRent: number;
  deposit: number;
  status: RoomStatus;
  displayStatus: RoomDisplayStatus;
  tenantName: string | null;
  /** YYYY-MM-DD */
  tenancyStart: string | null;
  contract: ContractState;
  /** Meter state of the current period (electricity first, as on LL-04). null for an empty room. */
  reading: ReviewState | null;
  electricity: MeterSnapshot;
  water: MeterSnapshot;
  invite: PendingInvite | null;
}

export type RoomFilter = 'all' | 'occupied' | 'vacant' | 'awaiting';
