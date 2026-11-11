export const ErrorCodes = {
  ValidationError: 'VALIDATION_ERROR',
  InvalidEmail: 'INVALID_EMAIL',
  InvalidFile: 'INVALID_FILE',
  InvalidPeriod: 'INVALID_PERIOD',
  Forbidden: 'FORBIDDEN',
  InviteEmailMismatch: 'INVITE_EMAIL_MISMATCH',
  NotFound: 'NOT_FOUND',
  RecordLocked: 'RECORD_LOCKED',
  InvalidState: 'INVALID_STATE',
  VersionConflict: 'VERSION_CONFLICT',
  RoomCodeExists: 'ROOM_CODE_EXISTS',
  RoomOccupied: 'ROOM_OCCUPIED',
  IncompleteInspection: 'INCOMPLETE_INSPECTION',
  PeriodClosed: 'PERIOD_CLOSED',
  InviteInvalid: 'INVITE_INVALID',
  Unauthorized: 'UNAUTHORIZED',
  InternalError: 'INTERNAL_ERROR',
} as const;

export type ErrorCodeType = (typeof ErrorCodes)[keyof typeof ErrorCodes];
