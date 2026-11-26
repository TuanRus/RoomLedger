import { Signal } from '@angular/core';

/** Nav badge counts keyed by nav path: only things that are waiting on this person. */
export type NavBadgeCounts = Signal<Record<string, number>>;

/** What the sidebar shows about the place and the person. */
export interface ShellIdentity {
  place: { title: string; sub: string };
  person: { initials: string; name: string; role: string };
}
