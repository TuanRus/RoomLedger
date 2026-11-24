import { DOCUMENT } from '@angular/common';
import { Service, computed, inject, signal } from '@angular/core';

import { MeterKind } from '../../../../core/data/models';
import { ROUND_START, UNIT_PRICE, sampleRound } from './capture-round.sample';
import { CaptureJob, CaptureRoom, CaptureSlot, SlotRef, SlotState } from './capture.models';

export const KINDS: MeterKind[] = ['ELECTRICITY', 'WATER'];
/** Simulated timings: the whole upload takes ~3 s, AI reading ~2.5 s (LM-07 says "2–5 giây"). */
export const UPLOAD_MS = 3000;
export const READING_MS = 2500;
const TICK_MS = 100;
/** Size shown for sample photos (LM-06: "3,8 MB · ảnh gốc, không nén"). */
const SAMPLE_BYTES = 3_800_000;

export type RoomGroup = 'todo' | 'retake' | 'done';

export function slotOf(room: CaptureRoom, kind: MeterKind): CaptureSlot {
  return kind === 'ELECTRICITY' ? room.electricity : room.water;
}

/** Usage and amount of a reading against the previous period (null when not computable). */
export function costOf(kind: MeterKind, previous: number, value: number | null): { usage: number; amount: number } | null {
  if (value == null || value < previous) return null;
  const usage = value - previous;
  return { usage, amount: usage * UNIT_PRICE[kind] };
}

/**
 * Landlord meter capture round (LM-04 … LM-10). Sample data only: the upload to S3 and the AI
 * reading are simulated with timers. Once the API is wired, capture() becomes
 * POST /rooms/{roomId}/meter-readings + PUT to the returned upload URL, and the AI result arrives
 * via GET /rooms/{roomId}/meter-readings polling.
 */
@Service()
export class MeterCaptureStore {
  private readonly win = inject(DOCUMENT).defaultView;

  readonly rooms = signal<CaptureRoom[]>(sampleRound());
  readonly jobs = signal<CaptureJob[]>([]);
  readonly active = signal<SlotRef | null>(null);

  /** navigator.onLine, kept current by the online/offline events. */
  readonly browserOnline = signal(this.win?.navigator.onLine ?? true);
  /** Demo switch to show LM-10 without unplugging the network. */
  readonly simulateOffline = signal(false);
  readonly online = computed(() => this.browserOnline() && !this.simulateOffline());
  /** "Tự gửi khi có mạng" (LM-10). */
  readonly autoSend = signal(true);

  private nextId = 1;
  private clockMinutes = 0;
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    this.win?.addEventListener('online', () => this.setBrowserOnline(true));
    this.win?.addEventListener('offline', () => this.setBrowserOnline(false));
  }

  // ---------- derived state ----------

  readonly jobFor = (roomId: string, kind: MeterKind) =>
    this.jobs().find((j) => j.roomId === roomId && j.kind === kind) ?? null;

  /** Effective state of one meter, counting photos taken in this session. */
  stateOf(room: CaptureRoom, kind: MeterKind): SlotState {
    const job = this.jobFor(room.roomId, kind);
    if (!job) return slotOf(room, kind).base;
    if (job.phase === 'read') return 'review';
    if (job.phase === 'unreadable') return 'retake';
    return 'sending';
  }

  groupOf(room: CaptureRoom): RoomGroup {
    const states = KINDS.map((k) => this.stateOf(room, k));
    if (states.includes('todo')) return 'todo';
    return states.includes('retake') ? 'retake' : 'done';
  }

  readonly total = computed(() => this.rooms().length);

  /** Rooms per pill. "Đã ghi" (done) includes retake rooms: their photo was taken, just unreadable. */
  readonly groups = computed(() => {
    const todo: CaptureRoom[] = [];
    const retake: CaptureRoom[] = [];
    const done: CaptureRoom[] = [];
    for (const room of this.rooms()) {
      const g = this.groupOf(room);
      if (g === 'todo') todo.push(room);
      else {
        done.push(room);
        if (g === 'retake') retake.push(room);
      }
    }
    return { todo, retake, done };
  });

  readonly recordedCount = computed(() => this.groups().done.length);

  readonly activeRoom = computed(() => {
    const a = this.active();
    return a ? (this.rooms().find((r) => r.roomId === a.roomId) ?? null) : null;
  });
  readonly activeSlot = computed(() => {
    const room = this.activeRoom();
    const a = this.active();
    return room && a ? slotOf(room, a.kind) : null;
  });
  readonly activeJob = computed(() => {
    const a = this.active();
    return a ? this.jobFor(a.roomId, a.kind) : null;
  });

  /** Next meter still to photograph after the active one: same room first, then down the list. */
  readonly nextSlot = computed<SlotRef | null>(() => {
    const order: SlotRef[] = [];
    const g = this.groups();
    for (const room of [...g.todo, ...g.retake]) {
      for (const kind of KINDS) {
        const s = this.stateOf(room, kind);
        if (s === 'todo' || s === 'retake') order.push({ roomId: room.roomId, kind });
      }
    }
    const a = this.active();
    const others = order.filter((s) => !a || s.roomId !== a.roomId || s.kind !== a.kind);
    if (!a) return others[0] ?? null;
    const sameRoom = others.find((s) => s.roomId === a.roomId);
    return sameRoom ?? others[0] ?? null;
  });

  readonly nextRoomCode = computed(() => {
    const n = this.nextSlot();
    return n ? (this.rooms().find((r) => r.roomId === n.roomId)?.roomCode ?? null) : null;
  });

  /** Photos not fully sent yet (LM-10 queue). */
  readonly pending = computed(() => this.jobs().filter((j) => j.phase === 'uploading'));
  readonly heldCount = computed(() => this.pending().filter((j) => j.held).length);

  // ---------- navigation ----------

  /** Opens the capture step for a room, on the first meter that still needs a photo. */
  open(roomId: string, kind?: MeterKind): void {
    const room = this.rooms().find((r) => r.roomId === roomId);
    if (!room) return;
    const k =
      kind ??
      KINDS.find((x) => ['todo', 'retake'].includes(this.stateOf(room, x))) ??
      KINDS.find((x) => this.jobFor(roomId, x)) ??
      'ELECTRICITY';
    this.active.set({ roomId, kind: k });
  }

  setKind(kind: MeterKind): void {
    const a = this.active();
    if (a) this.active.set({ ...a, kind });
  }

  close(): void {
    this.active.set(null);
  }

  /** "Chụp phòng kế" / "Bỏ qua": leave the current photo uploading and move on. */
  goNext(): void {
    this.active.set(this.nextSlot());
  }

  // ---------- capture ----------

  /** A photo picked from the camera or the computer. */
  capture(file: File): void {
    const url = this.win?.URL.createObjectURL(file) ?? '';
    this.startJob(url, !!url, file.size);
  }

  /** "Dùng ảnh mẫu": runs the same flow with the meter's sample photo. */
  captureSample(): void {
    const slot = this.activeSlot();
    if (slot) this.startJob(slot.sample.photoUrl, false, SAMPLE_BYTES);
  }

  /** "Hủy tải" (LM-06) or "Chụp lại" (LM-08/LM-09): drop the photo of the active meter. */
  discard(): void {
    const a = this.active();
    if (a) this.removeJob(a.roomId, a.kind);
  }

  /**
   * Confirms the value of the active meter (LM-08 "Duyệt", or LM-09 "Tự nhập số") and moves to the
   * next meter. Returns false when the value is lower than the previous reading.
   */
  approve(value: number): boolean {
    const a = this.active();
    const slot = this.activeSlot();
    if (!a || !slot || value < slot.previousValue) return false;
    const next = this.nextSlot();
    this.rooms.update((rooms) =>
      rooms.map((r) => {
        if (r.roomId !== a.roomId) return r;
        const updated: CaptureSlot = { ...slotOf(r, a.kind), base: 'approved', value };
        return a.kind === 'ELECTRICITY' ? { ...r, electricity: updated } : { ...r, water: updated };
      }),
    );
    this.removeJob(a.roomId, a.kind);
    this.active.set(next);
    return true;
  }

  // ---------- network ----------

  setBrowserOnline(value: boolean): void {
    this.browserOnline.set(value);
    this.onNetworkChange();
  }

  setSimulateOffline(value: boolean): void {
    this.simulateOffline.set(value);
    this.onNetworkChange();
  }

  /** "Thử gửi lại" (LM-10). Returns false while there is still no network. */
  retry(): boolean {
    if (!this.online()) return false;
    this.release();
    return true;
  }

  setAutoSend(value: boolean): void {
    this.autoSend.set(value);
    if (value && this.online()) this.release();
  }

  // ---------- simulation ----------

  /** Advances uploads and AI readings by ms. Called by the internal timer; public for tests. */
  tick(ms: number): void {
    const online = this.online();
    this.jobs.update((jobs) =>
      jobs.map((j) => {
        if (j.phase === 'uploading' && online && !j.held) {
          const progress = Math.min(100, j.progress + (100 * ms) / UPLOAD_MS);
          return progress >= 100 ? { ...j, progress: 100, phase: 'reading' } : { ...j, progress };
        }
        if (j.phase === 'reading') {
          const readingMs = j.readingMs + ms;
          if (readingMs < READING_MS) return { ...j, readingMs };
          return { ...j, readingMs, phase: j.aiValue == null ? 'unreadable' : 'read' };
        }
        return j;
      }),
    );
    if (!this.hasWork()) this.stopTimer();
  }

  private startJob(photoUrl: string, ownUrl: boolean, sizeBytes: number): void {
    const a = this.active();
    const slot = this.activeSlot();
    if (!a || !slot) return;
    this.removeJob(a.roomId, a.kind);
    const offline = !this.online();
    const capturedAt = this.clock();
    const job: CaptureJob = {
      id: this.nextId++,
      roomId: a.roomId,
      kind: a.kind,
      photoUrl,
      ownUrl,
      sizeBytes,
      progress: 0,
      phase: 'uploading',
      held: offline,
      heldAt: offline ? capturedAt : null,
      readingMs: 0,
      aiValue: slot.sample.aiValue,
      confidence: slot.sample.confidence,
      capturedAt,
    };
    this.jobs.update((jobs) => [...jobs, job]);
    this.ensureTimer();
  }

  private removeJob(roomId: string, kind: MeterKind): void {
    const job = this.jobFor(roomId, kind);
    if (!job) return;
    if (job.ownUrl) this.win?.URL.revokeObjectURL(job.photoUrl);
    this.jobs.update((jobs) => jobs.filter((j) => j.id !== job.id));
  }

  private onNetworkChange(): void {
    if (!this.online()) {
      const at = this.clock(false);
      this.jobs.update((jobs) =>
        jobs.map((j) => (j.phase === 'uploading' && !j.held ? { ...j, held: true, heldAt: at } : j)),
      );
    } else if (this.autoSend()) {
      this.release();
    }
  }

  private release(): void {
    this.jobs.update((jobs) => jobs.map((j) => (j.held ? { ...j, held: false, heldAt: null } : j)));
    this.ensureTimer();
  }

  private hasWork(): boolean {
    return this.jobs().some((j) => j.phase === 'reading' || (j.phase === 'uploading' && !j.held));
  }

  private ensureTimer(): void {
    if (this.timer || !this.hasWork() || !this.win) return;
    this.timer = this.win.setInterval(() => this.tick(TICK_MS), TICK_MS);
  }

  private stopTimer(): void {
    if (this.timer) this.win?.clearInterval(this.timer);
    this.timer = null;
  }

  /** Sample clock (01/12/2026 from 08:02), one minute per photo, so times match the story. */
  private clock(advance = true): string {
    const [date, time] = ROUND_START.split('T');
    const [h, m] = time.split(':').map(Number);
    const total = h * 60 + m + this.clockMinutes;
    if (advance) this.clockMinutes++;
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date}T${pad(Math.floor(total / 60) % 24)}:${pad(total % 60)}:00+07:00`;
  }
}
