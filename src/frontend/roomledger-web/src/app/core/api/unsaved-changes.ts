import { DOCUMENT } from '@angular/common';
import { DestroyRef, Service, inject } from '@angular/core';

/**
 * Some saves are held back for an undo window (meter approvals, "đã thanh toán") or still on their way (photos).
 * Closing or reloading the tab then would lose them, and the page cannot finish an authenticated request while it
 * unloads (the Cognito ID token is only available asynchronously). So the browser asks before leaving while anything
 * is still held. Stores register a check here. Browsers that never ask (some phone browsers) skip this.
 */
@Service()
export class UnsavedChanges {
  private readonly checks = new Set<() => boolean>();

  constructor() {
    const win = inject(DOCUMENT).defaultView;
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!this.any()) return;
      event.preventDefault();
      // Some browsers only show the prompt when returnValue is set.
      event.returnValue = '';
    };
    win?.addEventListener('beforeunload', onBeforeUnload);
    inject(DestroyRef).onDestroy(() => win?.removeEventListener('beforeunload', onBeforeUnload));
  }

  /** `hasPending` says whether the caller still holds an unsent save. Returns the function that removes the check. */
  register(hasPending: () => boolean): () => void {
    this.checks.add(hasPending);
    return () => {
      this.checks.delete(hasPending);
    };
  }

  any(): boolean {
    for (const check of this.checks) if (check()) return true;
    return false;
  }
}
