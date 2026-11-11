import { AuditableEntity } from '../../../shared/models/common.model';
import {
  AiStatus,
  FileStatus,
  TenancyStatus,
  TenancyVersionStatus,
  TenancyVersionType,
} from '../../../shared/models/enums';

export interface TenancyFile extends AuditableEntity {
  tenancyVersionId: string;
  fileName: string;
  contentType: string;
  size: number; // C# long -> TS number
  sha256: string;
  status: FileStatus;
  incomingKey: string;
  evidenceKey?: string | null;
  objectVersionId?: string | null;
}

export interface TenancyVersion extends AuditableEntity {
  tenancyId: string;
  number: number;
  type: TenancyVersionType;
  status: TenancyVersionStatus;
  aiStatus: AiStatus;
  aiTermsJson?: Record<string, any> | null; // C# string (jsonb) -> TS object/record
  monthlyRent?: number | null;
  deposit?: number | null;
  startDate?: string | null; // C# DateOnly -> TS string (YYYY-MM-DD)
  endDate?: string | null; // C# DateOnly -> TS string (YYYY-MM-DD)
  electricityRate?: number | null;
  waterRate?: number | null;
  editedFields: string[];
  landlordConfirmedAt?: string | null;
  tenantConfirmedAt?: string | null;
  lockedAt?: string | null;
  rejectReason?: string | null;
  files?: TenancyFile[];
}

export interface Tenancy extends AuditableEntity {
  roomId: string;
  landlordId: string;
  tenantId?: string | null;
  status: TenancyStatus;
  versions?: TenancyVersion[];
}

export interface Invite extends AuditableEntity {
  tenancyId: string;
  tokenHash: string;
  email: string;
  expiresAt: string;
  usedAt?: string | null;
  usedBy?: string | null;
  revokedAt?: string | null;
}
