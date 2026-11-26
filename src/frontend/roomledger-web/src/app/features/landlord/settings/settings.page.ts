import { DOCUMENT, NgOptimizedImage } from '@angular/common';
import { Component, DestroyRef, ElementRef, computed, inject, linkedSignal, signal } from '@angular/core';
import { FieldTree, FormField, apply, form, schema, submit, validate } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';

import { Viewport } from '../../../core/layout/viewport';
import { PeriodPipe, VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { LoadState } from '../shared/load-state';
import {
  PricingSettings,
  READING_WINDOW_DAYS,
  SettingsForm,
  accountNumberError,
  apiTransferNoteError,
  apiTransferNoteExample,
  bankFieldsMissing,
  dueDayError,
  formatMoney,
  fromForm,
  moneyError,
  parseMoney,
  roomNumber,
  sameForm,
  toForm,
  transferNoteError,
  transferNoteExample,
} from './settings.model';
import { QR_MAX_BYTES } from '../shared/upload';
import { BANKS, SettingsStore } from './settings.store';

const QR_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
const SAMPLE_QR_MAX_BYTES = 5 * 1024 * 1024;

const err = (message: string | null) => (message ? { kind: 'invalid', message } : undefined);

/**
 * Sample mode: bank details are optional behind a switch. API mode: no switch; the three fields go together
 * (all empty = no details), the template is optional and uses {room} {month} {year} {period}.
 */
const settingsSchemaFor = (api: boolean) =>
  schema<SettingsForm>((p) => {
    validate(p.electricityRate, ({ value }) => err(moneyError(value(), 'Giá điện', 500, 20_000)));
    validate(p.waterRate, ({ value }) => err(moneyError(value(), 'Giá nước', 1_000, 200_000)));
    validate(p.serviceFee, ({ value }) => err(moneyError(value(), 'Phí dịch vụ', 0, 5_000_000)));
    validate(p.dueDay, ({ value, valueOf }) => err(dueDayError(Number(valueOf(p.readingStartDay)), Number(value()))));
    const need = (bank: string, account: string, holder: string, enabled: boolean) =>
      api ? bankFieldsMissing({ bankName: bank, accountNumber: account, accountHolder: holder }) || account.trim() !== '' : enabled;
    validate(p.bankName, ({ value, valueOf }) =>
      err(need(value(), valueOf(p.accountNumber), valueOf(p.accountHolder), valueOf(p.bankEnabled)) && !value() ? 'Chọn ngân hàng.' : null),
    );
    validate(p.accountNumber, ({ value, valueOf }) =>
      err(need(valueOf(p.bankName), value(), valueOf(p.accountHolder), valueOf(p.bankEnabled)) ? accountNumberError(value()) : null),
    );
    validate(p.accountHolder, ({ value, valueOf }) => {
      if (!need(valueOf(p.bankName), valueOf(p.accountNumber), value(), valueOf(p.bankEnabled))) return undefined;
      const v = value().trim();
      const max = api ? 100 : 50;
      return err(v === '' ? 'Nhập tên chủ tài khoản.' : v.length > max ? `Tên tối đa ${max} ký tự.` : null);
    });
    validate(p.transferNoteTemplate, ({ value, valueOf }) =>
      err(api ? apiTransferNoteError(value()) : valueOf(p.bankEnabled) ? transferNoteError(value()) : null),
    );
  });

/** Sample edits so the page opens in the LL-06 state (giá điện 3.500 -> 3.800, P.209 2.600.000 chưa lưu). */
function withSampleEdits(f: SettingsForm): SettingsForm {
  return {
    ...f,
    electricityRate: '3.800',
    rents: f.rents.map((r) => (r.roomId === 'r-209' ? { ...r, rent: '2.600.000' } : r)),
  };
}

/** LL-06: electricity/water prices, billing cycle, per-room rent and optional bank-transfer details. */
@Component({
  selector: 'app-settings-page',
  imports: [NgOptimizedImage, FormField, Icon, LoadState, PeriodPipe, VndPipe],
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss',
})
export class SettingsPage {
  protected readonly store = inject(SettingsStore);
  protected readonly viewport = inject(Viewport);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly window = inject(DOCUMENT).defaultView;

  protected readonly banks = BANKS;
  protected readonly readingDays = Array.from({ length: 26 }, (_, i) => i + 1);
  protected readonly dueDays = Array.from({ length: 28 }, (_, i) => i + 1);
  protected readonly windowEnd = READING_WINDOW_DAYS - 1;

  /** The form follows the saved settings (the API's arrive after the page opens); the sample opens with its demo edits once. */
  protected readonly model = linkedSignal<PricingSettings, SettingsForm>({
    source: this.store.settings,
    computation: (saved, previous) => (this.store.sample && !previous ? withSampleEdits(toForm(saved)) : toForm(saved)),
  });
  protected readonly f = form(this.model, (p) => {
    apply(p, settingsSchemaFor(!this.store.sample));
  });

  private readonly savedForm = computed(() => toForm(this.store.settings()));
  protected readonly rooms = computed(() => this.store.settings().rents);

  /** QR image: saved URL, or a local preview (blob:) until saved. */
  private readonly sampleQr = signal<string | null>(this.store.settings().payment.qrImageUrl);
  /** API: the QR the server holds (changes at once on upload and remove, not with Lưu thay đổi). */
  protected readonly qrUrl = computed(() => (this.store.sample ? this.sampleQr() : this.store.settings().payment.qrImageUrl));
  private readonly localError = signal<string | null>(null);
  protected readonly qrError = computed(() => this.localError() ?? this.store.qrError());
  protected readonly qrIsLocal = computed(() => this.qrUrl()?.startsWith('blob:') ?? false);
  protected readonly qrBusy = this.store.qrProgress;
  /** API: banks to pick from, plus the saved one when it is not in the list. */
  protected readonly bankOptions = computed(() => {
    const saved = this.store.settings().payment.bankName;
    const list = BANKS.slice(1);
    return saved && !list.includes(saved) ? [saved, ...list] : list;
  });

  protected readonly status = signal<'idle' | 'saved' | 'invalid'>('idle');
  protected readonly savedAt = signal('');
  protected readonly saving = signal(false);

  protected readonly dirty = computed(
    () => !sameForm(this.model(), this.savedForm()) || (this.store.sample && this.sampleQr() !== this.store.settings().payment.qrImageUrl),
  );

  protected readonly changedRooms = computed(() => {
    const saved = new Map(this.savedForm().rents.map((r) => [r.roomId, parseMoney(r.rent)]));
    return new Set(this.model().rents.filter((r) => parseMoney(r.rent) !== saved.get(r.roomId)).map((r) => r.roomId));
  });

  /** "Đổi giá điện từ 3.500 đ lên 3.800 đ/kWh" for each price that changed and is valid. */
  protected readonly priceChanges = computed(() => {
    const s = this.store.settings();
    const m = this.model();
    const out: string[] = [];
    const e = parseMoney(m.electricityRate);
    const w = parseMoney(m.waterRate);
    const sf = parseMoney(m.serviceFee);
    if (e != null && e !== s.electricityRate) out.push(`giá điện từ ${formatMoney(s.electricityRate)} đ ${e > s.electricityRate ? 'lên' : 'xuống'} ${formatMoney(e)} đ/kWh`);
    if (w != null && w !== s.waterRate) out.push(`giá nước từ ${formatMoney(s.waterRate)} đ ${w > s.waterRate ? 'lên' : 'xuống'} ${formatMoney(w)} đ/m³`);
    if (sf != null && sf !== s.serviceFee) out.push(`phí dịch vụ từ ${formatMoney(s.serviceFee)} đ ${sf > s.serviceFee ? 'lên' : 'xuống'} ${formatMoney(sf)} đ/tháng`);
    return out;
  });

  protected readonly noteExample = computed(() => {
    const template = this.model().transferNoteTemplate;
    if (this.store.sample) return transferNoteExample(template, '204', 11);
    return apiTransferNoteExample(template, this.rooms()[0]?.roomCode ?? 'P.204', this.store.period());
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.revokeQr());
  }

  protected errOf<T>(field: FieldTree<T>): string | null {
    const s = field();
    return s.touched() && s.invalid() ? (s.errors()[0]?.message ?? 'Giá trị không hợp lệ.') : null;
  }

  protected rentField(index: number): FieldTree<string> {
    return this.f.rents[index].rent;
  }

  protected roomNo(code: string): string {
    return roomNumber(code);
  }

  protected onQrFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (!QR_TYPES.includes(file.type)) {
      this.localError.set('Chọn ảnh PNG, JPG hoặc WEBP.');
      return;
    }
    if (file.size > (this.store.sample ? SAMPLE_QR_MAX_BYTES : QR_MAX_BYTES)) {
      this.localError.set(`Ảnh QR tối đa ${this.store.sample ? 5 : 2} MB.`);
      return;
    }
    this.localError.set(null);
    if (this.store.sample) {
      this.revokeQr();
      this.sampleQr.set(URL.createObjectURL(file));
      this.status.set('idle');
    } else {
      this.store.uploadQr(file).subscribe({ error: () => undefined });
    }
  }

  protected removeQr(): void {
    this.localError.set(null);
    if (this.store.sample) {
      this.revokeQr();
      this.sampleQr.set(null);
    } else {
      this.store.removeQr().subscribe({ error: () => undefined });
    }
  }

  protected async save(): Promise<void> {
    if (this.saving()) return;
    this.saving.set(true);
    let cancelled = false;
    const ok = await submit(this.f, async () => {
      const next = fromForm(this.model(), this.store.settings(), this.qrUrl());
      // Asked only once the form is valid, so confirming always leads to a save.
      if (
        !this.store.sample &&
        this.store.clearsBank(next) &&
        !this.window?.confirm('Xoá thông tin chuyển khoản và ảnh QR? Hóa đơn của người thuê sẽ không còn hiện thông tin chuyển khoản.')
      ) {
        cancelled = true;
        return undefined;
      }
      if (this.store.sample) this.store.save(next);
      else await firstValueFrom(this.store.saveApi(next)).catch(() => undefined);
      return undefined;
    });
    this.saving.set(false);
    if (cancelled) return;
    if (ok && !this.store.saveError()) {
      this.model.set(toForm(this.store.settings()));
      this.f().reset();
      this.savedAt.set(nowHm());
      this.status.set('saved');
    } else {
      this.status.set('invalid');
      queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
    }
  }

  protected cancel(): void {
    this.revokeQr();
    this.model.set(toForm(this.store.settings()));
    this.sampleQr.set(this.store.settings().payment.qrImageUrl);
    this.localError.set(null);
    this.f().reset();
    this.status.set('idle');
  }

  private revokeQr(): void {
    const url = this.sampleQr();
    // A saved local preview stays in use by the store until the API stores the file.
    if (url?.startsWith('blob:') && url !== this.store.settings().payment.qrImageUrl) URL.revokeObjectURL(url);
  }
}

function nowHm(): string {
  const d = new Date();
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}
