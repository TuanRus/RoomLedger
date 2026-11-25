import { homeStateOf } from './home-samples';

describe('homeStateOf', () => {
  it('accepts known states and falls back to approved', () => {
    expect(homeStateOf('moving')).toBe('moving');
    expect(homeStateOf('unassigned')).toBe('unassigned');
    expect(homeStateOf('bogus')).toBe('approved');
    expect(homeStateOf(undefined)).toBe('approved');
  });
});
