import { DOCUMENT } from '@angular/common';
import { Service, inject, signal } from '@angular/core';

import { DEV_LANDLORD, DEV_TENANT, DevUser } from './dev-users';

const KEY = 'roomledger.devRole';

function readRole(): DevUser['group'] | null {
  try {
    const v = localStorage.getItem(KEY);
    return v === 'landlord' || v === 'tenant' ? v : null;
  } catch {
    return null;
  }
}

function toUser(role: DevUser['group'] | null): DevUser | null {
  return role === 'landlord' ? DEV_LANDLORD : role === 'tenant' ? DEV_TENANT : null;
}

/** Which sample person the app acts as in LocalDev mode. Chosen on the role picker, kept across reloads. */
@Service()
export class DevSession {
  private readonly win = inject(DOCUMENT).defaultView;
  readonly user = signal<DevUser | null>(toUser(readRole()));

  /**
   * Acts as this person from now on. With `target` the page is loaded afresh at that address: the stores are
   * singletons that load once, so they would otherwise keep showing the previous person's data.
   */
  signInAs(role: DevUser['group'], target?: string): void {
    this.user.set(toUser(role));
    try {
      localStorage.setItem(KEY, role);
    } catch {
      // Storage blocked (private window): the choice lasts until the page reloads.
    }
    if (target) this.win?.location.assign(target);
  }

  /** Forgets the chosen person and reloads the app at the role picker, with every cached store gone. */
  signOut(): void {
    this.user.set(null);
    try {
      localStorage.removeItem(KEY);
    } catch {
      // Storage blocked: the reload below still starts without a person if it was never stored.
    }
    this.win?.location.assign('/preview');
  }
}
