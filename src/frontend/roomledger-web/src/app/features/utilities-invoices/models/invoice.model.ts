import { AuditableEntity } from '../../../shared/models/common.model';
import {
  FileStatus,
  InvoiceStatus,
  MeterKind,
  MeterReadingStatus,
} from '../../../shared/models/enums';

export interface MeterReading extends AuditableEntity {
  roomId: string;
  tenancyId: string;
  period: string; // "YYYY-MM"
  kind: MeterKind;
  status: MeterReadingStatus;
  aiValue?: number | null;
  aiConfidence?: number | null; // C# decimal -> TS number
  aiRawJson?: Record<string, any> | null;
  value?: number | null;
  reviewReason?: string | null;
  meterReplaced: boolean;
  note?: string | null;
  editedByLandlord: boolean;
  confirmedBy?: string | null;
  confirmedAt?: string | null;
  photoFileName?: string | null;
  photoContentType?: string | null;
  photoSize?: number | null;
  photoSha256?: string | null;
  photoStatus?: FileStatus | null;
  photoKey?: string | null;
}

export interface Invoice extends AuditableEntity {
  tenancyId: string;
  period: string;
  status: InvoiceStatus;
  roomRent: number;
  electricityReadingId: string;
  electricityPrevious: number;
  electricityCurrent: number;
  electricityRate: number;
  electricityAmount: number;
  waterReadingId: string;
  waterPrevious: number;
  waterCurrent: number;
  waterRate: number;
  waterAmount: number;
  serviceFee: number;
  total: number;
  sentAt?: string | null;
  paidAt?: string | null;
}
