/**
 * LL-06 pricing, billing-cycle and bank-transfer settings.
 *
 * API gaps (2026-11-24): PropertyResponse only has electricityRate, waterRate and serviceFee.
 * Not in the contract yet: reading days, payment due day, reading reminder, per-room floor area,
 * the period a rent applies from, the period a price change applies from, and all bank-transfer
 * fields (bank, account number, holder, transfer note template, QR image).
 */

export interface RoomRent {
  roomId: string;
  roomCode: string;
  /** Floor area in m². Not in RoomResponse (its `areas` are inspection areas). */
  areaM2: number;
  monthlyRent: number;
  /** Period the current rent applies from, "YYYY-MM". */
  since: string;
}

export interface BankTransferSettings {
  /** Show transfer details on invoices. Optional for the landlord. */
  enabled: boolean;
  bankName: string;
  accountNumber: string;
  accountHolder: string;
  /** e.g. "P[phòng] T[tháng]" -> "P204 T11". */
  transferNoteTemplate: string;
  qrImageUrl: string | null;
}

export interface PricingSettings {
  electricityRate: number;
  waterRate: number;
  /** First of the three reading days, e.g. 1 -> "Ngày 1–3 hằng tháng". */
  readingStartDay: number;
  dueDay: number;
  remindReading: boolean;
  rents: RoomRent[];
  payment: BankTransferSettings;
}

/** Form model: money is kept as the text the landlord types ("3.800"), days as select values. */
export interface SettingsForm {
  electricityRate: string;
  waterRate: string;
  readingStartDay: string;
  dueDay: string;
  remindReading: boolean;
  rents: { roomId: string; rent: string }[];
  bankEnabled: boolean;
  bankName: string;
  accountNumber: string;
  accountHolder: string;
  transferNoteTemplate: string;
}

export const READING_WINDOW_DAYS = 3;

const groups = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });

/** 3800 -> "3.800". */
export function formatMoney(value: number): string {
  return groups.format(value);
}

/** "3.800", "3800", "3 800 đ" -> 3800. Null when empty or not a whole number. */
export function parseMoney(text: string): number | null {
  const cleaned = text.replace(/[\s.đ]/gi, '');
  if (!/^\d+$/.test(cleaned)) return null;
  return Number(cleaned);
}

/** Validation message for a money field, or null when valid. */
export function moneyError(text: string, label: string, min: number, max: number): string | null {
  if (text.trim() === '') return `Nhập ${label.toLowerCase()}.`;
  const value = parseMoney(text);
  if (value == null) return `${label} chỉ gồm chữ số, ví dụ ${formatMoney(min * 2)}.`;
  if (value < min) return `${label} tối thiểu ${formatMoney(min)} đ.`;
  if (value > max) return `${label} tối đa ${formatMoney(max)} đ.`;
  return null;
}

/** The due day must fall after the reading window so invoices exist before payment is due. */
export function dueDayError(readingStartDay: number, dueDay: number): string | null {
  const lastReadingDay = readingStartDay + READING_WINDOW_DAYS - 1;
  return dueDay > lastReadingDay ? null : `Hạn thanh toán phải sau ngày ghi chỉ số (sau ngày ${lastReadingDay}).`;
}

export function accountNumberError(text: string): string | null {
  const digits = text.replace(/\s/g, '');
  if (digits === '') return 'Nhập số tài khoản.';
  if (!/^\d+$/.test(digits)) return 'Số tài khoản chỉ gồm chữ số.';
  if (digits.length < 6 || digits.length > 19) return 'Số tài khoản có từ 6 đến 19 chữ số.';
  return null;
}

export function transferNoteError(template: string): string | null {
  const t = template.trim();
  if (t === '') return 'Nhập nội dung chuyển khoản.';
  if (!/\[phòng\]/i.test(t)) return 'Thêm [phòng] để bạn biết tiền của phòng nào.';
  if (transferNoteExample(t, '204', 11).length > 40) return 'Nội dung tối đa 40 ký tự sau khi điền phòng và tháng.';
  return null;
}

/** "P[phòng] T[tháng]" with room "204", month 11 -> "P204 T11". */
export function transferNoteExample(template: string, room: string, month: number): string {
  return template.trim().replace(/\[phòng\]/gi, room).replace(/\[tháng\]/gi, String(month));
}

/** "P.204" -> "204" (room code inside a transfer note). */
export function roomNumber(roomCode: string): string {
  return roomCode.replace(/^P\./i, '');
}

export function toForm(s: PricingSettings): SettingsForm {
  return {
    electricityRate: formatMoney(s.electricityRate),
    waterRate: formatMoney(s.waterRate),
    readingStartDay: String(s.readingStartDay),
    dueDay: String(s.dueDay),
    remindReading: s.remindReading,
    rents: s.rents.map((r) => ({ roomId: r.roomId, rent: formatMoney(r.monthlyRent) })),
    bankEnabled: s.payment.enabled,
    bankName: s.payment.bankName,
    accountNumber: s.payment.accountNumber,
    accountHolder: s.payment.accountHolder,
    transferNoteTemplate: s.payment.transferNoteTemplate,
  };
}

/** Converts a valid form back to settings. Rents keep their order and room data from `current`. */
export function fromForm(f: SettingsForm, current: PricingSettings, qrImageUrl: string | null): PricingSettings {
  const rentOf = new Map(f.rents.map((r) => [r.roomId, parseMoney(r.rent) ?? 0]));
  return {
    electricityRate: parseMoney(f.electricityRate) ?? current.electricityRate,
    waterRate: parseMoney(f.waterRate) ?? current.waterRate,
    readingStartDay: Number(f.readingStartDay),
    dueDay: Number(f.dueDay),
    remindReading: f.remindReading,
    rents: current.rents.map((r) => ({ ...r, monthlyRent: rentOf.get(r.roomId) ?? r.monthlyRent })),
    payment: {
      enabled: f.bankEnabled,
      bankName: f.bankName,
      accountNumber: f.accountNumber.trim(),
      accountHolder: f.accountHolder.trim().toUpperCase(),
      transferNoteTemplate: f.transferNoteTemplate.trim(),
      qrImageUrl,
    },
  };
}

/** Compares forms by value; money text "3800" equals "3.800". */
export function sameForm(a: SettingsForm, b: SettingsForm): boolean {
  const norm = (f: SettingsForm) =>
    JSON.stringify({
      ...f,
      electricityRate: parseMoney(f.electricityRate) ?? f.electricityRate,
      waterRate: parseMoney(f.waterRate) ?? f.waterRate,
      rents: f.rents.map((r) => parseMoney(r.rent) ?? r.rent),
    });
  return norm(a) === norm(b);
}
