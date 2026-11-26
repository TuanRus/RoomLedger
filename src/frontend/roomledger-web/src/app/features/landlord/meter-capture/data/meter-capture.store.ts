import { HttpClient } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { DestroyRef, Service, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription, catchError, exhaustMap, from, last, of, switchMap, tap, throwError, timer } from 'rxjs';

import type { components } from '../../../../generated/api-types';
import { injectApi } from '../../../../core/api/api-client';
import { UnsavedChanges } from '../../../../core/api/unsaved-changes';
import { LandlordPropertyStore } from '../../../../core/data/landlord-property.store';
import { MeterKind } from '../../../../core/data/models';
import { imageType, putPresigned, sha256Base64 } from '../../shared/upload';
import { ROUND_START, UNIT_PRICE, sampleRound } from './capture-round.sample';
import { CaptureJob, CaptureRoom, CaptureSlot, SlotBase, SlotRef, SlotState } from './capture.models';

type RoomReadingsResponse = components['schemas']['RoomReadingsResponse'];
type MeterReadingResponse = components['schemas']['MeterReadingResponse'];
type UploadUrlResponse = components['schemas']['UploadUrlResponse'];
interface CreateReadingResponse {
  reading?: MeterReadingResponse;
  upload?: UploadUrlResponse;
}

export const KINDS: MeterKind[] = ['ELECTRICITY', 'WATER'];
/** Simulated timings (sample data): the whole upload takes ~3 s, AI reading ~2.5 s (LM-07 says "2–5 giây"). */
export const UPLOAD_MS = 3000;
export const READING_MS = 2500;
const TICK_MS = 100;
/** Size shown for sample photos (LM-06: "3,8 MB · ảnh gốc, không nén"). */
const SAMPLE_BYTES = 3_800_000;
/** API: how often the readings are read again while the AI works, and how long to wait for it. */
export const POLL_MS = 2000;
export const POLL_MAX_MS = 60_000;

export type RoomGroup = 'todo' | 'retake' | 'done';

export function slotOf(room: CaptureRoom, kind: MeterKind): CaptureSlot {
  return kind === 'ELECTRICITY' ? room.electricity : room.water;
}

/** Usage and amount of a reading against the previous period (null when not computable or the rate is unknown). */
export function costOf(
  kind: MeterKind,
  previous: number | null,
  value: number | null,
  rate: number | null = UNIT_PRICE[kind],
): { usage: number; amount: number } | null {
  if (value == null || previous == null || rate == null || value < previous) return null;
  const usage = value - previous;
  return { usage, amount: usage * rate };
}

/** The slot of a meter from this period's reading: where it stands before any photo of this session. */
export function slotFromReading(kind: MeterKind, r: MeterReadingResponse | null | undefined): CaptureSlot {
  let base: SlotBase = 'todo';
  if (r) {
    if (r.status === 'CONFIRMED') base = 'approved';
    // The AI gave no number: the photo has to be retaken (or the number typed).
    else if (r.status === 'NEEDS_REVIEW' && r.value == null) base = 'retake';
    else base = 'sent';
  }
  return {
    kind,
    base,
    previousValue: r?.previousValue ?? null,
    meterNo: null,
    readingId: r?.readingId ?? null,
    value: r?.status === 'CONFIRMED' ? (r.value ?? null) : null,
    sample: null,
  };
}

/** GET /properties/{id}/meter-readings -> the capture round, in room-code order. Names come from the room list. */
export function toCaptureRooms(readings: RoomReadingsResponse[], names: Map<string, string>): CaptureRoom[] {
  return readings
    .map((r) => ({
      roomId: r.roomId ?? '',
      roomCode: r.roomCode ?? '',
      tenantName: names.get(r.roomId ?? '') ?? '',
      electricity: slotFromReading('ELECTRICITY', r.electricity),
      water: slotFromReading('WATER', r.water),
    }))
    .sort((a, b) => a.roomCode.localeCompare(b.roomCode, 'vi', { numeric: true }));
}

function findReading(rooms: RoomReadingsResponse[], readingId: string): MeterReadingResponse | undefined {
  for (const room of rooms) {
    if (room.electricity?.readingId === readingId) return room.electricity;
    if (room.water?.readingId === readingId) return room.water;
  }
  return undefined;
}

/** "2026-12-01T08:02:05+07:00" in the browser's own zone. */
export function localIso(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  const offset = -d.getTimezoneOffset();
  const sign = offset >= 0 ? '+' : '-';
  const abs = Math.abs(offset);
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}${sign}${p(Math.floor(abs / 60))}:${p(abs % 60)}`;
}

const failure = (err: unknown, fallback: string) => (err instanceof Error ? err.message : fallback);

/**
 * Landlord meter capture round (LM-04 … LM-10). Sample data by default: the upload and the AI reading are simulated
 * with timers. With environment.dataSource 'api' the round is GET /properties/{id}/meter-readings, a photo is
 * POST /rooms/{id}/meter-readings then a PUT to the presigned URL, the AI result is read by polling the same list
 * every 2 seconds, and approving is PATCH /meter-readings/{id} { value, confirm: true }.
 */
@Service()
export class MeterCaptureStore {
  private readonly api = injectApi();
  private readonly http = inject(HttpClient);
  private readonly property = inject(LandlordPropertyStore);
  private readonly win = inject(DOCUMENT).defaultView;

  /** True with sample data: the demo controls ("Dùng ảnh mẫu", "Giả lập mất mạng") may show. */
  readonly sample = !this.api;
  readonly period = this.property.period.asReadonly();
  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>(this.api ? 'idle' : 'ready');
  readonly error = signal<string | null>(null);

  private readonly sampleRooms = signal<CaptureRoom[]>(this.api ? [] : sampleRound());
  private readonly readings = signal<RoomReadingsResponse[]>([]);
  readonly rooms = computed<CaptureRoom[]>(() =>
    this.api ? toCaptureRooms(this.readings(), this.property.tenantNameByRoomId()) : this.sampleRooms(),
  );
  readonly jobs = signal<CaptureJob[]>([]);
  readonly active = signal<SlotRef | null>(null);
  /** An approval is on its way to the server. */
  readonly approving = signal(false);

  /** navigator.onLine, kept current by the online/offline events. */
  readonly browserOnline = signal(this.win?.navigator.onLine ?? true);
  /** Demo switch to show LM-10 without unplugging the network (sample data only). */
  readonly simulateOffline = signal(false);
  readonly online = computed(() => this.browserOnline() && !this.simulateOffline());
  /** "Tự gửi khi có mạng" (LM-10). */
  readonly autoSend = signal(true);

  private nextId = 1;
  private clockMinutes = 0;
  private timer: ReturnType<typeof setInterval> | null = null;
  private readonly subs = new Map<number, Subscription>();
  /** Photos uploaded and waiting for the AI, by job id: one poller below serves them all. */
  private readonly waiting = new Map<number, { readingId: string; started: number }>();
  private poller: Subscription | null = null;
  private loading: Subscription | null = null;
  /** The readings revision this copy was loaded at. */
  private seenRevision = -1;

  constructor() {
    this.win?.addEventListener('online', () => this.setBrowserOnline(true));
    this.win?.addEventListener('offline', () => this.setBrowserOnline(false));
    inject(DestroyRef).onDestroy(() => {
      this.subs.forEach((s) => s.unsubscribe());
      this.poller?.unsubscribe();
      this.loading?.unsubscribe();
    });
    this.load(true);
    if (this.api) {
      // Photos still on their way would be lost by closing the tab.
      inject(UnsavedChanges).register(() => this.jobs().some((j) => j.phase === 'uploading'));
      // Review (or another screen) changed the readings: this copy is old, read again.
      effect(() => {
        if (this.property.readingsRevision() !== this.seenRevision) untracked(() => this.load(true));
      });
    }
  }

  // ---------- loading (API) ----------

  /**
   * (Re)loads the round from the API; does nothing with sample data.
   * With `shared` the request another screen already made for the same readings is reused; otherwise the server is asked again.
   * A load still running is cancelled, so an older answer cannot overwrite a newer one.
   */
  load(shared = false): void {
    const api = this.api;
    if (!api) return;
    this.loading?.unsubscribe();
    if (!shared) this.property.invalidateReadings();
    this.seenRevision = this.property.readingsRevision();
    this.status.set('loading');
    this.error.set(null);
    this.loading = this.property
      .loadedReadings()
      .subscribe({
        next: (rooms) => {
          this.readings.set(rooms);
          this.status.set('ready');
        },
        error: (err: unknown) => {
          this.error.set(failure(err, 'Không tải được danh sách ghi chỉ số.'));
          this.status.set('error');
        },
      });
  }

  /** Always asks the server (the AI poll needs the newest state); the shared copy for other screens is dropped. */
  private fetchReadings(propertyId: string) {
    this.property.invalidateReadings(false);
    return this.api!.get<RoomReadingsResponse[]>(`/properties/${propertyId}/meter-readings`, { period: this.period() });
  }

  /** Unit price of a meter: the property's rate (API), or the sample ledger's. Null while the property is loading. */
  rateOf(kind: MeterKind): number | null {
    if (!this.api) return UNIT_PRICE[kind];
    const p = this.property.property();
    return (kind === 'ELECTRICITY' ? p?.electricityRate : p?.waterRate) ?? null;
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
  /** Photos whose upload failed and wait for "Thử lại" (API only). */
  readonly failedCount = computed(() => this.pending().filter((j) => j.error != null).length);

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
    if (this.api) {
      this.captureByApi(file);
      return;
    }
    const url = this.win?.URL.createObjectURL(file) ?? '';
    this.startJob(url, !!url, file.size);
  }

  /** "Dùng ảnh mẫu" (sample data only): runs the same flow with the meter's sample photo. */
  captureSample(): void {
    if (this.api) return;
    const slot = this.activeSlot();
    if (slot?.sample) this.startJob(slot.sample.photoUrl, false, SAMPLE_BYTES);
  }

  /** "Hủy tải" (LM-06) or "Chụp lại" (LM-08/LM-09): drop the photo of the active meter. */
  discard(): void {
    const a = this.active();
    if (a) this.removeJob(a.roomId, a.kind);
  }

  /** "Thử lại" on a photo that failed to upload (API only). */
  retryJob(roomId: string, kind: MeterKind): void {
    const job = this.jobFor(roomId, kind);
    if (this.api && job && job.phase === 'uploading' && !job.held) this.runUpload(job.id);
  }

  /**
   * Confirms the value of the active meter (LM-08 "Duyệt", or LM-09 "Tự nhập số") and moves to the
   * next meter. Returns false when the value is lower than the previous reading (or an approval is already on its way).
   */
  approve(value: number): boolean {
    const a = this.active();
    const slot = this.activeSlot();
    if (!a || !slot || (slot.previousValue != null && value < slot.previousValue)) return false;
    if (this.api) return this.approveByApi(a, slot, value);
    const next = this.nextSlot();
    this.sampleRooms.update((rooms) =>
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
    if (this.api) return;
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

  // ---------- simulation (sample data) ----------

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
      aiValue: slot.sample?.aiValue ?? null,
      confidence: slot.sample?.confidence ?? null,
      capturedAt,
      readingId: null,
      error: null,
      file: null,
      upload: null,
      aiTimedOut: false,
    };
    this.jobs.update((jobs) => [...jobs, job]);
    this.ensureTimer();
  }

  // ---------- API ----------

  private captureByApi(file: File): void {
    const a = this.active();
    const slot = this.activeSlot();
    if (!a || !slot) return;
    this.removeJob(a.roomId, a.kind);
    const type = imageType(file);
    const url = this.win?.URL.createObjectURL(file) ?? '';
    const offline = !this.online();
    const capturedAt = this.clock();
    const job: CaptureJob = {
      id: this.nextId++,
      roomId: a.roomId,
      kind: a.kind,
      photoUrl: url,
      ownUrl: !!url,
      sizeBytes: file.size,
      progress: 0,
      phase: 'uploading',
      held: offline && !!type,
      heldAt: offline && type ? capturedAt : null,
      readingMs: 0,
      aiValue: null,
      confidence: null,
      capturedAt,
      readingId: null,
      error: type ? null : 'Chỉ nhận ảnh JPG, PNG hoặc HEIC.',
      file,
      upload: null,
      aiTimedOut: false,
    };
    this.jobs.update((jobs) => [...jobs, job]);
    if (type && !offline) this.runUpload(job.id);
  }

  /**
   * Creates the reading and uploads the photo; when it is up, waits for the AI.
   * A link that expired while the job was held is asked for again: the API answers with a fresh URL for the same reading.
   */
  private runUpload(jobId: number): void {
    const api = this.api;
    const job = this.jobs().find((j) => j.id === jobId);
    if (!api || !job?.file) return;
    const file = job.file;
    const type = imageType(file);
    if (!type) return;
    this.subs.get(jobId)?.unsubscribe();
    this.patchJob(jobId, { error: null, progress: 0 });
    const reusable = job.upload && job.readingId && Date.parse(job.upload.expiresAt ?? '') > Date.now();
    const created$ = reusable
      ? of<CreateReadingResponse>({ reading: { readingId: job.readingId! }, upload: job.upload! })
      : from(sha256Base64(file)).pipe(
          switchMap((sha256) =>
            api.post<CreateReadingResponse>(`/rooms/${job.roomId}/meter-readings`, {
              kind: job.kind,
              period: this.period(),
              fileName: file.name,
              contentType: type,
              size: file.size,
              sha256,
            }),
          ),
        );
    const sub = created$
      .pipe(
        tap((res) => this.patchJob(jobId, { readingId: res.reading?.readingId ?? null, upload: res.upload ?? null })),
        switchMap((res) => (res.upload ? putPresigned(this.http, res.upload, file) : throwError(() => new Error('Không nhận được link tải ảnh lên.')))),
      )
      .subscribe({
        next: (pct) => this.patchJob(jobId, { progress: pct }),
        error: (err: unknown) => this.patchJob(jobId, { error: failure(err, 'Chưa tải được ảnh lên, thử lại.') }),
        complete: () => this.waitForAi(jobId),
      });
    this.subs.set(jobId, sub);
  }

  /**
   * Waits for the AI to read this photo: the job joins the shared poller, which reads the round every 2 s
   * while any job is waiting and finishes each by its reading id (at most a minute per job).
   */
  private waitForAi(jobId: number): void {
    const api = this.api;
    const job = this.jobs().find((j) => j.id === jobId);
    if (!api || !job?.readingId || !this.property.propertyId()) return;
    this.patchJob(jobId, { phase: 'reading', progress: 100 });
    this.waiting.set(jobId, { readingId: job.readingId, started: Date.now() });
    this.startPoller();
  }

  private startPoller(): void {
    if (this.poller) return;
    this.poller = timer(POLL_MS, POLL_MS)
      .pipe(
        exhaustMap(() => {
          const propertyId = this.property.propertyId();
          return propertyId ? this.fetchReadings(propertyId).pipe(catchError(() => of<RoomReadingsResponse[] | null>(null))) : of<RoomReadingsResponse[] | null>(null);
        }),
      )
      .subscribe((rooms) => {
        if (rooms) this.readings.set(rooms);
        for (const [jobId, w] of [...this.waiting]) {
          const r = rooms ? findReading(rooms, w.readingId) : undefined;
          const answered = !!r && r.status !== 'PENDING_AI';
          if (!answered && Date.now() - w.started < POLL_MAX_MS) continue;
          this.waiting.delete(jobId);
          this.finishReading(jobId, r);
        }
        if (!this.waiting.size) this.stopPoller();
      });
  }

  private stopPoller(): void {
    this.poller?.unsubscribe();
    this.poller = null;
  }

  /** The readings changed on the server: other screens reload; this copy is current already. */
  private changed(): void {
    this.property.invalidateReadings();
    this.seenRevision = this.property.readingsRevision();
  }

  private finishReading(jobId: number, r: MeterReadingResponse | undefined): void {
    this.changed();
    if (!r || r.status === 'PENDING_AI') {
      this.patchJob(jobId, { phase: 'read', aiValue: null, confidence: null, aiTimedOut: true });
    } else if (r.value == null && r.aiValue == null) {
      this.patchJob(jobId, { phase: 'unreadable', aiValue: null, confidence: null });
    } else {
      this.patchJob(jobId, { phase: 'read', aiValue: r.value ?? r.aiValue ?? null, confidence: r.aiConfidence ?? null });
    }
  }

  private approveByApi(a: SlotRef, slot: CaptureSlot, value: number): boolean {
    const api = this.api!;
    const job = this.jobFor(a.roomId, a.kind);
    const readingId = job?.readingId ?? slot.readingId;
    if (!readingId || this.approving()) return false;
    const next = this.nextSlot();
    this.approving.set(true);
    if (job) this.patchJob(job.id, { error: null });
    api.patch<MeterReadingResponse>(`/meter-readings/${readingId}`, { value, confirm: true }).subscribe({
      next: (saved) => {
        this.readings.update((rooms) =>
          rooms.map((room) => ({
            ...room,
            electricity: room.electricity?.readingId === saved.readingId ? saved : room.electricity,
            water: room.water?.readingId === saved.readingId ? saved : room.water,
          })),
        );
        this.changed();
        this.approving.set(false);
        this.removeJob(a.roomId, a.kind);
        this.active.set(next);
      },
      error: (err: unknown) => {
        this.approving.set(false);
        if (job) this.patchJob(job.id, { error: failure(err, 'Chưa lưu được chỉ số, thử lại.') });
      },
    });
    return true;
  }

  private patchJob(jobId: number, changes: Partial<CaptureJob>): void {
    this.jobs.update((jobs) => jobs.map((j) => (j.id === jobId ? { ...j, ...changes } : j)));
  }

  private removeJob(roomId: string, kind: MeterKind): void {
    const job = this.jobFor(roomId, kind);
    if (!job) return;
    this.subs.get(job.id)?.unsubscribe();
    this.subs.delete(job.id);
    this.waiting.delete(job.id);
    if (!this.waiting.size) this.stopPoller();
    if (job.ownUrl) this.win?.URL.revokeObjectURL(job.photoUrl);
    this.jobs.update((jobs) => jobs.filter((j) => j.id !== job.id));
  }

  private onNetworkChange(): void {
    if (!this.online()) {
      const at = this.clock(false);
      for (const j of this.jobs()) {
        if (j.phase !== 'uploading' || j.held) continue;
        // An API upload in flight is dropped; it starts again from the top when the network is back.
        if (this.api) this.subs.get(j.id)?.unsubscribe();
      }
      this.jobs.update((jobs) =>
        jobs.map((j) => (j.phase === 'uploading' && !j.held && j.error == null ? { ...j, held: true, heldAt: at } : j)),
      );
    } else if (this.autoSend()) {
      this.release();
    }
  }

  private release(): void {
    const released = this.jobs().filter((j) => j.held);
    this.jobs.update((jobs) => jobs.map((j) => (j.held ? { ...j, held: false, heldAt: null } : j)));
    if (this.api) for (const j of released) if (j.phase === 'uploading') this.runUpload(j.id);
    this.ensureTimer();
  }

  private hasWork(): boolean {
    return this.jobs().some((j) => j.phase === 'reading' || (j.phase === 'uploading' && !j.held));
  }

  private ensureTimer(): void {
    if (this.api || this.timer || !this.hasWork() || !this.win) return;
    this.timer = this.win.setInterval(() => this.tick(TICK_MS), TICK_MS);
  }

  private stopTimer(): void {
    if (this.timer) this.win?.clearInterval(this.timer);
    this.timer = null;
  }

  /** Sample data: a clock (01/12/2026 from 08:02), one minute per photo, so times match the story. With the API: now. */
  private clock(advance = true): string {
    if (this.api) return localIso(new Date());
    const [date, time] = ROUND_START.split('T');
    const [h, m] = time.split(':').map(Number);
    const total = h * 60 + m + this.clockMinutes;
    if (advance) this.clockMinutes++;
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date}T${pad(Math.floor(total / 60) % 24)}:${pad(total % 60)}:00+07:00`;
  }
}
