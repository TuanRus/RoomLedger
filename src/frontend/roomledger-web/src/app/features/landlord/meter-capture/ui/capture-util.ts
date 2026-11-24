import { MeterKind } from '../../../../core/data/models';

const mbFormat = new Intl.NumberFormat('vi-VN', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** 3800000 -> "3,8". */
export function mb(bytes: number): string {
  return mbFormat.format(bytes / 1_000_000);
}

/** ISO date-time -> "08:03". */
export function hhmm(iso: string | null): string {
  return iso ? iso.slice(11, 16) : '—';
}

/** ISO date-time -> "08:02, 01/12". */
export function shortStamp(iso: string): string {
  return `${iso.slice(11, 16)}, ${iso.slice(8, 10)}/${iso.slice(5, 7)}`;
}

export const KIND_LABEL: Record<MeterKind, string> = { ELECTRICITY: 'điện', WATER: 'nước' };
export const KIND_UNIT: Record<MeterKind, string> = { ELECTRICITY: 'kWh', WATER: 'm³' };
export const KIND_METER: Record<MeterKind, string> = { ELECTRICITY: 'công tơ điện', WATER: 'đồng hồ nước' };

/** Takes the picked file from a file input and clears the input so the same file can be picked again. */
export function pickedFile(event: Event): File | null {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0] ?? null;
  input.value = '';
  return file;
}

/** "2026-11" -> "10/2026" (the period before). */
export function previousPeriod(period: string): string {
  const [y, m] = period.split('-').map(Number);
  return m === 1 ? `12/${y - 1}` : `${String(m - 1).padStart(2, '0')}/${y}`;
}
