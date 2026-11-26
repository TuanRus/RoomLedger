import { components } from '../../../generated/api-types';

type Schemas = components['schemas'];

/**
 * LL-07 contracts. Shapes follow TenancyDetailResponse / TenancyVersionResponse, flattened for the UI.
 *
 * API gaps (2026-11-24):
 * - TenancyDetailResponse has no tenancy start/end summary, landlord name or created/uploaded date per version
 *   (only landlordConfirmedAt / tenantConfirmedAt / lockedAt); "rejectedAt" is missing for REJECTED versions.
 * - CreateTenancyVersionRequest.type is ADDENDUM | RENEWAL: no type for a corrected re-upload of a rejected INITIAL.
 * - No endpoint to send a draft version to the tenant (landlord "confirm" / status UPLOADED -> AWAITING_CONFIRM).
 * - TenancyFileResponse has no page count and no thumbnail URL.
 */
export type VersionStatus = Schemas['TenancyVersionStatus'];
export type VersionType = Schemas['TenancyVersionType'];
export type TenancyStatus = Schemas['TenancyStatus'];
/** Terms the landlord types from the scan: same keys as UpdateTermsRequest. */
export type ContractTerms = Schemas['UpdateTermsRequest'];

export interface ContractFile {
  fileId: string;
  fileName: string;
  contentType: string;
  size: number;
  url: string;
  /** Object URL of a file picked in this session (not uploaded yet). */
  local: boolean;
}

export interface ContractVersion {
  /** Id of the version on the server (API only); needed to attach scan files. */
  versionId?: string;
  number: number;
  type: VersionType;
  status: VersionStatus;
  /** "YYYY-MM-DD". Not in the API (null there). */
  uploadedAt: string | null;
  /** Short note, e.g. "sửa tiền cọc". */
  note: string | null;
  sentAt: string | null;
  tenantRespondedAt: string | null;
  rejectReason: string | null;
  terms: ContractTerms;
  files: ContractFile[];
}

export interface Contract {
  tenancyId: string;
  roomId: string;
  roomCode: string;
  tenantName: string;
  status: TenancyStatus;
  /** Oldest first. */
  versions: ContractVersion[];
}

/** A room with no tenancy yet has a placeholder contract with this id prefix; the first upload creates the tenancy. */
export const NEW_PREFIX = 'new:';
export const hasNoTenancy = (c: Contract): boolean => c.tenancyId.startsWith(NEW_PREFIX);

export type Tone = 'ok' | 'warn' | 'err' | 'info' | 'neu';
export interface ChipSpec { tone: Tone; icon: string; label: string }

export const isDraft = (v: ContractVersion | undefined): boolean => v?.status === 'UPLOADED' || v?.status === 'TERMS_REVIEWED';

export function latest(c: Contract): ContractVersion | undefined {
  return c.versions[c.versions.length - 1];
}

/** The version the tenant answered or is answering (the newest one that is not a draft). */
export function lastSent(c: Contract): ContractVersion | undefined {
  return [...c.versions].reverse().find((v) => !isDraft(v));
}

/** Status of a whole contract in the list and page header. */
export function contractChip(c: Contract): ChipSpec {
  const v = latest(c);
  if (!v) return { tone: 'neu', icon: 'i-doc', label: 'Chưa có bản scan' };
  if (v.status === 'LOCKED') return { tone: 'ok', icon: 'i-check', label: 'Đã xác nhận' };
  if (v.status === 'AWAITING_CONFIRM') return { tone: 'warn', icon: 'i-clock', label: 'Chờ người thuê xác nhận' };
  if (v.status === 'REJECTED' || lastSent(c)?.status === 'REJECTED') return { tone: 'err', icon: 'i-flag', label: 'Người thuê báo không đúng' };
  return { tone: 'neu', icon: 'i-edit', label: 'Nháp' };
}

/** Status of one version in the history. */
export function versionChip(status: VersionStatus): ChipSpec {
  switch (status) {
    case 'LOCKED': return { tone: 'ok', icon: 'i-lock', label: 'Đã xác nhận' };
    case 'AWAITING_CONFIRM': return { tone: 'warn', icon: 'i-clock', label: 'Chờ xác nhận' };
    case 'REJECTED': return { tone: 'err', icon: 'i-x', label: 'Bị từ chối' };
    default: return { tone: 'neu', icon: 'i-edit', label: 'Nháp' };
  }
}

/** The tenant's answer to the last version sent. */
export function tenantChip(v: ContractVersion | undefined): ChipSpec {
  if (!v) return { tone: 'neu', icon: 'i-clock', label: 'Chưa nhận' };
  if (v.status === 'LOCKED') return { tone: 'ok', icon: 'i-check', label: 'Đã xác nhận' };
  if (v.status === 'REJECTED') return { tone: 'err', icon: 'i-flag', label: 'Không đúng' };
  return { tone: 'warn', icon: 'i-clock', label: 'Chờ xác nhận' };
}

/** "2026-11-18" -> "18/11/2026". */
export function ddmmyyyy(iso: string | null | undefined): string {
  if (!iso) return '—';
  const [y, m, d] = iso.slice(0, 10).split('-');
  return `${d}/${m}/${y}`;
}

/** Local calendar date "YYYY-MM-DD". */
export function isoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Last day of a term: start 2026-06-01 + 12 months -> 2027-05-31. */
export function endDateFor(startIso: string, months: number): string {
  const [y, m, d] = startIso.split('-').map(Number);
  return isoDate(new Date(y, m - 1 + months, d - 1));
}

/** Whole months from start to the day after end; 2026-06-01..2027-05-31 -> 12. */
export function termMonths(startIso: string, endIso: string): number {
  const [y1, m1, d1] = startIso.split('-').map(Number);
  const [y2, m2, d2] = endIso.split('-').map(Number);
  const after = new Date(y2, m2 - 1, d2 + 1);
  return (after.getFullYear() - y1) * 12 + (after.getMonth() - (m1 - 1)) - (after.getDate() < d1 ? 1 : 0);
}

/** 1_887_436 -> "1,8 MB"; 240_000 -> "234 KB". */
export function fileSize(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

export const SCAN_TYPES = ['application/pdf', 'image/jpeg', 'image/png', 'image/heic', 'image/heif'];
export const SCAN_MAX_BYTES = 20 * 1024 * 1024;

/** Error for a picked scan file, or null when it can be uploaded. */
export function scanFileError(file: { name: string; type: string; size: number }): string | null {
  const type = file.type || (/\.hei[cf]$/i.test(file.name) ? 'image/heic' : '');
  if (!SCAN_TYPES.includes(type)) return `${file.name}: chỉ nhận PDF hoặc ảnh JPG, PNG, HEIC.`;
  if (file.size > SCAN_MAX_BYTES) return `${file.name}: lớn hơn 20 MB.`;
  if (file.size === 0) return `${file.name}: file rỗng.`;
  return null;
}
