import { proposedValueError, reasonError, reasonLength } from './dispute-rules';
import { subjectFromReading } from './dispute-new.page';

describe('dispute rules', () => {
  it('needs at least 10 characters of reason, ignoring surrounding spaces', () => {
    expect(reasonError('')).toBe('Viết lý do để chủ nhà hiểu bạn thấy gì');
    expect(reasonError('Số sai')).toBe('Cần ít nhất 10 ký tự');
    expect(reasonError('   Số sai rồi   ')).toBeNull();
    expect(reasonLength('  abc  ')).toBe(3);
    expect(reasonError('x'.repeat(501))).toBe('Tối đa 500 ký tự');
  });

  it('checks the proposed reading', () => {
    expect(proposedValueError('', 61, 67)).toBe('Nhập chỉ số bạn thấy trên đồng hồ');
    expect(proposedValueError('6,5', 61, 67)).toContain('số nguyên');
    expect(proposedValueError('60', 61, 67)).toContain('kỳ trước (61)');
    expect(proposedValueError('1000', 1102, 1284)).toContain('(1.102)');
    expect(proposedValueError('67', 61, 67)).toBe('Số này giống số chủ nhà đã ghi');
    expect(proposedValueError(' 65 ', 61, 67)).toBeNull();
  });

  it('picks the subject from the reading id', () => {
    expect(subjectFromReading('r-204-ELECTRICITY-2026-11')).toBe('ELECTRICITY');
    expect(subjectFromReading('r-204-WATER-2026-11')).toBe('WATER');
    expect(subjectFromReading(undefined)).toBe('WATER');
  });
});
