import { Component, inject, signal } from '@angular/core';

import { Icon } from '../../../../shared/ui/icon';
import { MeterCaptureStore } from '../data/meter-capture.store';
import { KIND_LABEL, KIND_METER, hhmm } from './capture-util';

/**
 * LM-10: no network while uploading. Photos stay in this page (memory only, not persisted)
 * and are sent again on retry, or automatically when the network returns if the switch is on.
 */
@Component({
  selector: 'app-offline-queue',
  imports: [Icon],
  template: `
    @let n = store.pending().length;
    @if (!store.online()) {
      <div class="banner warn" role="status">
        <app-icon name="i-wifi-off" />
        @if (n) {
          <span><b>Mất kết nối mạng.</b> {{ n }} ảnh chưa gửi đi. Bạn vẫn chụp tiếp được, không cần chụp lại. Giữ trang này mở cho đến khi gửi xong.</span>
        } @else {
          <span><b>Mất kết nối mạng.</b> Bạn vẫn chụp được: ảnh chờ trong trang này và gửi khi có mạng.</span>
        }
      </div>
    } @else if (store.heldCount()) {
      <div class="banner info" role="status"><app-icon name="i-wifi" /><span><b>Đã có mạng.</b> {{ store.heldCount() }} ảnh đang chờ bạn bấm gửi.</span></div>
    }
    @if (n) {
      <ul class="list">
        @for (job of store.pending(); track job.id) {
          @let room = roomCode(job.roomId);
          <li class="list-item">
            <!-- Plain img: the photo can be a blob: URL, which NgOptimizedImage rejects. -->
            <img class="thumb" [src]="job.photoUrl" [alt]="'Ảnh ' + kindMeter[job.kind] + ' ' + room + ' đang chờ gửi'">
            <div class="grow">
              <b>{{ room }} · {{ kindLabel[job.kind] }}</b>
              @if (!job.held) {
                <p class="small muted">Đang gửi · {{ round(job.progress) }}%</p>
              } @else if (job.progress > 0) {
                <p class="small muted">Đã tải {{ round(job.progress) }}% · dừng lúc {{ hhmm(job.heldAt) }}</p>
              } @else {
                <p class="small muted">Chờ gửi · chụp {{ hhmm(job.capturedAt) }}</p>
              }
              @if (job.progress > 0) { <div class="bar" aria-hidden="true"><i [class.held]="job.held" [style.width.%]="job.progress"></i></div> }
            </div>
          </li>
        }
      </ul>
      @if (store.heldCount()) {
        <button type="button" class="btn lg block" (click)="retry()"><app-icon name="i-refresh" />Thử gửi lại {{ store.heldCount() }} ảnh</button>
      }
      <p class="small err-msg" role="status">{{ message() }}</p>
    }
    <button type="button" class="sheet switch" role="switch" [attr.aria-checked]="store.autoSend()" (click)="store.setAutoSend(!store.autoSend())">
      <span><b>Tự gửi khi có mạng</b><span class="small muted">Giữ trang này mở cho đến khi gửi xong</span></span>
      <span class="toggle" [class.on]="store.autoSend()" aria-hidden="true"></span>
    </button>
  `,
  styles: `
    :host { display: grid; gap: 12px; }
    ul.list { list-style: none; margin: 0; padding: 0; }
    .grow { flex: 1; min-width: 0; }
    .grow b { font-weight: 500; }
    .bar { margin-top: 6px; }
    .bar i.held { background: var(--warn); }
    .err-msg { color: var(--err); min-height: 0; }
    .err-msg:empty { display: none; }
    .switch { display: flex; align-items: center; justify-content: space-between; gap: 12px; width: 100%; min-height: 64px; text-align: left; cursor: pointer; font: inherit; color: inherit; }
    .switch > span:first-child { display: grid; gap: 2px; }
    .switch b { font-weight: 500; }
  `,
})
export class OfflineQueue {
  protected readonly store = inject(MeterCaptureStore);
  protected readonly kindLabel = KIND_LABEL;
  protected readonly kindMeter = KIND_METER;
  protected readonly hhmm = hhmm;
  protected readonly message = signal('');

  protected roomCode(roomId: string): string {
    return this.store.rooms().find((r) => r.roomId === roomId)?.roomCode ?? '';
  }

  protected round(n: number): number {
    return Math.round(n);
  }

  protected retry(): void {
    this.message.set(this.store.retry() ? '' : 'Vẫn chưa có mạng. Ảnh vẫn chờ trong trang này, không mất.');
  }
}
