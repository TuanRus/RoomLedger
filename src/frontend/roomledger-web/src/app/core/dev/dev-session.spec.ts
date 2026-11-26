import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';

import { DevSession } from './dev-session';

describe('DevSession', () => {
  const assign = vi.fn();

  beforeEach(() => {
    assign.mockClear();
    localStorage.removeItem('roomledger.devRole');
    TestBed.configureTestingModule({ providers: [{ provide: DOCUMENT, useValue: { defaultView: { location: { assign } } } }] });
  });

  afterEach(() => localStorage.removeItem('roomledger.devRole'));

  it('switching person loads the page afresh, so no store keeps the previous person data', () => {
    const session = TestBed.inject(DevSession);
    session.signInAs('landlord', '/landlord/overview');
    expect(session.user()?.group).toBe('landlord');
    expect(localStorage.getItem('roomledger.devRole')).toBe('landlord');
    expect(assign).toHaveBeenCalledWith('/landlord/overview');
  });

  it('does not reload when no target is given', () => {
    TestBed.inject(DevSession).signInAs('tenant');
    expect(assign).not.toHaveBeenCalled();
  });

  it('signing out forgets the person and reloads at the role picker', () => {
    const session = TestBed.inject(DevSession);
    session.signInAs('tenant');
    session.signOut();
    expect(session.user()).toBeNull();
    expect(localStorage.getItem('roomledger.devRole')).toBeNull();
    expect(assign).toHaveBeenCalledWith('/preview');
  });
});
