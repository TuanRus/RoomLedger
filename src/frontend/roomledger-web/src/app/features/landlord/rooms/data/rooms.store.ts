import { Service, computed, signal } from '@angular/core';

import { PendingInvite, RoomFilter, RoomRow } from './rooms.models';
import { ROOMS_NOW, SAMPLE_ROOMS } from './rooms.sample';

/** An invite link stays valid this long (CreateInvite, PRODUCT.md). */
export const INVITE_HOURS = 72;

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function isValidEmail(value: string): boolean {
  return EMAIL.test(value.trim());
}

export function isOccupied(room: RoomRow): boolean {
  return room.status === 'OCCUPIED';
}

export function matchesFilter(room: RoomRow, filter: RoomFilter): boolean {
  switch (filter) {
    case 'occupied':
      return room.status === 'OCCUPIED';
    case 'vacant':
      return room.status === 'VACANT';
    case 'awaiting':
      return room.displayStatus === 'AWAITING_CONFIRM';
    default:
      return true;
  }
}

/** Case- and accent-insensitive match on room code ("204", "p.204") or tenant name ("minh anh"). */
export function matchesQuery(room: RoomRow, query: string): boolean {
  const q = fold(query.trim());
  if (!q) return true;
  return fold(room.code).includes(q) || fold(room.tenantName ?? '').includes(q);
}

function fold(text: string): string {
  return text.normalize('NFD').replace(/\p{M}/gu, '').replace(/đ/gi, 'd').toLowerCase();
}

/** Adds hours to an ISO time and writes the result in Vietnam time (+07:00), independent of the browser zone. */
export function addHoursVn(iso: string, hours: number): string {
  const vn = new Date(Date.parse(iso) + (hours + 7) * 3_600_000);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${vn.getUTCFullYear()}-${p(vn.getUTCMonth() + 1)}-${p(vn.getUTCDate())}T${p(vn.getUTCHours())}:${p(vn.getUTCMinutes())}:00+07:00`;
}

/**
 * Rooms of the landlord's property (LL-03), backed by sample data until the API is connected:
 * GET /properties/{id}/rooms, POST /tenancies/{id}/invites.
 */
@Service()
export class RoomsStore {
  readonly period = '2026-11';
  private readonly data = signal<RoomRow[]>(structuredClone(SAMPLE_ROOMS));
  readonly rooms = this.data.asReadonly();

  readonly counts = computed(() => {
    const rooms = this.data();
    const count = (f: RoomFilter) => rooms.filter((r) => matchesFilter(r, f)).length;
    return { all: rooms.length, occupied: count('occupied'), vacant: count('vacant'), awaiting: count('awaiting') };
  });

  byId(roomId: string): RoomRow | null {
    return this.data().find((r) => r.roomId === roomId) ?? null;
  }

  /** Sends an invite for an empty room; the link expires INVITE_HOURS after now. */
  invite(roomId: string, email: string, startDate: string, now = ROOMS_NOW): PendingInvite {
    const invite: PendingInvite = { email: email.trim(), startDate, expiresAt: addHoursVn(now, INVITE_HOURS) };
    this.patch(roomId, { displayStatus: 'AWAITING_CONFIRM', contract: 'INVITED', invite });
    return invite;
  }

  /**
   * Applies a finished end of tenancy: the room becomes empty; on a move the tenant takes the
   * target room with a new contract that waits for their confirmation.
   */
  endTenancy(roomId: string, moveToRoomId: string | null, date: string): void {
    const from = this.byId(roomId);
    if (!from) return;
    if (moveToRoomId) {
      this.patch(moveToRoomId, {
        status: 'OCCUPIED', displayStatus: 'ACTIVE', tenantName: from.tenantName, tenancyStart: date,
        contract: 'AWAITING_TENANT', reading: null, invite: null,
      });
    }
    this.patch(roomId, { status: 'VACANT', displayStatus: 'VACANT', tenantName: null, tenancyStart: null, contract: 'NONE', reading: null, invite: null });
  }

  private patch(roomId: string, changes: Partial<RoomRow>): void {
    this.data.update((rooms) => rooms.map((r) => (r.roomId === roomId ? { ...r, ...changes } : r)));
  }
}
