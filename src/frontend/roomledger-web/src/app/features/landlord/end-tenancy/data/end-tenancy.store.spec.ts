import { TestBed } from '@angular/core/testing';

import { RoomsStore } from '../../rooms/data/rooms.store';
import { EndTenancyStore, blockers, currentStep, finalInvoice } from './end-tenancy.store';

describe('EndTenancyStore', () => {
  let store: EndTenancyStore;

  beforeEach(() => {
    store = TestBed.inject(EndTenancyStore);
  });

  it('starts P.204 as in LL-10: move to P.302, electricity approved, water missing, blocked', () => {
    const d = store.draft('r-204')!;
    expect(d.kind).toBe('MOVE');
    expect(d.targetRoomId).toBe('r-302');
    expect(currentStep(d)).toBe(2);
    expect(blockers(d)).toEqual(['còn thiếu chỉ số nước cuối']);
    const inv = finalInvoice(d);
    expect(inv.electricity).toEqual({ usage: 112, rate: 3500, amount: 392_000 });
    expect(inv.total).toBeNull();
  });

  it('computes the total once water is recorded, using the typed rent without day split', () => {
    store.recordDemo('r-204', 'WATER');
    store.setRent('r-204', 1_250_000);
    const d = store.draft('r-204')!;
    expect(blockers(d)).toEqual([]);
    expect(finalInvoice(d).total).toBe(1_250_000 + 392_000 + 108_000);
  });

  it('needs approval of an uploaded photo before finishing', () => {
    store.receivePhoto('r-204', 'WATER');
    expect(blockers(store.draft('r-204')!)).toEqual(['chỉ số nước cuối chưa duyệt']);
    expect(store.finish('r-204')).toBe(false);
    store.approve('r-204', 'WATER');
    expect(store.finish('r-204')).toBe(true);
    expect(TestBed.inject(RoomsStore).byId('r-204')!.status).toBe('VACANT');
    expect(store.draft('r-204')!.finishedAt).not.toBeNull();
  });

  it('lists every blocker for a fresh room and refuses empty rooms', () => {
    const d = store.draft('r-101')!;
    expect(blockers(d)).toEqual([
      'chưa chọn hình thức', 'còn thiếu chỉ số điện cuối', 'còn thiếu chỉ số nước cuối', 'chưa nhập tiền phòng tháng cuối',
    ]);
    expect(store.draft('r-209')).toBeNull();
  });

  it('offers only empty rooms without a pending invite as move targets', () => {
    TestBed.inject(RoomsStore).invite('r-209', 'a@b.vn', '2026-12-01');
    expect(store.targets().map((t) => t.code)).toEqual(['P.302', 'P.303']);
  });
});
