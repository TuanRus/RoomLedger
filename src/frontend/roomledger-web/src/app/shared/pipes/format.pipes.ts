import { Pipe, PipeTransform } from '@angular/core';

const vnd = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });

/** 3245000 -> "3.245.000 đ" (PRODUCT.md money format). */
@Pipe({ name: 'vnd' })
export class VndPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return value == null ? '—' : `${vnd.format(value)} đ`;
  }
}

/** 1284 -> "1.284" (plain readings and usage, no unit). */
@Pipe({ name: 'num' })
export class NumPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return value == null ? '—' : vnd.format(value);
  }
}

/** "2026-11" -> "11/2026" (PRODUCT.md period format, used after "Kỳ"). */
@Pipe({ name: 'period' })
export class PeriodPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '—';
    const [year, month] = value.split('-');
    return `${month}/${year}`;
  }
}

/** ISO date-time -> "08:02, 01/12/2026" without depending on the browser locale. */
@Pipe({ name: 'stamp' })
export class StampPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '—';
    const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(value);
    return m ? `${m[4]}:${m[5]}, ${m[3]}/${m[2]}/${m[1]}` : value;
  }
}

/** "2026-12-10" or an ISO date-time -> "10/12/2026" ('full', default) or "10/12" ('short'), read from the string so the time zone cannot shift it. */
const WEEKDAYS = ['Chủ nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy'];

@Pipe({ name: 'day' })
export class DayPipe implements PipeTransform {
  transform(value: string | null | undefined, format: 'full' | 'short' | 'long' = 'full'): string {
    const m = value ? /^(\d{4})-(\d{2})-(\d{2})/.exec(value) : null;
    if (!m) return '—';
    if (format === 'short') return `${m[3]}/${m[2]}`;
    if (format === 'long') {
      // "Thứ Hai, 01/12/2026": spelled out so a date never depends on the browser's mm/dd or dd/mm order.
      const weekday = WEEKDAYS[new Date(Date.UTC(+m[1], +m[2] - 1, +m[3])).getUTCDay()];
      return `${weekday}, ${m[3]}/${m[2]}/${m[1]}`;
    }
    return `${m[3]}/${m[2]}/${m[1]}`;
  }
}
