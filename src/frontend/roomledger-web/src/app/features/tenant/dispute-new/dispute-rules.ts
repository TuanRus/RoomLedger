/** Validation for TN-11 (PRODUCT.md: the tenant explains in ≥ 10 characters and proposes the correct value). */

const num = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });

export const REASON_MIN = 10;
export const REASON_MAX = 500;

/** Characters that count toward the minimum (surrounding spaces do not). */
export function reasonLength(value: string): number {
  return value.trim().length;
}

export function reasonError(value: string): string | null {
  const n = reasonLength(value);
  if (n === 0) return 'Viết lý do để chủ nhà hiểu bạn thấy gì';
  if (n < REASON_MIN) return `Cần ít nhất ${REASON_MIN} ký tự`;
  if (n > REASON_MAX) return `Tối đa ${REASON_MAX} ký tự`;
  return null;
}

/**
 * The value the tenant reads on the meter. Must be a whole number, not below last period's reading,
 * and different from what the landlord recorded (otherwise there is nothing to correct).
 */
export function proposedValueError(raw: string, previous: number, recorded: number): string | null {
  const v = raw.trim();
  if (!v) return 'Nhập chỉ số bạn thấy trên đồng hồ';
  if (!/^\d+$/.test(v)) return 'Chỉ nhập số nguyên, không có dấu chấm hay chữ';
  const n = Number(v);
  if (n < previous) return `Không thể nhỏ hơn chỉ số kỳ trước (${num.format(previous)})`;
  if (n === recorded) return 'Số này giống số chủ nhà đã ghi';
  return null;
}
