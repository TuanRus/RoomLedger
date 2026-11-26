import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, linkedSignal, signal } from '@angular/core';
import { FieldTree, FormField, form, schema, submit, validate } from '@angular/forms/signals';

import { Viewport } from '../../../core/layout/viewport';
import { VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { LoadState } from '../shared/load-state';
import { formatMoney, moneyError, parseMoney } from '../settings/settings.model';
import {
  Contract,
  ContractFile,
  ContractTerms,
  contractChip,
  ddmmyyyy,
  endDateFor,
  fileSize,
  hasNoTenancy,
  isDraft,
  lastSent,
  latest,
  scanFileError,
  tenantChip,
  termMonths,
  versionChip,
} from './contracts.model';
import { ContractsStore } from './contracts.store';

interface TermsForm {
  monthlyRent: string;
  deposit: string;
  startDate: string;
  months: string;
  electricityRate: string;
  waterRate: string;
}

const TERM_OPTIONS = [3, 6, 9, 12, 18, 24, 36];
const err = (message: string | null) => (message ? { kind: 'invalid', message } : undefined);

const termsSchema = schema<TermsForm>((p) => {
  validate(p.monthlyRent, ({ value }) => err(moneyError(value(), 'Tiền phòng', 100_000, 50_000_000)));
  validate(p.deposit, ({ value }) => err(moneyError(value(), 'Tiền cọc', 0, 200_000_000)));
  validate(p.startDate, ({ value }) => err(/^\d{4}-\d{2}-\d{2}$/.test(value()) ? null : 'Chọn ngày bắt đầu.'));
  validate(p.electricityRate, ({ value }) => err(moneyError(value(), 'Giá điện', 500, 20_000)));
  validate(p.waterRate, ({ value }) => err(moneyError(value(), 'Giá nước', 1_000, 200_000)));
});

function toTermsForm(t: ContractTerms): TermsForm {
  const months = termMonths(t.startDate, t.endDate);
  return {
    monthlyRent: formatMoney(t.monthlyRent),
    deposit: formatMoney(t.deposit),
    startDate: t.startDate,
    months: String(TERM_OPTIONS.includes(months) ? months : 12),
    electricityRate: formatMoney(t.electricityRate),
    waterRate: formatMoney(t.waterRate),
  };
}

function fromTermsForm(f: TermsForm): ContractTerms {
  return {
    monthlyRent: parseMoney(f.monthlyRent) ?? 0,
    deposit: parseMoney(f.deposit) ?? 0,
    startDate: f.startDate,
    endDate: endDateFor(f.startDate, Number(f.months)),
    electricityRate: parseMoney(f.electricityRate) ?? 0,
    waterRate: parseMoney(f.waterRate) ?? 0,
  };
}

/** LL-07: contracts per room, scanned pages, terms the landlord typed from the scan, versions and tenant answers. */
@Component({
  selector: 'app-contracts-page',
  imports: [NgOptimizedImage, NgTemplateOutlet, FormField, Icon, LoadState, VndPipe],
  templateUrl: './contracts.page.html',
  styleUrl: './contracts.page.scss',
})
export class ContractsPage {
  protected readonly store = inject(ContractsStore);
  protected readonly viewport = inject(Viewport);

  protected readonly termOptions = TERM_OPTIONS;
  protected readonly contractChip = contractChip;
  protected readonly versionChip = versionChip;
  protected readonly ddmmyyyy = ddmmyyyy;
  protected readonly fileSize = fileSize;
  protected readonly isDraft = isDraft;

  /**
   * Phone opens on the list; computer opens the contract that needs attention (P.305 in the sample;
   * with the API the first one the tenant reported as wrong, else the first in the list).
   */
  protected readonly selectedId = linkedSignal<Contract[], string | null>({
    source: this.store.contracts,
    computation: (list, previous) => {
      if (previous?.value && list.some((c) => c.tenancyId === previous.value)) return previous.value;
      if (this.viewport.isPhone()) return null;
      if (this.store.sample) return 't-305';
      return (list.find((c) => contractChip(c).tone === 'err') ?? list[0])?.tenancyId ?? null;
    },
  });
  protected readonly selected = computed(() => this.store.byId(this.selectedId()));
  protected readonly current = computed(() => { const c = this.selected(); return c ? latest(c) : undefined; });
  protected readonly sent = computed(() => { const c = this.selected(); return c ? lastSent(c) : undefined; });
  protected readonly tenant = computed(() => tenantChip(this.sent()));
  protected readonly draft = computed(() => (isDraft(this.current()) ? this.current() : undefined));
  /** Version the draft corrects, to show what changed. */
  protected readonly previous = computed(() => {
    const c = this.selected();
    return c && this.draft() ? c.versions[c.versions.length - 2] : undefined;
  });
  protected readonly history = computed(() => [...(this.selected()?.versions ?? [])].reverse());
  protected readonly totalSize = computed(() => fileSize((this.current()?.files ?? []).reduce((s, f) => s + f.size, 0)));

  /** Resets the terms form only when another version is shown, not when pages of the same draft change. */
  private readonly versionKey = computed(() => `${this.selectedId()}:${this.current()?.number}:${this.current()?.status}`);
  protected readonly termsModel = linkedSignal<string, TermsForm>({
    source: this.versionKey,
    computation: () => toTermsForm(this.current()?.terms ?? { monthlyRent: 0, deposit: 0, startDate: '', endDate: '', electricityRate: 0, waterRate: 0 }),
  });
  protected readonly tf = form(this.termsModel, termsSchema);
  protected readonly endDate = computed(() => {
    const m = this.termsModel();
    return /^\d{4}-\d{2}-\d{2}$/.test(m.startDate) ? ddmmyyyy(endDateFor(m.startDate, Number(m.months))) : '—';
  });

  protected readonly dragging = signal(false);
  protected readonly uploadErrors = signal<string[]>([]);
  protected readonly sendError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  private readonly objectUrls: string[] = [];

  constructor() {
    this.store.refreshIfStale();
    // The file links from the API last 5 minutes: read the shown contract again before they expire.
    const timer = setInterval(() => {
      const id = this.selectedId();
      if (id) this.store.refreshLinks(id);
    }, 30_000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      this.objectUrls.forEach((u) => URL.revokeObjectURL(u));
    });
  }

  protected readonly noTenancy = hasNoTenancy;

  protected select(c: Contract): void {
    this.selectedId.set(c.tenancyId);
    this.uploadErrors.set([]);
    this.sendError.set(null);
    this.notice.set(null);
  }

  protected back(): void {
    this.selectedId.set(null);
  }

  protected errOf<T>(field: FieldTree<T>): string | null {
    const s = field();
    return s.touched() && s.invalid() ? (s.errors()[0]?.message ?? 'Giá trị không hợp lệ.') : null;
  }

  /** "Phiên bản 1: 3.000.000 đ" when a draft term differs from the version it corrects. */
  protected was(key: 'monthlyRent' | 'deposit' | 'electricityRate' | 'waterRate'): string | null {
    const prev = this.previous();
    const now = parseMoney(this.termsModel()[key]);
    return prev && now != null && prev.terms[key] !== now ? `Phiên bản ${prev.number}: ${formatMoney(prev.terms[key])} đ` : null;
  }

  /** "Phạm Quốc Huy" -> "QH" (the two last words, as in the design). */
  protected initials(name: string): string {
    return name.split(/\s+/).slice(-2).map((w) => w[0]).join('').toUpperCase();
  }

  protected months(t: ContractTerms): number {
    return termMonths(t.startDate, t.endDate);
  }

  protected isImage(f: ContractFile): boolean {
    return f.contentType.startsWith('image/') && !/hei[cf]/.test(f.contentType);
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.addFiles(Array.from(input.files ?? []));
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  protected async send(): Promise<void> {
    const c = this.selected();
    const d = this.draft();
    if (!c || !d) return;
    this.sendError.set(d.files.length ? null : 'Tải bản scan lên trước khi gửi.');
    const ok = await submit(this.tf, async () => undefined);
    if (!ok || !d.files.length) return;
    const terms = fromTermsForm(this.termsModel());
    if (this.store.sample) {
      this.store.send(c.tenancyId, terms);
      this.notice.set(`Đã gửi phiên bản ${d.number} cho ${c.tenantName}. Người thuê nhận email, xem bản scan và xác nhận hoặc báo “Không đúng”.`);
      return;
    }
    this.store.sendByApi(c, terms).subscribe({
      next: (status) =>
        this.notice.set(
          status === 'AWAITING_CONFIRM'
            ? `Đã gửi phiên bản ${d.number} cho ${c.tenantName}. Người thuê nhận email, xem bản scan và xác nhận hoặc báo “Không đúng”.`
            : 'Đã lưu điều khoản. Mời người thuê ở màn Phòng.',
        ),
      error: (err: unknown) => this.sendError.set(err instanceof Error ? err.message : 'Chưa gửi được, thử lại sau.'),
    });
  }

  private addFiles(files: File[]): void {
    const c = this.selected();
    if (!c || files.length === 0) return;
    const errors = files.map(scanFileError).filter((e): e is string => e != null);
    this.uploadErrors.set(errors);
    if (errors.length) return;
    if (!this.store.sample) {
      this.notice.set(null);
      this.sendError.set(null);
      this.store.uploadScanByApi(c, files).subscribe({
        next: (fresh) => {
          // A room without a tenancy gets its real id once the first scan is up.
          if (this.selectedId() === c.tenancyId) this.selectedId.set(fresh.tenancyId);
          this.notice.set(`Đã tải ${files.length} tệp lên phiên bản ${latest(fresh)?.number ?? 1}.`);
        },
        error: (err: unknown) => this.uploadErrors.set([err instanceof Error ? err.message : 'Chưa tải được bản scan, thử lại sau.']),
      });
      return;
    }
    const picked: ContractFile[] = files.map((file, i) => {
      const url = URL.createObjectURL(file);
      this.objectUrls.push(url);
      return { fileId: `local-${Date.now()}-${i}`, fileName: file.name, contentType: file.type || 'image/heic', size: file.size, url, local: true };
    });
    this.notice.set(null);
    this.sendError.set(null);
    this.store.uploadScan(c.tenancyId, picked);
  }
}
