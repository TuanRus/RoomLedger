import { AuditableEntity } from '../../../shared/models/common.model';
import {
  CaptureSource,
  FileStatus,
  InspectionStatus,
  InspectionType,
  UserRole,
} from '../../../shared/models/enums';

export interface InspectionArea {
  roomAreaId: string;
  name: string;
  minPhotos: number;
  sortOrder: number;
}

export interface Inspection extends AuditableEntity {
  tenancyId: string;
  type: InspectionType;
  status: InspectionStatus;
  landlordConfirmedAt?: string | null;
  tenantConfirmedAt?: string | null;
  areaSnapshotJson?: InspectionArea[] | null; // Deserialized from C# jsonb string
}

export interface Photo extends AuditableEntity {
  inspectionId: string;
  roomAreaId: string;
  uploadedBy: string;
  uploaderRole: UserRole;
  fileName: string;
  contentType: string;
  size: number; // C# long -> TS number
  sha256: string;
  status: FileStatus;
  incomingKey: string;
  derivedKey?: string | null;
  evidenceKey?: string | null;
  objectVersionId?: string | null;
  exifTakenAt?: string | null;
  oldPhotoWarning: boolean;
  captureSource: CaptureSource;
}
