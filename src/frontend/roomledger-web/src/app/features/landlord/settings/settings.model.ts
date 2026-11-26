/**
 * LL-06 pricing, billing-cycle and bank-transfer settings.
 *
 * API (2026-11-26): prices (PATCH /properties/{id}) and bank-transfer details with QR (payment-info) are real.
 * Still not in the contract: reading days, payment due day, reading reminder, per-room floor area,
 * the period a rent applies from and the period a price change applies from.
 */

export interface RoomRent {
  roomId: string;
  roomCode: string;
  /** Floor area in m². Not in RoomResponse (its `areas` are inspection areas): null with the API. */
  areaM2: number | null;
  monthlyRent: number;
  /** Period the current rent applies from, "YYYY-MM". Null with the API. */
  since: string | null;
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
  /** Monthly service fee in VND, may be 0. */
  serviceFee: number;
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
  serviceFee: string;
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
  if (digits.length < 6 || digits.length > 20) return 'Số tài khoản có từ 6 đến 20 chữ số.';
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
    serviceFee: formatMoney(s.serviceFee),
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
    serviceFee: parseMoney(f.serviceFee) ?? current.serviceFee,
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
      serviceFee: parseMoney(f.serviceFee) ?? f.serviceFee,
      rents: f.rents.map((r) => parseMoney(r.rent) ?? r.rent),
    });
  return norm(a) === norm(b);
}

/** Template the API uses when none is set. */
export const API_DEFAULT_TEMPLATE = '{room} T{month}';
const API_PLACEHOLDERS = ['{room}', '{month}', '{year}', '{period}'];

/** API mode: the template is optional (empty = default), at most 100 characters, only {room} {month} {year} {period}. */
export function apiTransferNoteError(template: string): string | null {
  const t = template.trim();
  if (t.length > 100) return 'Nội dung tối đa 100 ký tự.';
  const rest = API_PLACEHOLDERS.reduce((s, p) => s.split(p).join(''), t);
  if (rest.includes('{') || rest.includes('}')) return 'Chỉ dùng {room}, {month}, {year}, {period}.';
  return null;
}

/** Same filling as the API: "{room} T{month}" with "P.204", "2026-11" -> "P204 T11". */
export function apiTransferNoteExample(template: string, roomCode: string, period: string): string {
  const room = roomCode.replace(/[.\s]/g, '');
  return (template.trim() || API_DEFAULT_TEMPLATE)
    .split('{room}').join(room)
    .split('{month}').join(period.slice(5, 7))
    .split('{year}').join(period.slice(0, 4))
    .split('{period}').join(period);
}

/** API mode: the three bank fields go together; all empty means no bank details. */
export function bankFieldsMissing(f: Pick<SettingsForm, 'bankName' | 'accountNumber' | 'accountHolder'>): boolean {
  const filled = [f.bankName, f.accountNumber, f.accountHolder].filter((v) => v.trim() !== '').length;
  return filled > 0 && filled < 3;
}
export function bankFieldsEmpty(f: Pick<SettingsForm, 'bankName' | 'accountNumber' | 'accountHolder'>): boolean {
  return [f.bankName, f.accountNumber, f.accountHolder].every((v) => v.trim() === '');
}
