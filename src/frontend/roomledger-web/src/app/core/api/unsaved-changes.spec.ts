import { TestBed } from '@angular/core/testing';

import { UnsavedChanges } from './unsaved-changes';

describe('UnsavedChanges', () => {
  it('stops the page from closing only while a registered store holds a save', () => {
    const guard = TestBed.inject(UnsavedChanges);
    let held = false;
    guard.register(() => held);

    const idle = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(idle);
    expect(idle.defaultPrevented).toBe(false);

    held = true;
    const busy = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(busy);
    expect(busy.defaultPrevented).toBe(true);
  });

  it('forgets a check that is unregistered, and its window listener when the app ends', () => {
    const guard = TestBed.inject(UnsavedChanges);
    const off = guard.register(() => true);
    expect(guard.any()).toBe(true);
    off();
    expect(guard.any()).toBe(false);

    guard.register(() => true);
    TestBed.resetTestingModule();
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
  });
});
