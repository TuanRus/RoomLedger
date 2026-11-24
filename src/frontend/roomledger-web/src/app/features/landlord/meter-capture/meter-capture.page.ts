import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, afterRenderEffect, computed, inject, signal } from '@angular/core';

import { Viewport } from '../../../core/layout/viewport';
import { PeriodPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { CaptureRoom } from './data/capture.models';
import { CAPTURE_PERIOD, ROUND_DATE } from './data/capture-round.sample';
import { KINDS, MeterCaptureStore, RoomGroup } from './data/meter-capture.store';
import { CaptureCamera } from './ui/capture-camera';
import { CaptureDesk } from './ui/capture-desk';
import { CaptureResult } from './ui/capture-result';
import { KIND_LABEL } from './ui/capture-util';
import { OfflineQueue } from './ui/offline-queue';

const PILLS: { id: RoomGroup; label: string }[] = [
  { id: 'todo', label: 'Chưa ghi' },
  { id: 'retake', label: 'Chụp lại' },
  { id: 'done', label: 'Đã ghi' },
];

/**
 * LM-04 … LM-10: the landlord's meter capture round. The round list is the page; choosing a room
 * opens the capture step (full screen on a phone, side panel on a computer).
 */
@Component({
  selector: 'app-meter-capture-page',
  imports: [NgTemplateOutlet, Icon, PeriodPipe, CaptureCamera, CaptureResult, CaptureDesk, OfflineQueue],
  templateUrl: './meter-capture.page.html',
  styleUrl: './meter-capture.page.scss',
  host: { '(keydown.escape)': 'store.close()' },
})
export class MeterCapturePage {
  protected readonly store = inject(MeterCaptureStore);
  protected readonly viewport = inject(Viewport);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly pills = PILLS;
  protected readonly period = CAPTURE_PERIOD;
  protected readonly roundDate = ROUND_DATE;
  protected readonly filter = signal<RoomGroup>('todo');

  protected readonly visible = computed(() => this.store.groups()[this.filter()]);
  protected readonly percent = computed(() => Math.round((this.store.recordedCount() / this.store.total()) * 100));
  protected readonly showQueue = computed(() => !this.store.online() || this.store.heldCount() > 0);
  /** Phone: the light result screen (LM-08/LM-09) replaces the dark camera once AI has answered. */
  protected readonly showResult = computed(() => {
    const p = this.store.activeJob()?.phase;
    return p === 'read' || p === 'unreadable';
  });

  /** Room whose row gets focus back when the capture step closes. */
  private lastRoomId: string | null = null;

  constructor() {
    // Move focus to the capture step heading whenever the step opens, changes meter or switches screen,
    // and back to the room row when it closes.
    afterRenderEffect(() => {
      const a = this.store.active();
      this.showResult();
      const root = this.host.nativeElement;
      if (a) {
        this.lastRoomId = a.roomId;
        root.querySelector<HTMLElement>('.cap-focus')?.focus({ preventScroll: !this.viewport.isPhone() });
      } else if (this.lastRoomId) {
        root.querySelector<HTMLElement>(`[data-room="${this.lastRoomId}"]`)?.focus();
        this.lastRoomId = null;
      }
    });
  }

  protected isActive(room: CaptureRoom): boolean {
    return this.store.active()?.roomId === room.roomId;
  }

  /** Second line of a row, e.g. "Trần Minh Anh · còn nước" (LM-04). */
  protected describe(room: CaptureRoom): string {
    const states = KINDS.map((k) => ({ k, s: this.store.stateOf(room, k) }));
    const missing = states.filter((x) => x.s === 'todo' || x.s === 'retake');
    let text: string;
    if (missing.some((x) => x.s === 'retake')) {
      text = missing.map((x) => KIND_LABEL[x.k]).join(', ') + ': AI không đọc được, cần chụp lại';
    } else if (missing.length === 2) {
      text = 'điện, nước';
    } else if (missing.length === 1) {
      text = 'còn ' + KIND_LABEL[missing[0].k];
    } else if (states.some((x) => x.s === 'sending')) {
      text = this.store.online() ? 'đang gửi ảnh' : 'ảnh chờ mạng';
    } else if (states.some((x) => x.s === 'review' || x.s === 'sent')) {
      text = 'đã gửi, chờ duyệt';
    } else {
      text = 'đã duyệt';
    }
    return `${room.tenantName} · ${text}`;
  }
}
