import { Service, computed, effect, inject, signal, untracked } from '@angular/core';

import { injectApi } from '../../../core/api/api-client';
import { TenantHomeStore } from '../../../core/data/tenant-home.store';
import type { components } from '../../../generated/api-types';

type TenancyDetailResponse = components['schemas']['TenancyDetailResponse'];
type TenancyVersionResponse = components['schemas']['TenancyVersionResponse'];

/**
 * The tenant's contract (TN-10). Sample data by default; in API mode GET /tenancies/{id} gives the
 * version with its terms and files, and POST /tenancies/{id}/versions/{v}/confirm sends CONFIRM or REJECT + reason.
 */
export interface TenantContract {
  roomCode: string;
  propertyName: string;
  /** First image of the scan; empty when the version has no image file. */
  scanUrl: string;
  /** Number of files of the version. */
  pages: number;
  monthlyRent: number;
  deposit: number;
  startDate: string;
  endDate: string;
  electricityRate: number;
  waterRate: number;
}

export type ContractDecision =
  | { status: 'NONE' }
  | { status: 'AWAITING_CONFIRM' }
  | { status: 'CONFIRMED'; at: string | null }
  | { status: 'REJECTED'; at: string | null; reason: string };

export const REASON_MIN = 10;

/** Today as dd/MM/yyyy, for the sample confirmation stamp. */
function today(): string {
  const d = new Date();
  return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
}

/** "2026-08-01" or an ISO date-time -> "01/08/2026". */
export function dmyOf(iso: string | null | undefined): string | null {
  const m = iso ? /^(\d{4})-(\d{2})-(\d{2})/.exec(iso) : null;
  return m ? `${m[3]}/${m[2]}/${m[1]}` : null;
}

function termNumber(v: TenancyVersionResponse, key: string): number {
  const value = v.terms?.[key]?.value;
  return typeof value === 'number' ? value : Number(value ?? 0) || 0;
}

function termDate(v: TenancyVersionResponse, key: string): string {
  const value = v.terms?.[key]?.value;
  return typeof value === 'string' ? (dmyOf(value) ?? '—') : '—';
}

/** The version the tenant looks at: the one waiting for them, else the locked one, else the newest (versions come newest first). */
export function pickVersion(versions: TenancyVersionResponse[]): TenancyVersionResponse | null {
  return versions.find((v) => v.status === 'AWAITING_CONFIRM') ?? versions.find((v) => v.status === 'LOCKED') ?? versions[0] ?? null;
}

export function toDecision(v: TenancyVersionResponse): ContractDecision {
  if (v.status === 'AWAITING_CONFIRM') return { status: 'AWAITING_CONFIRM' };
  if (v.status === 'LOCKED') return { status: 'CONFIRMED', at: dmyOf(v.tenantConfirmedAt) };
  if (v.status === 'REJECTED') return { status: 'REJECTED', at: null, reason: v.rejectReason ?? '' };
  return { status: 'NONE' };
}

/** TenancyDetailResponse -> what the contract screen shows; null when the tenancy has no version yet. */
export function toTenantContract(t: TenancyDetailResponse): { contract: TenantContract; decision: ContractDecision; versionNumber: number } | null {
  const v = pickVersion(t.versions ?? []);
  if (!v) return null;
  const files = v.files ?? [];
  return {
    versionNumber: v.number ?? 1,
    decision: toDecision(v),
    contract: {
      roomCode: t.roomCode ?? '',
      propertyName: t.propertyName ?? '',
      scanUrl: files.find((f) => f.contentType?.startsWith('image/') && f.url)?.url ?? '',
      pages: files.length,
      monthlyRent: termNumber(v, 'monthlyRent'),
      deposit: termNumber(v, 'deposit'),
      startDate: termDate(v, 'startDate'),
      endDate: termDate(v, 'endDate'),
      electricityRate: termNumber(v, 'electricityRate'),
      waterRate: termNumber(v, 'waterRate'),
    },
  };
}

@Service()
export class TenantContractStore {
  private readonly api = injectApi();
  private readonly home = inject(TenantHomeStore);

  /** True with sample data: demo controls (?sample=confirmed, "Xem lại") may show. */
  readonly sample = !this.api;
  /** Null in API mode until loaded, or when the room has no contract yet. */
  readonly contract = signal<TenantContract | null>(
    this.api
      ? null
      : {
          roomCode: 'P.204',
          propertyName: 'Nhà trọ Cô Lan',
          scanUrl: 'sample/contract-p1.jpg',
          pages: 2,
          monthlyRent: 2_500_000,
          deposit: 3_000_000,
          startDate: '01/03/2026',
          endDate: '28/02/2027',
          electricityRate: 3_500,
          waterRate: 18_000,
        },
  );

  readonly decision = signal<ContractDecision>(this.api ? { status: 'NONE' } : { status: 'AWAITING_CONFIRM' });

  private readonly fetch = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  private readonly fetchError = signal<string | null>(null);
  private readonly versionNumber = signal<number | null>(null);
  /** A confirm or reject request is running. */
  readonly busy = signal(false);
  /** Why the last confirm / reject failed (Vietnamese, from the API). */
  readonly actionError = signal<string | null>(null);

  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = computed<'idle' | 'loading' | 'ready' | 'error'>(() => {
    if (!this.api) return 'ready';
    const home = this.home.status();
    if (home === 'error') return 'error';
    if (home !== 'ready') return 'loading';
    return this.home.tenancyId() ? this.fetch() : 'ready';
  });
  readonly error = computed(() => (this.home.status() === 'error' ? this.home.error() : this.fetchError()));

  constructor() {
    if (this.api) {
      effect(() => {
        if (this.home.tenancyId()) untracked(() => this.load());
      });
    }
  }

  /** (Re)loads the contract of the tenant's room; does nothing with sample data. */
  load(): void {
    const api = this.api;
    if (!api) return;
    if (this.home.status() === 'error') {
      this.home.reload();
      return;
    }
    const tenancyId = this.home.tenancyId();
    if (!tenancyId) return;
    this.fetch.set('loading');
    this.fetchError.set(null);
    api.get<TenancyDetailResponse>(`/tenancies/${tenancyId}`).subscribe({
      next: (t) => {
        const mapped = toTenantContract(t);
        this.contract.set(mapped?.contract ?? null);
        this.decision.set(mapped?.decision ?? { status: 'NONE' });
        this.versionNumber.set(mapped?.versionNumber ?? null);
        this.fetch.set('ready');
      },
      error: (err: unknown) => {
        this.fetchError.set(err instanceof Error ? err.message : 'Không tải được hợp đồng.');
        this.fetch.set('error');
      },
    });
  }

  confirm(): void {
    if (this.api) return this.send({ decision: 'CONFIRM' });
    this.decision.set({ status: 'CONFIRMED', at: today() });
  }

  /** Returns false when the reason is too short (BR: at least 10 characters). */
  reject(reason: string): boolean {
    const text = reason.trim();
    if (text.length < REASON_MIN) return false;
    if (this.api) this.send({ decision: 'REJECT', reason: text });
    else this.decision.set({ status: 'REJECTED', at: today(), reason: text });
    return true;
  }

  private send(body: { decision: 'CONFIRM' | 'REJECT'; reason?: string }): void {
    const api = this.api;
    const tenancyId = this.home.tenancyId();
    const number = this.versionNumber();
    if (!api || !tenancyId || number == null) return;
    this.busy.set(true);
    this.actionError.set(null);
    api.post<TenancyVersionResponse>(`/tenancies/${tenancyId}/versions/${number}/confirm`, body).subscribe({
      next: (v) => {
        this.decision.set(toDecision(v));
        this.busy.set(false);
      },
      error: (err: unknown) => {
        this.actionError.set(err instanceof Error ? err.message : 'Chưa gửi được, thử lại sau.');
        this.busy.set(false);
      },
    });
  }

  /** Sample switch for ?sample=confirmed: the contract as confirmed on move-in day. */
  showConfirmed(): void {
    if (this.api) return;
    this.decision.set({ status: 'CONFIRMED', at: '01/03/2026' });
  }

  reset(): void {
    if (this.api) return;
    this.decision.set({ status: 'AWAITING_CONFIRM' });
  }
}
