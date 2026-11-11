import { AuditableEntity } from '../../../shared/models/common.model';
import { DisputeResolution, DisputeStatus, DisputeTargetType } from '../../../shared/models/enums';

export interface Dispute extends AuditableEntity {
  tenancyId: string;
  targetType: DisputeTargetType;
  targetId: string;
  invoiceId?: string | null;
  raisedBy: string;
  reason: string;
  status: DisputeStatus;
  resolution?: DisputeResolution | null;
  response?: string | null;
  resolvedAt?: string | null;
}
