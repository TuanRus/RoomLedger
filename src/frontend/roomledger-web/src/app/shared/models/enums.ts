export type AiStatus = 'PENDING' | 'SUCCEEDED' | 'FAILED';

export type CaptureSource = 'CAMERA' | 'GALLERY';

export type ConfirmDecision = 'CONFIRM' | 'REJECT';

export type DisputeResolution = 'CORRECTED' | 'KEPT';

export type DisputeStatus = 'OPEN' | 'RESOLVED';

export type DisputeTargetType = 'METER_READING' | 'FINDING';

export type FileStatus = 'PENDING_UPLOAD' | 'RECEIVED' | 'READY' | 'SEALED';

export type InspectionStatus =
  'OPEN' | 'AWAITING_CONFIRM' | 'LOCKED' | 'COMPARING' | 'REVIEWING' | 'SETTLED';

export type InspectionType = 'MOVE_IN' | 'MOVE_OUT';

export type InvoiceStatus = 'DRAFT' | 'SENT' | 'PAID' | 'DISPUTED';

export type MeterKind = 'ELECTRICITY' | 'WATER';

export type MeterReadingStatus = 'PENDING_AI' | 'NEEDS_REVIEW' | 'PENDING_CONFIRM' | 'CONFIRMED';

export type RoomDisplayStatus = 'VACANT' | 'AWAITING_CONFIRM' | 'ACTIVE' | 'MOVING_OUT';

export type RoomStatus = 'VACANT' | 'OCCUPIED';

export type TenancyStatus = 'DRAFT' | 'AWAITING_TENANT' | 'ACTIVE' | 'MOVING_OUT' | 'SETTLED';

export type TenancyVersionStatus =
  'UPLOADED' | 'TERMS_REVIEWED' | 'AWAITING_CONFIRM' | 'LOCKED' | 'REJECTED';

export type TenancyVersionType = 'INITIAL' | 'ADDENDUM' | 'RENEWAL';

export type UploadTargetType = 'INSPECTION' | 'TENANCY_VERSION' | 'METER_READING';

export type UserRole = 'LANDLORD' | 'TENANT' | 'ADDED' | 'ADMIN';
