/**
 * Fixed sample people for the backend's LocalDev mode (header X-Dev-User), used while Cognito sign-in
 * is not merged. The seed script (src/backend/scripts/local-seed.mjs) creates data for the same people.
 * These are sample identities, not credentials: the header only works against an API running with
 * LocalDev__Enabled=true on your own machine.
 */
export interface DevUser {
  id: string;
  email: string;
  group: 'landlord' | 'tenant';
  name: string;
}

export const DEV_LANDLORD: DevUser = {
  id: '0193a000-0000-7000-8000-00000000a001',
  email: 'chunha.lan@example.com',
  group: 'landlord',
  name: 'Cô Lan',
};

export const DEV_TENANT: DevUser = {
  id: '0193a000-0000-7000-8000-00000000b204',
  email: 'minhanh@example.com',
  group: 'tenant',
  name: 'Trần Minh Anh',
};

/** sub|email|group|emailVerified|name, the name URL-encoded because headers must be ASCII. */
export function devUserHeader(user: DevUser): string {
  return [user.id, user.email, user.group, 'true', encodeURIComponent(user.name)].join('|');
}
