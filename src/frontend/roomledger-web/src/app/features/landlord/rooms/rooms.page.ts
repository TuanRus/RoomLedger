import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { FormField, email, form, required } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';

import { Viewport } from '../../../core/layout/viewport';
import { NumPipe, PeriodPipe, StampPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { StatusChip } from '../../../shared/ui/status-chip';
import { ContractState, PendingInvite, RoomFilter, RoomRow } from './data/rooms.models';
import { INVITE_HOURS, RoomsStore, isOccupied, matchesFilter, matchesQuery } from './data/rooms.store';

type Tone = 'ok' | 'warn' | 'err' | 'info' | 'neu';

const FILTERS: { id: RoomFilter; label: string }[] = [
  { id: 'all', label: 'Tất cả' },
  { id: 'occupied', label: 'Đang thuê' },
  { id: 'vacant', label: 'Trống' },
  { id: 'awaiting', label: 'Chờ nhận lời mời' },
];

const CONTRACT: Record<ContractState, [Tone, string, string]> = {
  CONFIRMED: ['ok', 'i-check', 'Đã xác nhận'],
  REJECTED: ['err', 'i-flag', 'Báo không đúng'],
  INVITED: ['warn', 'i-mail', 'Đã gửi lời mời'],
  AWAITING_TENANT: ['warn', 'i-clock', 'Chờ người thuê xác nhận'],
  DRAFT: ['neu', 'i-doc', 'Nháp'],
  NONE: ['neu', 'i-doc', 'Chưa có'],
};

const DEFAULT_START = '2026-12-01';

/** "2026-11-30" -> "30/11/2026". */
function day(iso: string | null): string {
  return iso ? iso.split('-').reverse().join('/') : '—';
}

/** LL-03 (computer) and its phone layout: rooms, tenants, contract and reading state, invite a tenant to an empty room. */
@Component({
  selector: 'app-rooms-page',
  imports: [NgTemplateOutlet, RouterLink, FormField, Icon, StatusChip, NumPipe, PeriodPipe, StampPipe, VndPipe],
  templateUrl: './rooms.page.html',
  styleUrl: './rooms.page.scss',
})
export class RoomsPage {
  protected readonly store = inject(RoomsStore);
  protected readonly viewport = inject(Viewport);

  protected readonly filters = FILTERS;
  protected readonly contract = CONTRACT;
  protected readonly inviteHours = INVITE_HOURS;
  protected readonly isOccupied = isOccupied;
  protected readonly day = day;

  protected readonly filter = signal<RoomFilter>('all');
  protected readonly query = signal('');

  protected readonly visibleRooms = computed(() => {
    const f = this.filter();
    const q = this.query();
    return this.store.rooms().filter((r) => matchesFilter(r, f) && matchesQuery(r, q));
  });

  /** On a computer P.209 (the empty room of LL-03) starts selected; on a phone the list shows first. */
  protected readonly selectedId = linkedSignal<boolean, string | null>({
    source: this.viewport.isPhone,
    computation: (phone, previous) => previous?.value ?? (phone ? null : 'r-209'),
  });
  protected readonly selected = computed(() => {
    const id = this.selectedId();
    return id ? this.store.byId(id) : null;
  });

  /** Invite just sent from this screen, shown as the success state. */
  protected readonly sent = signal<{ roomId: string; invite: PendingInvite } | null>(null);

  protected readonly inviteModel = signal({ email: '', startDate: DEFAULT_START });
  protected readonly inviteForm = form(this.inviteModel, (p) => {
    required(p.email, { message: 'Nhập email người thuê.' });
    email(p.email, { message: 'Email chưa đúng, ví dụ ten@vidu.vn.' });
    required(p.startDate, { message: 'Chọn ngày bắt đầu thuê.' });
  });

  protected select(roomId: string | null): void {
    if (roomId !== this.selectedId()) {
      this.inviteModel.set({ email: '', startDate: DEFAULT_START });
      this.inviteForm().reset();
      this.sent.set(null);
    }
    this.selectedId.set(roomId);
  }

  protected sendInvite(event: Event, room: RoomRow): void {
    event.preventDefault();
    if (this.inviteForm().invalid()) {
      this.inviteForm.email().markAsTouched();
      this.inviteForm.startDate().markAsTouched();
      return;
    }
    const { email: to, startDate } = this.inviteModel();
    this.sent.set({ roomId: room.roomId, invite: this.store.invite(room.roomId, to, startDate) });
  }

  protected roomState(room: RoomRow): [Tone, string, string] {
    if (room.displayStatus === 'AWAITING_CONFIRM') return ['warn', 'i-mail', 'Chờ nhận lời mời'];
    if (room.status === 'VACANT') return ['neu', 'i-door', 'Trống'];
    return ['ok', 'i-user', 'Đang thuê'];
  }

  protected contractLabel(state: ContractState): string {
    return CONTRACT[state][2];
  }

  protected onQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }
}
