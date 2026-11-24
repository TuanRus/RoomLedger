import type { components } from '../../../generated/api-types';

type InvitePreviewResponse = components['schemas']['InvitePreviewResponse'];

/**
 * InvitePreviewResponse plus what TN-01 shows that the contract does not carry yet (see report / API gaps):
 * landlord name and email, tenancy start date, when the invite was sent, and the full invited email
 * (the contract only has emailMasked; the email is shown locked, so the masked one would also work).
 */
export interface InvitePreview extends Required<InvitePreviewResponse> {
  landlordName: string;
  landlordShortName: string;
  landlordEmail: string;
  startDate: string;
  sentAt: string;
  email: string;
}

export type InviteState = { kind: 'valid'; invite: InvitePreview } | { kind: 'expired'; invite: InvitePreview } | { kind: 'invalid' };

/** Sample invite: sent 22/02/2026 14:00, valid 72 hours (PRODUCT.md). */
export const SAMPLE_INVITE: InvitePreview = {
  propertyName: 'Nhà trọ Cô Lan',
  roomCode: 'P.204',
  emailMasked: 'm*****h@vidu.vn',
  email: 'minhanh@vidu.vn',
  expiresAt: '2026-02-25T14:00:00+07:00',
  sentAt: '2026-02-22T14:00:00+07:00',
  landlordName: 'Lê Thị Lan',
  landlordShortName: 'Cô Lan',
  landlordEmail: 'lan.chunha@vidu.vn',
  startDate: '2026-03-01',
};

/** Stands in for GET /invites/{token} (404 → invalid, 410 → expired). */
export function previewInvite(token: string): InviteState {
  if (token === 'invalid') return { kind: 'invalid' };
  if (token === 'expired') return { kind: 'expired', invite: SAMPLE_INVITE };
  return { kind: 'valid', invite: SAMPLE_INVITE };
}

/** "2026-03-01" -> "01/03/2026". */
export function dmy(isoDate: string): string {
  return isoDate.slice(0, 10).split('-').reverse().join('/');
}
