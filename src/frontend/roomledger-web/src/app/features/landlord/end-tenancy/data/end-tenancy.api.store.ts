import { HttpClient } from '@angular/common/http';
import { Service, computed, inject, signal } from '@angular/core';
import { Observable, Subscription, catchError, exhaustMap, from, map, mergeMap, of, switchMap, tap, throwError, timer, toArray } from 'rxjs';

import type { components } from '../../../../generated/api-types';
import { ApiError, injectApi } from '../../../../core/api/api-client';
import { LandlordPropertyStore } from '../../../../core/data/landlord-property.store';
import { MeterKind } from '../../../../core/data/models';
import { RoomsStore } from '../../rooms/data/rooms.store';
import { imageType, putPresigned, sha256Base64 } from '../../shared/upload';
import { EndKind } from './end-tenancy.models';

type RoomDetailResponse = components['schemas']['RoomDetailResponse'];
type TenancyDetailResponse = components['schemas']['TenancyDetailResponse'];
type EndTenancyResponse = components['schemas']['EndTenancyResponse'];
type FinalReadingResponse = components['schemas']['FinalReading'];
type MeterReadingResponse = components['schemas']['MeterReadingResponse'];
type UploadUrlResponse = components['schemas']['UploadUrlResponse'];
interface CreateReadingResponse {
  reading?: MeterReadingResponse;
  upload?: UploadUrlResponse;
}

export const KINDS: MeterKind[] = ['ELECTRICITY', 'WATER'];
/** How often the end of tenancy is read again while the AI reads a final photo, and how long to wait for it. */
export const POLL_MS = 2000;
export const POLL_MAX_MS = 60_000;

/** Where a final reading stands: no photo, photo on its way, AI reading it, waiting for approval, approved. */
export type ReadingPhase = 'missing' | 'uploading' | 'reading' | 'pending' | 'approved';

/** A photo being sent for one final reading (this session only). */
export interface ReadingJob {
  phase: 'uploading' | 'reading';
  progress: number;
  error: string | null;
  file: File;
  /** The AI did not answer in time: the landlord can type the number. */
  aiTimedOut: boolean;
}

export interface InvoiceLineView {
  usage: number;
  rate: number;
  amount: number;
}

/** The final invoice as the server will build it: rent typed by the landlord, usage from the final readings, the property's service fee. */
export interface FinalInvoiceView {
  rent: number | null;
  electricity: InvoiceLineView | null;
  water: InvoiceLineView | null;
  serviceFee: number;
  /** null until every line is known. */
  invoiceTotal: number | null;
  /** Earlier invoices still unpaid: shown apart, not part of the final invoice. */
  unpaidPrevious: number;
  /** Final invoice plus unpaidPrevious. */
  toCollect: number | null;
  /** The deposit held (display only: nothing is deducted by the system). */
  deposit: number;
  /** deposit − toCollect: positive is returned to the tenant, negative is still owed by them. */
  balance: number | null;
}

/** "2026-11-25" -> "2026-11". */
export function periodOf(isoDate: string): string {
  return isoDate.slice(0, 7);
}

export function readingPhase(r: FinalReadingResponse | undefined, job: ReadingJob | undefined): ReadingPhase {
  if (r?.status === 'CONFIRMED') return 'approved';
  if (job) return job.phase;
  if (!r?.readingId) return 'missing';
  return r.status === 'PENDING_AI' && r.value == null ? 'reading' : 'pending';
}

function line(r: FinalReadingResponse | undefined, rate: number | undefined): InvoiceLineView | null {
  const previous = r?.meterReplaced ? 0 : r?.previous;
  if (r?.status !== 'CONFIRMED' || r.value == null || previous == null || rate == null) return null;
  const usage = Math.max(0, r.value - previous);
  return { usage, rate, amount: usage * rate };
}

/** Final invoice preview. The server builds the real one on finish; the numbers follow the same rules (rent never prorated). */
export function finalInvoiceOf(end: EndTenancyResponse | null, lastRent: number | null, serviceFee: number): FinalInvoiceView {
  const electricity = line(end?.electricity, end?.rates?.electricity);
  const water = line(end?.water, end?.rates?.water);
  const invoiceTotal = lastRent != null && electricity && water ? lastRent + electricity.amount + water.amount + serviceFee : null;
  const unpaidPrevious = end?.unpaidPrevious ?? 0;
  const toCollect = invoiceTotal == null ? null : invoiceTotal + unpaidPrevious;
  const deposit = end?.deposit ?? 0;
  return { rent: lastRent, electricity, water, serviceFee, invoiceTotal, unpaidPrevious, toCollect, deposit, balance: toCollect == null ? null : deposit - toCollect };
}

const failure = (err: unknown, fallback: string) => (err instanceof Error ? err.message : fallback);

/**
 * End of tenancy and room moves (LL-10) against the API: POST/GET/DELETE /tenancies/{id}/end, final readings through
 * POST /rooms/{id}/meter-readings and PATCH /meter-readings/{id}, then POST /tenancies/{id}/end:finish.
 * One room at a time (open). After finish the room list and readings are loaded again.
 */
@Service()
export class EndTenancyApiStore {
  private readonly api = injectApi();
  private readonly http = inject(HttpClient);
  private readonly property = inject(LandlordPropertyStore);
  private readonly rooms = inject(RoomsStore);

  readonly roomId = signal<string | null>(null);
  readonly tenancyId = signal<string | null>(null);
  /** The tenancy's status when it was loaded; MOVING_OUT without end data means the old move-out inspection is running. */
  readonly tenancyStatus = signal<string | null>(null);
  readonly tenantName = signal<string | null>(null);
  /** YYYY-MM-DD */
  readonly tenancyStart = signal<string | null>(null);
  readonly end = signal<EndTenancyResponse | null>(null);
  /** Loading of the room and its end state. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  readonly error = signal<string | null>(null);
  /** A request (start, approve, finish, cancel) is on its way. */
  readonly busy = signal(false);
  readonly actionError = signal<string | null>(null);
  readonly jobs = signal<Partial<Record<MeterKind, ReadingJob>>>({});

  // The landlord's inputs.
  readonly kind = signal<EndKind>('LEAVE');
  readonly endDate = signal('');
  readonly targetRoomId = signal<string | null>(null);
  readonly lastRent = signal<number | null>(null);
  readonly startElectricity = signal<number | null>(null);
  readonly startWater = signal<number | null>(null);

  readonly roomCode = computed(() => this.end()?.roomCode ?? this.property.roomById().get(this.roomId() ?? '')?.code ?? '');
  readonly monthlyRent = computed(() => this.property.roomById().get(this.roomId() ?? '')?.monthlyRent ?? 0);
  /** Empty rooms of the property the tenant can move into. */
  readonly targets = computed(() =>
    this.property
      .rooms()
      .filter((r) => r.displayStatus === 'VACANT' && r.roomId !== this.roomId() && !this.heldRooms().has(r.roomId ?? ''))
      .sort((a, b) => (a.code ?? '').localeCompare(b.code ?? '', 'vi', { numeric: true })),
  );
  readonly started = computed(() => this.end() != null);
  readonly finished = computed(() => this.end()?.endedAt != null);
  readonly finalPeriod = computed(() => this.end()?.finalPeriod ?? (this.endDate() ? periodOf(this.endDate()) : ''));
  readonly invoice = computed(() => finalInvoiceOf(this.end(), this.lastRent(), this.property.property()?.serviceFee ?? 0));
  readonly phases = computed<Record<MeterKind, ReadingPhase>>(() => {
    const e = this.end();
    const jobs = this.jobs();
    return { ELECTRICITY: readingPhase(e?.electricity, jobs.ELECTRICITY), WATER: readingPhase(e?.water, jobs.WATER) };
  });
  readonly readingsApproved = computed(() => KINDS.every((k) => this.phases()[k] === 'approved'));

  /** Bumped by reset(): an answer that arrives after the page moved to another tenancy is ignored. */
  private epoch = 0;
  /** Vacant rooms that still have a tenancy (a draft): the server does not count them as free either. */
  private readonly heldRooms = signal<Set<string>>(new Set());
  private poller: Subscription | null = null;
  private pollStarted = 0;
  private uploads: Partial<Record<MeterKind, Subscription>> = {};
  private loading: Subscription | null = null;

  /** The server cannot build the final invoice: no previous reading or opening reading for this meter, and the meter was not replaced. */
  missingPrevious(kind: MeterKind): boolean {
    const r = kind === 'ELECTRICITY' ? this.end()?.electricity : this.end()?.water;
    return !!r && r.previous == null && !r.meterReplaced && this.replacedNow()[kind] !== true;
  }

  /** Meters the landlord marked as replaced in the approval box not yet sent. */
  readonly replacedNow = signal<Partial<Record<MeterKind, boolean>>>({});

  /** Why finishing is not possible yet, in the order to fix them. Empty means the landlord can finish. */
  readonly blockers = computed(() => {
    const out: string[] = [];
    const e = this.end();
    if (!e) return ['chưa bắt đầu kết thúc thuê'];
    for (const k of KINDS) {
      const phase = this.phases()[k];
      const name = k === 'ELECTRICITY' ? 'điện' : 'nước';
      if (this.missingPrevious(k)) out.push(`chưa có chỉ số ${name} kỳ trước (nhập chỉ số nhận phòng hoặc chọn thay đồng hồ)`);
      if (phase === 'approved') continue;
      out.push(phase === 'pending' ? `chỉ số ${name} cuối chưa duyệt` : `còn thiếu chỉ số ${name} cuối`);
    }
    if (this.lastRent() == null) out.push('chưa nhập tiền phòng tháng cuối');
    if (e.kind === 'MOVE') {
      if (this.startElectricity() == null) out.push('chưa nhập chỉ số điện đầu ở phòng mới');
      if (this.startWater() == null) out.push('chưa nhập chỉ số nước đầu ở phòng mới');
    }
    return out;
  });

  /** Loads a room's tenancy and, when it is being ended, the end state. Replaces whatever room was open. */
  open(roomId: string): void {
    const api = this.api;
    if (!api) return;
    this.loading?.unsubscribe();
    this.stopPoller();
    this.reset(roomId);
    const epoch = this.epoch;
    this.status.set('loading');
    this.loading = api
      .get<RoomDetailResponse>(`/rooms/${roomId}`)
      .pipe(
        switchMap((room) => {
          const id = room.currentTenancy?.tenancyId ?? null;
          this.tenancyId.set(id);
          this.tenancyStatus.set(room.currentTenancy?.status ?? null);
          this.tenantName.set(room.currentTenancy?.tenantName ?? null);
          return id ? api.get<TenancyDetailResponse>(`/tenancies/${id}`) : of(null);
        }),
        tap((t) => this.tenancyStart.set(startOf(t))),
        switchMap(() => {
          const id = this.tenancyId();
          if (!id || this.tenancyStatus() !== 'MOVING_OUT') return of(null);
          // 404: the tenancy is moving out through the old inspection, not through this flow.
          return api.get<EndTenancyResponse>(`/tenancies/${id}/end`).pipe(catchError((err: unknown) => (err instanceof ApiError && err.status === 404 ? of(null) : throwError(() => err))));
        }),
      )
      .subscribe({
        next: (end) => {
          if (epoch !== this.epoch) return;
          if (end) this.applyEnd(end);
          this.status.set('ready');
          this.checkTargets(epoch);
        },
        error: (err: unknown) => {
          if (epoch !== this.epoch) return;
          this.error.set(failure(err, 'Không tải được hợp đồng của phòng.'));
          this.status.set('error');
        },
      });
  }

  /**
   * A room without a tenant can still hold a draft tenancy, which the server counts as taken. The room list does not
   * say, so each vacant room's current tenancy is read (GET /rooms/{id}); rooms that have one drop out of targets().
   */
  private checkTargets(epoch: number): void {
    const api = this.api;
    if (!api) return;
    const ids = this.property.rooms().filter((r) => r.displayStatus === 'VACANT' && r.roomId && r.roomId !== this.roomId()).map((r) => r.roomId!);
    from(ids)
      .pipe(
        mergeMap((id) => api.get<RoomDetailResponse>(`/rooms/${id}`).pipe(map((d) => (d.currentTenancy?.tenancyId ? id : null)), catchError(() => of(null))), 4),
        toArray(),
      )
      .subscribe((held) => {
        if (epoch === this.epoch) this.heldRooms.set(new Set(held.filter((id): id is string => id != null)));
      });
  }

  /** Step 1: POST /tenancies/{id}/end (a second call while moving out changes the date, kind and target). */
  start(): void {
    const api = this.api;
    const id = this.tenancyId();
    if (!api || !id || this.busy()) return;
    const body = { endDate: this.endDate(), kind: this.kind(), targetRoomId: this.kind() === 'MOVE' ? this.targetRoomId() : undefined };
    this.run(api.post<EndTenancyResponse>(`/tenancies/${id}/end`, body), (end) => {
      this.applyEnd(end);
      this.tenancyStatus.set('MOVING_OUT');
      this.property.reloadRooms();
      this.property.invalidateReadings();
    });
  }

  /** Step 2: sends the photo of a final reading; the AI value then waits for approve(). */
  uploadPhoto(kind: MeterKind, file: File): void {
    const api = this.api;
    const end = this.end();
    if (!api || !end || this.finished()) return;
    this.uploads[kind]?.unsubscribe();
    const type = imageType(file);
    if (!type) {
      this.setJob(kind, { phase: 'uploading', progress: 0, error: 'Chỉ nhận ảnh JPG, PNG hoặc HEIC.', file, aiTimedOut: false });
      return;
    }
    this.setJob(kind, { phase: 'uploading', progress: 0, error: null, file, aiTimedOut: false });
    this.uploads[kind] = from(sha256Base64(file))
      .pipe(
        switchMap((sha256) =>
          api.post<CreateReadingResponse>(`/rooms/${end.roomId}/meter-readings`, {
            kind,
            period: end.finalPeriod,
            fileName: file.name,
            contentType: type,
            size: file.size,
            sha256,
          }),
        ),
        switchMap((res) => (res.upload ? putPresigned(this.http, res.upload, file) : throwError(() => new Error('Không nhận được link tải ảnh lên.')))),
      )
      .subscribe({
        next: (progress) => this.patchJob(kind, { progress }),
        error: (err: unknown) => this.patchJob(kind, { error: failure(err, 'Chưa tải được ảnh lên, thử lại.') }),
        complete: () => {
          this.patchJob(kind, { phase: 'reading', progress: 100 });
          this.pollStarted = Date.now();
          this.startPoller();
        },
      });
  }

  /** Drops a failed or stuck photo so the landlord can pick another. */
  discardJob(kind: MeterKind): void {
    this.uploads[kind]?.unsubscribe();
    this.jobs.update((all) => {
      const rest = { ...all };
      delete rest[kind];
      return rest;
    });
  }

  /** Step 2: PATCH /meter-readings/{id} { value, confirm: true }; the value is the AI's unless the landlord typed another. */
  approve(kind: MeterKind, value: number, meterReplaced = false): void {
    const api = this.api;
    const r = kind === 'ELECTRICITY' ? this.end()?.electricity : this.end()?.water;
    if (!api || !r?.readingId || this.busy()) return;
    if (!meterReplaced && r.previous != null && value < r.previous) {
      this.actionError.set('Chỉ số cuối không được nhỏ hơn chỉ số kỳ trước.');
      return;
    }
    this.run(api.patch<MeterReadingResponse>(`/meter-readings/${r.readingId}`, meterReplaced ? { value, meterReplaced: true, confirm: true } : { value, confirm: true }), () => {
      this.discardJob(kind);
      this.property.invalidateReadings();
      this.refresh();
    });
  }

  /** Step 4: POST /tenancies/{id}/end:finish. Reloads the rooms and readings the screens of the property hold. */
  finish(): void {
    const api = this.api;
    const id = this.tenancyId();
    const e = this.end();
    if (!api || !id || !e || this.busy() || this.blockers().length) return;
    const body = {
      lastRent: this.lastRent(),
      startElectricity: e.kind === 'MOVE' ? this.startElectricity() : undefined,
      startWater: e.kind === 'MOVE' ? this.startWater() : undefined,
    };
    this.run(api.post<EndTenancyResponse>(`/tenancies/${id}/end:finish`, body), (done) => {
      this.end.set(done);
      this.stopPoller();
      this.property.reloadRooms();
      this.property.invalidateReadings();
      // The room's tenancy details were cached while it was rented.
      this.rooms.loadDetail(done.roomId ?? '', true);
      if (done.targetRoom?.roomId) this.rooms.loadDetail(done.targetRoom.roomId, true);
    });
  }

  /** DELETE /tenancies/{id}/end: back to an active tenancy. */
  cancel(): void {
    const api = this.api;
    const id = this.tenancyId();
    if (!api || !id || this.busy() || this.finished()) return;
    this.run(api.delete<unknown>(`/tenancies/${id}/end`), () => {
      this.stopPoller();
      const roomId = this.roomId();
      this.reset(roomId);
      this.tenancyId.set(id);
      this.tenancyStatus.set('ACTIVE');
      this.status.set('ready');
      this.property.reloadRooms();
      this.property.invalidateReadings();
      if (roomId) this.open(roomId);
    });
  }

  /** Reads the end state again (after an approval or a poll). */
  refresh(): void {
    const api = this.api;
    const id = this.tenancyId();
    if (!api || !id) return;
    const epoch = this.epoch;
    api.get<EndTenancyResponse>(`/tenancies/${id}/end`).subscribe({
      next: (end) => epoch === this.epoch && this.end.set(end),
      error: (err: unknown) => epoch === this.epoch && this.actionError.set(failure(err, 'Không tải được chỉ số cuối.')),
    });
  }

  private applyEnd(end: EndTenancyResponse): void {
    this.end.set(end);
    this.kind.set(end.kind ?? 'LEAVE');
    this.endDate.set(end.endDate ?? '');
    this.targetRoomId.set(end.targetRoom?.roomId ?? null);
    if (end.lastRent != null) this.lastRent.set(end.lastRent);
    // A reading with a photo but no approval, from before this page was opened, still waits for the AI.
    if ((['electricity', 'water'] as const).some((k) => end[k]?.status === 'PENDING_AI' && end[k]?.value == null)) {
      this.pollStarted = Date.now();
      this.startPoller();
    }
  }

  private reset(roomId: string | null): void {
    this.epoch++;
    for (const kind of KINDS) this.uploads[kind]?.unsubscribe();
    this.uploads = {};
    this.stopPoller();
    this.heldRooms.set(new Set());
    this.replacedNow.set({});
    this.roomId.set(roomId);
    this.tenancyId.set(null);
    this.tenancyStatus.set(null);
    this.tenantName.set(null);
    this.tenancyStart.set(null);
    this.end.set(null);
    this.error.set(null);
    this.actionError.set(null);
    this.jobs.set({});
    this.kind.set('LEAVE');
    this.endDate.set('');
    this.targetRoomId.set(null);
    this.lastRent.set(null);
    this.startElectricity.set(null);
    this.startWater.set(null);
  }

  /** Runs a request that changes the server, with the busy flag and the server's message on failure. */
  private run<T>(request: Observable<T>, done: (value: T) => void): void {
    this.busy.set(true);
    this.actionError.set(null);
    const epoch = this.epoch;
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        if (epoch === this.epoch) done(value);
      },
      error: (err: unknown) => {
        this.busy.set(false);
        if (epoch === this.epoch) this.actionError.set(failure(err, 'Có lỗi xảy ra, thử lại sau.'));
      },
    });
  }

  private setJob(kind: MeterKind, job: ReadingJob): void {
    this.jobs.update((all) => ({ ...all, [kind]: job }));
  }

  private patchJob(kind: MeterKind, changes: Partial<ReadingJob>): void {
    this.jobs.update((all) => (all[kind] ? { ...all, [kind]: { ...all[kind], ...changes } } : all));
  }

  /** Reads the end state every POLL_MS while the AI reads a photo; gives up after POLL_MAX_MS and lets the landlord type the number. */
  private startPoller(): void {
    if (this.poller) return;
    const api = this.api;
    const id = this.tenancyId();
    if (!api || !id) return;
    const epoch = this.epoch;
    this.poller = timer(POLL_MS, POLL_MS)
      .pipe(exhaustMap(() => api.get<EndTenancyResponse>(`/tenancies/${id}/end`).pipe(catchError(() => of<EndTenancyResponse | null>(null)))))
      .subscribe((end) => {
        if (epoch !== this.epoch) return;
        if (end) this.end.set(end);
        const timedOut = Date.now() - this.pollStarted >= POLL_MAX_MS;
        let waiting = false;
        for (const kind of KINDS) {
          const r = kind === 'ELECTRICITY' ? end?.electricity : end?.water;
          const job = this.jobs()[kind];
          const isAnswered = !!r?.readingId && r.status !== 'PENDING_AI';
          if (job?.phase === 'reading') {
            if (isAnswered) this.discardJob(kind);
            else if (timedOut) this.patchJob(kind, { aiTimedOut: true });
            else waiting = true;
          } else if (!job && r?.status === 'PENDING_AI' && r.value == null && !timedOut) {
            waiting = true;
          }
        }
        if (!waiting) this.stopPoller();
      });
  }

  private stopPoller(): void {
    this.poller?.unsubscribe();
    this.poller = null;
  }
}

/** Start date of the latest locked version, YYYY-MM-DD. */
function startOf(t: TenancyDetailResponse | null): string | null {
  const locked = (t?.versions ?? []).find((v) => v.status === 'LOCKED');
  const value = locked?.terms?.['startDate']?.value;
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}/.test(value) ? value.slice(0, 10) : null;
}
