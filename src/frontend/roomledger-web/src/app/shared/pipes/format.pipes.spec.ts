import { NumPipe, PeriodPipe, StampPipe, VndPipe } from './format.pipes';

describe('format pipes (PRODUCT.md formats)', () => {
  it('formats money as "3.245.000 đ"', () => {
    expect(new VndPipe().transform(3_245_000)).toBe('3.245.000 đ');
    expect(new VndPipe().transform(null)).toBe('—');
  });

  it('formats readings with dot thousands', () => {
    expect(new NumPipe().transform(2988)).toBe('2.988');
  });

  it('formats a period as MM/yyyy', () => {
    expect(new PeriodPipe().transform('2026-11')).toBe('11/2026');
  });

  it('formats a timestamp as HH:mm, dd/MM/yyyy', () => {
    expect(new StampPipe().transform('2026-12-01T08:14:00+07:00')).toBe('08:14, 01/12/2026');
  });
});
