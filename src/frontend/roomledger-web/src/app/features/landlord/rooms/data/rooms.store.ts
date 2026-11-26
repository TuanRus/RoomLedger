import { Service, computed, inject, signal } from '@angular/core';
import { Observable, map, of, switchMap, tap } from 'rxjs';

import type { components } from '../../../../generated/api-types';
import { injectApi } from '../../../../core/api/api-client';
import { LandlordPropertyStore, RoomResponse } from '../../../../core/data/landlord-property.store';
import { MeterReviewStore, stateOf } from '../../../../core/data/meter-review.store';
import { ReviewRow } from '../../../../core/data/models';
import { ContractState, MeterSnapshot, PendingInvite, RoomFilter, RoomRow } from './rooms.models';
import { ROOMS_NOW, SAMPLE_ROOMS } from './rooms.sample';

type RoomDetailResponse = components['schemas']['RoomDetailResponse'];
type TenancyDetailResponse = components['schemas']['TenancyDetailResponse'];
type InviteResponse = components['schemas']['InviteResponse'];

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

/** What the tenancy of one room adds to its row (API mode): loaded when the room is selected. */
export interface RoomDetail {
  status: 'loading' | 'ready' | 'error';
  error: string | null;
  deposit: number | null;
  /** YYYY-MM-DD */
  startDate: string | null;
  contract: ContractState | null;
  files: number | null;
}

const EMPTY_DETAIL = { deposit: null, startDate: null, contract: null, files: null } as const;

const asNumber = (v: unknown): number | null => (typeof v === 'number' ? v : typeof v === 'string' && /^\d+$/.test(v) ? Number(v) : null);
const asDate = (v: unknown): string | null => (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}/.test(v) ? v.slice(0, 10) : null);

/** Contract column from the room's display status alone, before its tenancy is loaded. */
export function contractFromDisplay(status: RoomResponse['displayStatus']): ContractState {
  switch (status) {
    case 'AWAITING_CONFIRM':
      return 'INVITED';
    case 'ACTIVE':
    case 'MOVING_OUT':
      return 'CONFIRMED';
    default:
      return 'NONE';
  }
}

/** Deposit, start date, contract state and file count from the tenancy (versions come newest first). */
export function toRoomDetail(t: TenancyDetailResponse | null, display: RoomResponse['displayStatus']): RoomDetail {
  const versions = t?.versions ?? [];
  const latest = versions[0];
  const shown = versions.find((v) => v.status === 'LOCKED') ?? latest;
  let contract: ContractState | null = null;
  if (latest) {
    if (latest.status === 'LOCKED') contract = 'CONFIRMED';
    else if (latest.status === 'REJECTED') contract = 'REJECTED';
    else if (latest.status === 'AWAITING_CONFIRM' && t?.tenantName) contract = 'AWAITING_TENANT';
    else if ((latest.status === 'UPLOADED' || latest.status === 'TERMS_REVIEWED') && t?.status === 'DRAFT') contract = 'DRAFT';
    else contract = contractFromDisplay(display);
  }
  return {
    status: 'ready',
    error: null,
    deposit: asNumber(shown?.terms?.['deposit']?.value),
    startDate: asDate(shown?.terms?.['startDate']?.value),
    contract,
    files: latest ? (latest.files?.length ?? 0) : null,
  };
}

/** Latest approved reading of a meter: the confirmed value, else the previous period's value (no date). */
export function meterSnapshot(row: ReviewRow | undefined): MeterSnapshot | null {
  const r = row?.reading;
  if (!r) return null;
  if (r.status === 'CONFIRMED' && r.value != null) return { value: r.value, date: r.confirmedAt ? r.confirmedAt.slice(0, 10) : null };
  return r.previousValue != null ? { value: r.previousValue, date: null } : null;
}

/** One RoomResponse plus what the meter review and the tenancy know about it. Fields the API lacks stay null. */
export function toRoomRow(
  r: RoomResponse,
  electricity: ReviewRow | undefined,
  water: ReviewRow | undefined,
  detail: RoomDetail | undefined,
  invite: PendingInvite | undefined,
): RoomRow {
  const display = r.displayStatus ?? 'VACANT';
  return {
    roomId: r.roomId ?? '',
    code: r.code ?? '',
    floor: null,
    monthlyRent: r.monthlyRent ?? 0,
    deposit: detail?.deposit ?? null,
    status: r.status ?? 'VACANT',
    displayStatus: display,
    tenantName: r.tenantName ?? null,
    tenancyStart: detail?.startDate ?? null,
    contract: detail?.contract ?? contractFromDisplay(display),
    reading: r.status === 'OCCUPIED' && electricity ? stateOf(electricity) : null,
    electricity: meterSnapshot(electricity),
    water: meterSnapshot(water),
    invite: invite ?? null,
    contractFiles: detail?.files ?? null,
  };
}

/** Rooms in room-code order ("P.101" before "P.201"). */
const byCode = (a: RoomRow, b: RoomRow) => a.code.localeCompare(b.code, 'vi', { numeric: true });

/**
 * Rooms of the landlord's property (LL-03). Sample data by default; with environment.dataSource 'api' the list
 * comes from GET /properties/{id}/rooms, the meter state from the meter review, and deposit / start date / contract
 * state from the tenancy of the selected room (GET /rooms/{id}, GET /tenancies/{id}).
 * An invite is POST /rooms/{id}/tenancies then POST /tenancies/{id}/invites.
 */
@Service()
export class RoomsStore {
  private readonly api = injectApi();
  private readonly property = inject(LandlordPropertyStore);
  private readonly review = inject(MeterReviewStore);

  /** True with sample data: the demo-only parts of the screen may show. */
  readonly sample = !this.api;
  readonly period = this.property.period.asReadonly();
  /** Loading state of the room list (the property store loads it). */
  readonly status = this.property.status;
  readonly error = this.property.error;

  private readonly data = signal<RoomRow[]>(this.api ? [] : structuredClone(SAMPLE_ROOMS));
  private readonly details = signal<Record<string, RoomDetail>>({});
  private readonly invites = signal<Record<string, PendingInvite>>({});

  readonly rooms = computed<RoomRow[]>(() => {
    if (!this.api) return this.data();
    const details = this.details();
    const invites = this.invites();
    const elec = new Map(this.review.rowsOf('ELECTRICITY').map((r) => [r.roomId, r]));
    const water = new Map(this.review.rowsOf('WATER').map((r) => [r.roomId, r]));
    return this.property
      .rooms()
      .map((r) => toRoomRow(r, elec.get(r.roomId ?? ''), water.get(r.roomId ?? ''), details[r.roomId ?? ''], invites[r.roomId ?? '']))
      .sort(byCode);
  });

  readonly counts = computed(() => {
    const rooms = this.rooms();
    const count = (f: RoomFilter) => rooms.filter((r) => matchesFilter(r, f)).length;
    return { all: rooms.length, occupied: count('occupied'), vacant: count('vacant'), awaiting: count('awaiting') };
  });

  byId(roomId: string): RoomRow | null {
    return this.rooms().find((r) => r.roomId === roomId) ?? null;
  }

  /** The tenancy detail of a room (loading, error or ready). Null with sample data or before the first load. */
  detailOf(roomId: string | null): RoomDetail | null {
    return roomId ? (this.details()[roomId] ?? null) : null;
  }

  /** Reloads the room list and the meter review behind it. */
  reload(): void {
    this.property.load();
    this.review.load();
  }

  /** Loads the tenancy of a room (deposit, start date, contract state, scan files) once per selection. */
  loadDetail(roomId: string, force = false): void {
    const api = this.api;
    if (!api) return;
    const known = this.details()[roomId];
    if (!force && known && known.status !== 'error') return;
    const display = this.property.roomById().get(roomId)?.displayStatus;
    this.setDetail(roomId, { status: 'loading', error: null, ...EMPTY_DETAIL });
    api
      .get<RoomDetailResponse>(`/rooms/${roomId}`)
      .pipe(
        switchMap((room) => {
          const id = room.currentTenancy?.tenancyId;
          return id ? api.get<TenancyDetailResponse>(`/tenancies/${id}`) : of(null);
        }),
      )
      .subscribe({
        next: (t) => this.setDetail(roomId, toRoomDetail(t, display)),
        error: (err: unknown) =>
          this.setDetail(roomId, { status: 'error', error: err instanceof Error ? err.message : 'Không tải được hợp đồng của phòng.', ...EMPTY_DETAIL }),
      });
  }

  /** Sample data: sends an invite for an empty room; the link expires INVITE_HOURS after now. */
  invite(roomId: string, email: string, startDate: string, now = ROOMS_NOW): PendingInvite {
    const invite: PendingInvite = { email: email.trim(), startDate, expiresAt: addHoursVn(now, INVITE_HOURS) };
    this.patch(roomId, { displayStatus: 'AWAITING_CONFIRM', contract: 'INVITED', invite });
    return invite;
  }

  /**
   * API: opens (or finds) the draft tenancy of an empty room, then sends the invite email.
   * Errors carry the server's Vietnamese message, e.g. the terms have to be saved first.
   */
  inviteByApi(roomId: string, email: string): Observable<PendingInvite> {
    const api = this.api;
    if (!api) throw new Error('inviteByApi needs the API');
    return api.post<TenancyDetailResponse>(`/rooms/${roomId}/tenancies`).pipe(
      switchMap((t) => api.post<InviteResponse>(`/tenancies/${t.tenancyId}/invites`, { email: email.trim() })),
      map((i): PendingInvite => ({ email: i.email ?? email.trim(), startDate: null, expiresAt: i.expiresAt ?? '' })),
      tap({
        next: (invite) => {
          this.invites.update((all) => ({ ...all, [roomId]: invite }));
          this.details.update((all) => {
            const rest = { ...all };
            delete rest[roomId];
            return rest;
          });
          this.property.reloadRooms();
        },
        // The tenancy may have been created before the invite failed: show the room as the server has it now.
        error: () => this.property.reloadRooms(),
      }),
    );
  }

  /**
   * Applies a finished end of tenancy: the room becomes empty; on a move the tenant takes the
   * target room with a new contract that waits for their confirmation. Sample data only (no endpoint).
   */
  endTenancy(roomId: string, moveToRoomId: string | null, date: string): void {
    if (this.api) return;
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

  private setDetail(roomId: string, detail: RoomDetail): void {
    this.details.update((all) => ({ ...all, [roomId]: detail }));
  }

  private patch(roomId: string, changes: Partial<RoomRow>): void {
    this.data.update((rooms) => rooms.map((r) => (r.roomId === roomId ? { ...r, ...changes } : r)));
  }
}
