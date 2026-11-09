import { TenancyVersion } from '../models/contract.model';
import { InspectionStatus } from '../../../shared/models/enums';

export class TenancyUiRules {
  /** Equivalent to TenancyStateMachine.EnsureCanEditTerms */
  static canEditTerms(version: TenancyVersion): boolean {
    if (version.status === 'LOCKED') return false;
    return version.status === 'UPLOADED' || version.status === 'TERMS_REVIEWED';
  }

  /** Equivalent to TenancyStateMachine.EnsureCanConfirm */
  static canConfirm(version: TenancyVersion): boolean {
    return version.status !== 'LOCKED' && version.status === 'AWAITING_CONFIRM';
  }

  /** Equivalent to InspectionStateMachine.EnsureCanChangePhotos */
  static canChangePhotos(status: InspectionStatus): boolean {
    return status === 'OPEN' || status === 'AWAITING_CONFIRM';
  }
}
