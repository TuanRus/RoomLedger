import { Service, computed, inject, signal } from '@angular/core';

import { injectApi } from '../../../../core/api/api-client';
import { MeterKind } from '../../../../core/data/models';
import { RoomRow } from '../../rooms/data/rooms.models';
import { RoomsStore } from '../../rooms/data/rooms.store';
import { EndKind, EndTenancyDraft, EndTenancyHeader, FinalInvoice, FinalReading, InvoiceLine, MoveTarget, StepId } from './end-tenancy.models';
import { DEMO_FINAL, END_DATE, MOVE_TARGETS, P204_FINAL_ELECTRICITY, RATES } from './end-tenancy.sample';

const KIND_LABEL: Record<MeterKind, string> = { ELECTRICITY: 'điện', WATER: 'nước' };

function line(r: FinalReading, rate: number): InvoiceLine | null {
  if (r.state !== 'approved' || r.value == null) return null;
  const usage = r.value - r.previous;
  return { usage, rate, amount: usage * rate };
}

/** Final invoice lines. Rent is exactly what the landlord typed; nothing is split by days. */
export function finalInvoice(d: EndTenancyDraft): FinalInvoice {
  const electricity = line(d.electricity, d.rates.ELECTRICITY);
  const water = line(d.water, d.rates.WATER);
  const total = d.lastRent != null && electricity && water ? d.lastRent + electricity.amount + water.amount + d.unpaidPrevious : null;
  return { rent: d.lastRent, electricity, water, unpaidPrevious: d.unpaidPrevious, total };
}

/** Why the tenancy cannot end yet, in the order the landlord should fix them. Empty means it can. */
export function blockers(d: EndTenancyDraft): string[] {
  const out: string[] = [];
  if (!d.kind) out.push('chưa chọn hình thức');
  if (d.kind === 'MOVE' && !d.targetRoomId) out.push('chưa chọn phòng chuyển đến');
  for (const r of [d.electricity, d.water]) {
    if (r.state === 'missing') out.push(`còn thiếu chỉ số ${KIND_LABEL[r.kind]} cuối`);
    else if (r.state === 'pending') out.push(`chỉ số ${KIND_LABEL[r.kind]} cuối chưa duyệt`);
  }
  if (d.lastRent == null) out.push('chưa nhập tiền phòng tháng cuối');
  return out;
}

export function stepsDone(d: EndTenancyDraft): Record<StepId, boolean> {
  const s1 = d.kind === 'LEAVE' || (d.kind === 'MOVE' && !!d.targetRoomId);
  const s2 = d.electricity.state === 'approved' && d.water.state === 'approved';
  const s3 = finalInvoice(d).total != null;
  return { 1: s1, 2: s2, 3: s3, 4: d.finishedAt != null };
}

/** The first step that still needs work. */
export function currentStep(d: EndTenancyDraft): StepId {
  const done = stepsDone(d);
  return ([1, 2, 3, 4] as StepId[]).find((s) => !done[s]) ?? 4;
}

function reading(kind: MeterKind, previous: number): FinalReading {
  return { kind, previous, value: null, state: 'missing', approvedOn: null, photoUrl: null };
}

/** A fresh draft for a rented room. P.204 starts where LL-10 shows it. */
export function initialDraft(room: RoomRow): EndTenancyDraft {
  const draft: EndTenancyDraft = {
    roomId: room.roomId,
    roomCode: room.code,
    tenantName: room.tenantName ?? '',
    tenancyStart: room.tenancyStart ?? '',
    endDate: END_DATE,
    monthlyRent: room.monthlyRent,
    kind: null,
    targetRoomId: null,
    electricity: reading('ELECTRICITY', room.electricity?.value ?? 0),
    water: reading('WATER', room.water?.value ?? 0),
    lastRent: null,
    unpaidPrevious: 0,
    rates: { ...RATES },
    finishedAt: null,
  };
  if (room.roomId === 'r-204') {
    draft.kind = 'MOVE';
    draft.targetRoomId = 'r-302';
    draft.lastRent = 1_400_000;
    draft.electricity = { ...draft.electricity, value: P204_FINAL_ELECTRICITY, state: 'approved', approvedOn: END_DATE, photoUrl: 'sample/elec-204.jpg' };
  }
  return draft;
}

/**
 * End of tenancy and room moves (LL-10), sample-backed: the API has no end-tenancy or room-move endpoint
 * (API-GAPS.md). With environment.dataSource 'api' the screen only shows the room and its tenant (header()),
 * and nothing can be finished.
 * Drafts live in memory per room, so "Lưu nháp" and leaving the page keep the work.
 */
@Service()
export class EndTenancyStore {
  private readonly api = injectApi();
  private readonly rooms = inject(RoomsStore);
  private readonly drafts = signal<Record<string, EndTenancyDraft>>({});

  /** True with sample data: the end-of-tenancy steps work. */
  readonly sample = !this.api;

  /** Empty rooms (not waiting on an invite) the tenant can move into. Sample data only. */
  readonly targets = computed<MoveTarget[]>(() =>
    this.api ? [] : MOVE_TARGETS.filter((t) => this.rooms.rooms().some((r) => r.roomId === t.roomId && r.displayStatus === 'VACANT')),
  );

  /** API: the room and its tenant for the page header (tenancy start loads with the room detail), or null when nobody lives there. */
  header(roomId: string): EndTenancyHeader | null {
    const room = this.rooms.byId(roomId);
    if (!room || room.status !== 'OCCUPIED') return null;
    return { roomId, roomCode: room.code, tenantName: room.tenantName ?? '', tenancyStart: room.tenancyStart, monthlyRent: room.monthlyRent };
  }

  /** API: loads the room's tenancy (start date) for the header. */
  loadHeader(roomId: string): void {
    this.rooms.loadDetail(roomId);
  }

  /** The saved draft, a fresh one for a rented room, or null when the room has nobody to move out (always null with the API). */
  draft(roomId: string): EndTenancyDraft | null {
    if (this.api) return null;
    const saved = this.drafts()[roomId];
    if (saved) return saved;
    const room = this.rooms.byId(roomId);
    return room && room.status === 'OCCUPIED' ? initialDraft(room) : null;
  }

  target(roomId: string | null): MoveTarget | null {
    return this.api ? null : (MOVE_TARGETS.find((t) => t.roomId === roomId) ?? null);
  }

  setKind(roomId: string, kind: EndKind): void {
    this.update(roomId, (d) => ({ ...d, kind, targetRoomId: kind === 'MOVE' ? (d.targetRoomId ?? this.targets()[0]?.roomId ?? null) : d.targetRoomId }));
  }

  setTarget(roomId: string, targetRoomId: string): void {
    this.update(roomId, (d) => ({ ...d, targetRoomId }));
  }

  setRent(roomId: string, lastRent: number | null): void {
    this.update(roomId, (d) => ({ ...d, lastRent }));
  }

  /** A photo arrived (upload from the computer); AI's value waits for the landlord's approval. */
  receivePhoto(roomId: string, kind: MeterKind): void {
    this.updateReading(roomId, kind, (r) => ({ ...r, value: r.previous + DEMO_FINAL[kind], state: 'pending' }));
  }

  approve(roomId: string, kind: MeterKind): void {
    this.updateReading(roomId, kind, (r) => (r.value == null ? r : { ...r, state: 'approved', approvedOn: END_DATE }));
  }

  /** Demo only: pretend the reading was photographed on the phone and approved. */
  recordDemo(roomId: string, kind: MeterKind): void {
    this.updateReading(roomId, kind, (r) => ({ ...r, value: r.previous + DEMO_FINAL[kind], state: 'approved', approvedOn: END_DATE }));
  }

  /** Ends the tenancy when nothing blocks it. Returns false otherwise. */
  finish(roomId: string, now = '2026-12-15T08:10:00+07:00'): boolean {
    const d = this.draft(roomId);
    if (this.api || !d || d.finishedAt || blockers(d).length) return false;
    this.update(roomId, (x) => ({ ...x, finishedAt: now }));
    this.rooms.endTenancy(roomId, d.kind === 'MOVE' ? d.targetRoomId : null, d.endDate);
    return true;
  }

  private updateReading(roomId: string, kind: MeterKind, fn: (r: FinalReading) => FinalReading): void {
    const key = kind === 'ELECTRICITY' ? 'electricity' : 'water';
    this.update(roomId, (d) => ({ ...d, [key]: fn(d[key]) }));
  }

  private update(roomId: string, fn: (d: EndTenancyDraft) => EndTenancyDraft): void {
    const current = this.draft(roomId);
    if (!current || current.finishedAt) return;
    this.drafts.update((all) => ({ ...all, [roomId]: fn(current) }));
  }
}
