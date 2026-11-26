import { HttpClient } from '@angular/common/http';
import { DestroyRef, Service, computed, inject, signal } from '@angular/core';
import { Observable, Subscription, concatMap, finalize, from, last, map, mergeMap, of, switchMap, tap, throwError, toArray } from 'rxjs';

import type { components } from '../../../generated/api-types';
import { injectApi } from '../../../core/api/api-client';
import { LandlordPropertyStore, RoomResponse } from '../../../core/data/landlord-property.store';
import { MeStore } from '../../../core/data/me.store';
import { putPresigned, scanType, sha256Base64 } from '../shared/upload';
import {
  Contract,
  ContractFile,
  ContractTerms,
  ContractVersion,
  NEW_PREFIX,
  VersionStatus,
  hasNoTenancy,
  isDraft,
  isoDate,
  latest,
} from './contracts.model';

const SCAN = 'sample/contract-p1.jpg';
const sampleFiles = (id: string): ContractFile[] => [
  { fileId: `${id}-p1`, fileName: 'hop-dong-trang-1.jpg', contentType: 'image/jpeg', size: 942_000, url: SCAN, local: false },
  { fileId: `${id}-p2`, fileName: 'hop-dong-trang-2.jpg', contentType: 'image/jpeg', size: 945_000, url: SCAN, local: false },
];

const terms = (monthlyRent: number, deposit: number, startDate: string, endDate: string): ContractTerms => ({
  monthlyRent, deposit, startDate, endDate, electricityRate: 3_500, waterRate: 18_000,
});

function locked(id: string, roomCode: string, tenantName: string, t: ContractTerms, uploadedAt: string, confirmedAt: string): Contract {
  return {
    tenancyId: id, roomId: id.replace('t-', 'r-'), roomCode, tenantName, status: 'ACTIVE',
    versions: [{ number: 1, type: 'INITIAL', status: 'LOCKED', uploadedAt, note: null, sentAt: uploadedAt, tenantRespondedAt: confirmedAt, rejectReason: null, terms: t, files: sampleFiles(id) }],
  };
}

/** Sample contracts, same story as the design mockups (Nhà trọ Cô Lan, 19/11/2026). Names are samples. */
const SAMPLE_CONTRACTS: Contract[] = [
  {
    tenancyId: 't-305', roomId: 'r-305', roomCode: 'P.305', tenantName: 'Phạm Quốc Huy', status: 'AWAITING_TENANT',
    versions: [
      {
        number: 1, type: 'INITIAL', status: 'REJECTED', uploadedAt: '2026-11-18', note: null, sentAt: '2026-11-18', tenantRespondedAt: '2026-11-19',
        rejectReason: 'Tiền cọc ghi 3.000.000 đ nhưng tôi đã đóng 2.500.000 đ, có biên nhận.',
        terms: terms(2_800_000, 3_000_000, '2026-06-01', '2027-05-31'), files: sampleFiles('t-305-v1'),
      },
      {
        number: 2, type: 'INITIAL', status: 'UPLOADED', uploadedAt: '2026-11-19', note: 'sửa tiền cọc', sentAt: null, tenantRespondedAt: null, rejectReason: null,
        terms: terms(2_800_000, 2_500_000, '2026-06-01', '2027-05-31'), files: sampleFiles('t-305-v2'),
      },
    ],
  },
  {
    tenancyId: 't-209', roomId: 'r-209', roomCode: 'P.209', tenantName: 'Ngô Thanh Tâm', status: 'AWAITING_TENANT',
    versions: [{ number: 1, type: 'INITIAL', status: 'AWAITING_CONFIRM', uploadedAt: '2026-11-17', note: null, sentAt: '2026-11-17', tenantRespondedAt: null, rejectReason: null, terms: terms(2_500_000, 3_000_000, '2026-11-15', '2027-11-14'), files: sampleFiles('t-209') }],
  },
  locked('t-101', 'P.101', 'Đặng Văn Long', terms(2_500_000, 2_500_000, '2026-03-01', '2027-02-28'), '2026-02-26', '2026-02-27'),
  locked('t-102', 'P.102', 'Mai Thị Hồng', terms(2_500_000, 2_500_000, '2026-03-01', '2027-02-28'), '2026-02-26', '2026-02-28'),
  locked('t-201', 'P.201', 'Nguyễn Hữu Phước', terms(2_700_000, 2_700_000, '2026-01-01', '2026-12-31'), '2025-12-28', '2025-12-29'),
  locked('t-204', 'P.204', 'Trần Minh Anh', terms(2_500_000, 2_500_000, '2026-03-01', '2027-02-28'), '2026-02-25', '2026-02-26'),
];

type TenancyDetailResponse = components['schemas']['TenancyDetailResponse'];
type TenancyVersionResponse = components['schemas']['TenancyVersionResponse'];
type UploadUrlResponse = components['schemas']['UploadUrlResponse'];
type RoomDetailResponse = components['schemas']['RoomDetailResponse'];

/** The presigned file links last 5 minutes (TenancyFileResponse.url); a contract older than this is read again. */
const LINK_MAX_AGE_MS = 4 * 60_000;
const CONCURRENCY = 6;

const asNumber = (v: unknown): number | null => (typeof v === 'number' ? v : typeof v === 'string' && /^\d+$/.test(v) ? Number(v) : null);
const asDate = (v: unknown): string | null => (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}/.test(v) ? v.slice(0, 10) : null);

/** What a term shows when the server has no value yet: the room's rent and the property's rates. */
export interface TermDefaults {
  monthlyRent: number;
  electricityRate: number;
  waterRate: number;
}

/** A version's terms (saved values, or what the AI read). Unknown dates and deposit stay empty. */
export function termsOf(v: TenancyVersionResponse, d: TermDefaults): ContractTerms {
  const t = v.terms ?? {};
  return {
    monthlyRent: asNumber(t['monthlyRent']?.value) ?? d.monthlyRent,
    deposit: asNumber(t['deposit']?.value) ?? 0,
    startDate: asDate(t['startDate']?.value) ?? '',
    endDate: asDate(t['endDate']?.value) ?? '',
    electricityRate: asNumber(t['electricityRate']?.value) ?? d.electricityRate,
    waterRate: asNumber(t['waterRate']?.value) ?? d.waterRate,
  };
}

/** TenancyDetailResponse (newest version first) -> the contract, oldest version first. */
export function toContract(t: TenancyDetailResponse, d: TermDefaults): Contract {
  const oldestFirst = [...(t.versions ?? [])].reverse();
  const versions: ContractVersion[] = oldestFirst.map((v, i) => {
    const terms = termsOf(v, d);
    const before = i > 0 ? termsOf(oldestFirst[i - 1], d) : null;
    return {
      versionId: v.versionId,
      number: v.number ?? i + 1,
      type: v.type ?? 'INITIAL',
      status: v.status ?? 'UPLOADED',
      uploadedAt: null,
      note: before ? changedNote(before, terms) : null,
      sentAt: v.landlordConfirmedAt ?? null,
      tenantRespondedAt: v.tenantConfirmedAt ?? null,
      rejectReason: v.rejectReason ?? null,
      terms,
      files: (v.files ?? []).map((f) => ({
        fileId: f.fileId ?? '',
        fileName: f.fileName ?? '',
        contentType: f.contentType ?? '',
        size: f.size ?? 0,
        url: f.url ?? '',
        local: false,
      })),
    };
  });
  return {
    tenancyId: t.tenancyId ?? '',
    roomId: t.roomId ?? '',
    roomCode: t.roomCode ?? '',
    tenantName: t.tenantName ?? '',
    status: t.status ?? 'DRAFT',
    versions,
  };
}

/** A room that has no tenancy yet: listed so its first scan can be uploaded. */
export function emptyContract(roomId: string, roomCode: string): Contract {
  return { tenancyId: NEW_PREFIX + roomId, roomId, roomCode, tenantName: '', status: 'DRAFT', versions: [] };
}

/**
 * LL-07 contracts of one property. Sample data by default; with environment.dataSource 'api' there is no list
 * endpoint, so each room is read (GET /rooms/{id}, then GET /tenancies/{id}): a request or two per room, so it only
 * happens when the contracts page opens (refreshIfStale), not when the app starts. Uploading a scan is POST /uploads
 * (after POST /rooms/{id}/tenancies or POST /tenancies/{id}/versions when there is no draft version), and
 * "Gửi" saves the terms with PATCH /tenancies/{id}/versions/{n}/terms.
 */
@Service()
export class ContractsStore {
  private readonly api = injectApi();
  private readonly http = inject(HttpClient);
  private readonly property = inject(LandlordPropertyStore);
  private readonly me = inject(MeStore);

  /** True with sample data. */
  readonly sample = !this.api;
  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>(this.api ? 'idle' : 'ready');
  readonly error = signal<string | null>(null);
  readonly uploading = signal(false);
  readonly sending = signal(false);

  private readonly data = signal<Contract[]>(this.api ? [] : SAMPLE_CONTRACTS);
  readonly contracts = this.data.asReadonly();
  readonly landlordName = computed(() => (this.api ? (this.me.me()?.fullName ?? '') : 'Lê Thị Lan'));
  readonly landlordInitials = computed(() => (this.api ? this.me.initials() : 'LL'));

  private readonly loadedAt = new Map<string, number>();
  private seenRevision = -1;
  private loading: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.loading?.unsubscribe());
  }

  byId(id: string | null): Contract | undefined {
    return this.contracts().find((c) => c.tenancyId === id);
  }

  /** (Re)loads every room's contract from the API; does nothing with sample data. A load still running is cancelled. */
  load(): void {
    const api = this.api;
    if (!api) return;
    this.loading?.unsubscribe();
    this.status.set('loading');
    this.error.set(null);
    this.loading = this.property
      .loadedRooms()
      .pipe(
        switchMap(() => {
          this.seenRevision = this.property.roomsRevision();
          const rooms = this.property.rooms();
          return from(rooms).pipe(
            mergeMap((room) => this.readRoom(room), CONCURRENCY),
            toArray(),
          );
        }),
      )
      .subscribe({
        next: (list) => {
          this.data.set(list.sort((a, b) => a.roomCode.localeCompare(b.roomCode, 'vi', { numeric: true })));
          this.status.set('ready');
        },
        error: (err: unknown) => {
          this.error.set(err instanceof Error ? err.message : 'Không tải được hợp đồng.');
          this.status.set('error');
        },
      });
  }

  /** Reads the contracts when the page opens: the first time, and again when the room list changed since (an invite created a tenancy). */
  refreshIfStale(): void {
    if (this.api && this.status() !== 'loading' && (this.status() === 'idle' || this.status() === 'error' || this.seenRevision !== this.property.roomsRevision())) this.load();
  }

  /** Reads one contract again when its file links are about to expire. */
  refreshLinks(tenancyId: string): void {
    const at = this.loadedAt.get(tenancyId);
    if (this.api && at != null && Date.now() - at > LINK_MAX_AGE_MS) this.reloadTenancy(tenancyId).subscribe({ error: () => undefined });
  }

  /**
   * Sample data: adds scanned pages. A draft version gets the new pages in place of its old ones; otherwise a new
   * draft version is created with the terms of the latest version, for the landlord to check before sending.
   */
  uploadScan(tenancyId: string, files: ContractFile[], today = isoDate(new Date())): void {
    this.update(tenancyId, (c) => {
      const last = latest(c);
      if (last && isDraft(last)) {
        return { ...c, versions: [...c.versions.slice(0, -1), { ...last, files, uploadedAt: today }] };
      }
      const next: ContractVersion = {
        number: (last?.number ?? 0) + 1,
        type: !last || last.status === 'REJECTED' ? 'INITIAL' : 'ADDENDUM',
        status: 'UPLOADED', uploadedAt: today, note: null, sentAt: null, tenantRespondedAt: null, rejectReason: null,
        terms: last ? { ...last.terms } : { monthlyRent: 0, deposit: 0, startDate: today, endDate: today, electricityRate: 3_500, waterRate: 18_000 },
        files,
      };
      return { ...c, versions: [...c.versions, next] };
    });
  }

  /**
   * API: attaches the picked files to the draft version of the contract. Creates the tenancy for a room without one,
   * and a new version when the latest is not a draft. The server only adds files; it cannot remove old pages of a draft.
   */
  uploadScanByApi(c: Contract, files: File[]): Observable<Contract> {
    const api = this.api;
    if (!api) return throwError(() => new Error('uploadScanByApi needs the API'));
    this.uploading.set(true);
    const detail$ = hasNoTenancy(c)
      ? api.post<TenancyDetailResponse>(`/rooms/${c.roomId}/tenancies`)
      : api.get<TenancyDetailResponse>(`/tenancies/${c.tenancyId}`);
    return detail$.pipe(
      switchMap((t) => {
        const newest = t.versions?.[0];
        const draft = newest && (newest.status === 'UPLOADED' || newest.status === 'TERMS_REVIEWED');
        // After a rejection the server starts the corrected version with the same type; ADDENDUM is only the required field.
        const version$ = draft
          ? of(newest.versionId)
          : api.post<TenancyVersionResponse>(`/tenancies/${t.tenancyId}/versions`, { type: 'ADDENDUM' }).pipe(map((v) => v.versionId));
        return version$.pipe(map((versionId) => ({ tenancyId: t.tenancyId ?? '', versionId: versionId ?? '' })));
      }),
      switchMap(({ tenancyId, versionId }) =>
        from(files).pipe(
          concatMap((file) => this.uploadFile(versionId, file)),
          toArray(),
          map(() => tenancyId),
        ),
      ),
      switchMap((tenancyId) => this.reloadTenancy(tenancyId, hasNoTenancy(c) ? c.tenancyId : undefined)),
      finalize(() => this.uploading.set(false)),
    );
  }

  /** Sample data: saves the draft terms and sends the draft to the tenant: status "Chờ người thuê xác nhận". */
  send(tenancyId: string, t: ContractTerms, today = isoDate(new Date())): void {
    this.update(tenancyId, (c) => {
      const last = latest(c);
      if (!last || !isDraft(last) || last.files.length === 0) return c;
      const prev = c.versions[c.versions.length - 2];
      const note = last.note ?? (prev ? changedNote(prev.terms, t) : null);
      return {
        ...c,
        status: c.status === 'ACTIVE' ? 'ACTIVE' : 'AWAITING_TENANT',
        versions: [...c.versions.slice(0, -1), { ...last, terms: t, note, status: 'AWAITING_CONFIRM', sentAt: today }],
      };
    });
  }

  /**
   * API: PATCH the terms of the draft version. The server makes it AWAITING_CONFIRM when a tenant is linked, or
   * TERMS_REVIEWED when there is none yet. Emits the new status of the version.
   */
  sendByApi(c: Contract, t: ContractTerms): Observable<VersionStatus> {
    const api = this.api;
    const last = latest(c);
    if (!api || !last) return throwError(() => new Error('Chưa có phiên bản để gửi.'));
    this.sending.set(true);
    return api.patch<TenancyVersionResponse>(`/tenancies/${c.tenancyId}/versions/${last.number}/terms`, t).pipe(
      switchMap((v) => this.reloadTenancy(c.tenancyId).pipe(map(() => v.status ?? 'TERMS_REVIEWED'))),
      finalize(() => this.sending.set(false)),
    );
  }

  /** Reads one tenancy again and puts it in the list (in place of the room's placeholder, if that is what `replacing` names). */
  private reloadTenancy(tenancyId: string, replacing?: string): Observable<Contract> {
    const api = this.api!;
    return api.get<TenancyDetailResponse>(`/tenancies/${tenancyId}`).pipe(
      map((t) => {
        const room = this.property.roomById().get(t.roomId ?? '');
        return toContract(t, this.defaultsOf(room));
      }),
      tap((fresh) => {
        this.loadedAt.set(fresh.tenancyId, Date.now());
        this.data.update((list) => {
          const key = replacing ?? fresh.tenancyId;
          return list.some((c) => c.tenancyId === key) ? list.map((c) => (c.tenancyId === key ? fresh : c)) : [...list, fresh];
        });
      }),
    );
  }

  private readRoom(room: RoomResponse): Observable<Contract> {
    const api = this.api!;
    const roomId = room.roomId ?? '';
    return api.get<RoomDetailResponse>(`/rooms/${roomId}`).pipe(
      switchMap((d) => {
        const id = d.currentTenancy?.tenancyId;
        return id ? api.get<TenancyDetailResponse>(`/tenancies/${id}`).pipe(map((t) => toContract(t, this.defaultsOf(room)))) : of(emptyContract(roomId, room.code ?? ''));
      }),
      tap((c) => this.loadedAt.set(c.tenancyId, Date.now())),
    );
  }

  private uploadFile(versionId: string, file: File): Observable<number> {
    const api = this.api!;
    const type = scanType(file);
    if (!type) return throwError(() => new Error(`${file.name}: chỉ nhận PDF hoặc ảnh JPG, PNG, HEIC.`));
    return from(sha256Base64(file)).pipe(
      switchMap((sha256) =>
        api.post<UploadUrlResponse>('/uploads', { targetType: 'TENANCY_VERSION', targetId: versionId, fileName: file.name, contentType: type, size: file.size, sha256 }),
      ),
      switchMap((upload) => putPresigned(this.http, upload, file)),
      last(),
    );
  }

  private defaultsOf(room: RoomResponse | undefined): TermDefaults {
    const p = this.property.property();
    return { monthlyRent: room?.monthlyRent ?? 0, electricityRate: p?.electricityRate ?? 0, waterRate: p?.waterRate ?? 0 };
  }

  private update(tenancyId: string, fn: (c: Contract) => Contract): void {
    this.data.update((list) => list.map((c) => (c.tenancyId === tenancyId ? fn(c) : c)));
  }
}

const TERM_NAMES: Record<keyof ContractTerms, string> = {
  monthlyRent: 'tiền phòng', deposit: 'tiền cọc', startDate: 'ngày bắt đầu', endDate: 'thời hạn', electricityRate: 'giá điện', waterRate: 'giá nước',
};

/** "sửa tiền cọc" from the terms that differ from the previous version. */
export function changedNote(before: ContractTerms, after: ContractTerms): string | null {
  const keys = (Object.keys(TERM_NAMES) as (keyof ContractTerms)[]).filter((k) => before[k] !== after[k]);
  return keys.length ? `sửa ${keys.map((k) => TERM_NAMES[k]).join(', ')}` : null;
}
