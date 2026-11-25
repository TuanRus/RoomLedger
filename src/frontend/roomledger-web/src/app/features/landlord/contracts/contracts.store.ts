import { Service, signal } from '@angular/core';

import { Contract, ContractFile, ContractTerms, ContractVersion, isDraft, isoDate, latest } from './contracts.model';

const SCAN = 'sample/contract-p1.jpg';
const sampleFiles = (id: string): ContractFile[] => [
  { fileId: `${id}-p1`, fileName: 'hop-dong-trang-1.jpg', contentType: 'image/jpeg', size: 942_000, url: SCAN, local: false },
  { fileId: `${id}-p2`, fileName: 'hop-dong-trang-2.jpg', contentType: 'image/jpeg', size: 945_000, url: SCAN, local: false },
];

const terms = (monthlyRent: number, deposit: number, startDate: string, endDate: string): ContractTerms => ({
  monthlyRent, deposit, startDate, endDate, electricityRate: 3_500, waterRate: 18_000,
});

function locked(id: string, roomCode: string, tenantName: string, t: ContractTerms, uploadedAt: string, confirmedAt: string): Contract {
  return {
    tenancyId: id, roomCode, tenantName, status: 'ACTIVE',
    versions: [{ number: 1, type: 'INITIAL', status: 'LOCKED', uploadedAt, note: null, sentAt: uploadedAt, tenantRespondedAt: confirmedAt, rejectReason: null, terms: t, files: sampleFiles(id) }],
  };
}

/** Sample contracts, same story as the design mockups (Nhà trọ Cô Lan, 19/11/2026). Names are samples. */
const SAMPLE_CONTRACTS: Contract[] = [
  {
    tenancyId: 't-305', roomCode: 'P.305', tenantName: 'Phạm Quốc Huy', status: 'AWAITING_TENANT',
    versions: [
      {
        number: 1, type: 'INITIAL', status: 'REJECTED', uploadedAt: '2026-11-18', note: null, sentAt: '2026-11-18', tenantRespondedAt: '2026-11-19',
        rejectReason: 'Tiền cọc ghi 3.000.000 đ nhưng tôi đã đóng 2.500.000 đ, có biên nhận.',
        terms: terms(2_800_000, 3_000_000, '2026-06-01', '2027-05-31'), files: sampleFiles('t-305-v1'),
      },
      {
        number: 2, type: 'INITIAL', status: 'UPLOADED', uploadedAt: '2026-11-19', note: 'sửa tiền cọc', sentAt: null, tenantRespondedAt: null, rejectReason: null,
        terms: terms(2_800_000, 2_500_000, '2026-06-01', '2027-05-31'), files: sampleFiles('t-305-v2'),
      },
    ],
  },
  {
    tenancyId: 't-209', roomCode: 'P.209', tenantName: 'Ngô Thanh Tâm', status: 'AWAITING_TENANT',
    versions: [{ number: 1, type: 'INITIAL', status: 'AWAITING_CONFIRM', uploadedAt: '2026-11-17', note: null, sentAt: '2026-11-17', tenantRespondedAt: null, rejectReason: null, terms: terms(2_500_000, 3_000_000, '2026-11-15', '2027-11-14'), files: sampleFiles('t-209') }],
  },
  locked('t-101', 'P.101', 'Đặng Văn Long', terms(2_500_000, 2_500_000, '2026-03-01', '2027-02-28'), '2026-02-26', '2026-02-27'),
  locked('t-102', 'P.102', 'Mai Thị Hồng', terms(2_500_000, 2_500_000, '2026-03-01', '2027-02-28'), '2026-02-26', '2026-02-28'),
  locked('t-201', 'P.201', 'Nguyễn Hữu Phước', terms(2_700_000, 2_700_000, '2026-01-01', '2026-12-31'), '2025-12-28', '2025-12-29'),
  locked('t-204', 'P.204', 'Trần Minh Anh', terms(2_500_000, 2_500_000, '2026-03-01', '2027-02-28'), '2026-02-25', '2026-02-26'),
];

/** LL-07 contracts of one property. No HTTP yet. */
@Service()
export class ContractsStore {
  readonly contracts = signal<Contract[]>(SAMPLE_CONTRACTS);
  readonly landlordName = 'Lê Thị Lan';

  byId(id: string | null): Contract | undefined {
    return this.contracts().find((c) => c.tenancyId === id);
  }

  /**
   * Adds scanned pages. A draft version gets the new pages in place of its old ones; otherwise a new
   * draft version is created with the terms of the latest version, for the landlord to check before sending.
   */
  uploadScan(tenancyId: string, files: ContractFile[], today = isoDate(new Date())): void {
    this.update(tenancyId, (c) => {
      const last = latest(c);
      if (last && isDraft(last)) {
        return { ...c, versions: [...c.versions.slice(0, -1), { ...last, files, uploadedAt: today }] };
      }
      const next: ContractVersion = {
        number: (last?.number ?? 0) + 1,
        type: !last || last.status === 'REJECTED' ? 'INITIAL' : 'ADDENDUM',
        status: 'UPLOADED', uploadedAt: today, note: null, sentAt: null, tenantRespondedAt: null, rejectReason: null,
        terms: last ? { ...last.terms } : { monthlyRent: 0, deposit: 0, startDate: today, endDate: today, electricityRate: 3_500, waterRate: 18_000 },
        files,
      };
      return { ...c, versions: [...c.versions, next] };
    });
  }

  /** Saves the draft terms and sends the draft to the tenant: status "Chờ người thuê xác nhận". */
  send(tenancyId: string, t: ContractTerms, today = isoDate(new Date())): void {
    this.update(tenancyId, (c) => {
      const last = latest(c);
      if (!last || !isDraft(last) || last.files.length === 0) return c;
      const prev = c.versions[c.versions.length - 2];
      const note = last.note ?? (prev ? changedNote(prev.terms, t) : null);
      return {
        ...c,
        status: c.status === 'ACTIVE' ? 'ACTIVE' : 'AWAITING_TENANT',
        versions: [...c.versions.slice(0, -1), { ...last, terms: t, note, status: 'AWAITING_CONFIRM', sentAt: today }],
      };
    });
  }

  private update(tenancyId: string, fn: (c: Contract) => Contract): void {
    this.contracts.update((list) => list.map((c) => (c.tenancyId === tenancyId ? fn(c) : c)));
  }
}

const TERM_NAMES: Record<keyof ContractTerms, string> = {
  monthlyRent: 'tiền phòng', deposit: 'tiền cọc', startDate: 'ngày bắt đầu', endDate: 'thời hạn', electricityRate: 'giá điện', waterRate: 'giá nước',
};

/** "sửa tiền cọc" from the terms that differ from the previous version. */
export function changedNote(before: ContractTerms, after: ContractTerms): string | null {
  const keys = (Object.keys(TERM_NAMES) as (keyof ContractTerms)[]).filter((k) => before[k] !== after[k]);
  return keys.length ? `sửa ${keys.map((k) => TERM_NAMES[k]).join(', ')}` : null;
}
