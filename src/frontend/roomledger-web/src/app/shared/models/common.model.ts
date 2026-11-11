import { UserRole } from './enums';

/** Base interface mirroring AuditableEntity in C# */
export interface AuditableEntity {
  id: string; // UUID v7
  createdAt: string; // ISO DateTimeOffset
  updatedAt: string; // ISO DateTimeOffset
  createdBy?: string | null;
  updatedBy?: string | null;
}

export interface AppUser extends AuditableEntity {
  email: string;
  fullName: string;
  role: UserRole;
}

export interface AuditEvent {
  id: string;
  eventType: string;
  actorId?: string | null;
  targetType: string;
  targetId: string;
  detailJson?: Record<string, any> | null;
  traceId?: string | null;
  createdAt: string;
}
