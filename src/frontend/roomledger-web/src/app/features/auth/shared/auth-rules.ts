/** Client-side checks shared by the sign-in, forgot-password and invite screens (PRODUCT.md / Cognito password policy). */

export interface PasswordCheck {
  /** At least 8 characters. */
  length: boolean;
  /** Has both an upper-case and a lower-case letter. */
  mixedCase: boolean;
  /** Has at least one digit. */
  digit: boolean;
}

export function checkPassword(value: string): PasswordCheck {
  return {
    length: value.length >= 8,
    mixedCase: /\p{Lu}/u.test(value) && /\p{Ll}/u.test(value),
    digit: /\d/.test(value),
  };
}

export function isStrongPassword(value: string): boolean {
  const c = checkPassword(value);
  return c.length && c.mixedCase && c.digit;
}

/** Same rule as the browser's type="email" check, kept simple on purpose. */
export function isEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
}

/** "minhanh@vidu.vn" -> "m•••••h@vidu.vn" (shows the user where the code went without exposing the address). */
export function maskEmail(value: string): string {
  const [name, domain] = value.trim().split('@');
  if (!name || !domain) return value;
  if (name.length <= 2) return `${name[0]}•••@${domain}`;
  return `${name[0]}•••••${name[name.length - 1]}@${domain}`;
}

/**
 * TEMPORARY sign-in stub until Kiru's Cognito integration lands (src/app/core/auth).
 * The real role comes from the users pool ("custom:role" / group); here a landlord is anyone whose email
 * contains "lan" or "chunha", e.g. lan.chunha@vidu.vn.
 * TODO(Kiru): replace with the Cognito SRP sign-in and route by the role claim.
 */
export function stubRoleFor(email: string): 'landlord' | 'tenant' {
  const e = email.toLowerCase();
  return e.includes('lan') || e.includes('chunha') ? 'landlord' : 'tenant';
}
