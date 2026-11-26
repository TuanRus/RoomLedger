#!/usr/bin/env node
/**
 * Seed + smoke test for the API running on your machine in LocalDev mode.
 * It calls the real endpoints as the FE will (header X-Dev-User instead of Cognito), checks every status code,
 * and leaves a property ready for the FE: meter readings to review and invoices for the sample tenant.
 *
 *   node src/backend/scripts/local-seed.mjs                       # API at http://localhost:5000
 *   API_URL=http://localhost:5080 node src/backend/scripts/local-seed.mjs
 *
 * Runs once per database: if the sample property exists it stops. Start over with `docker compose down -v`, then migrate again.
 * People and ids match src/frontend/roomledger-web/src/app/core/dev/dev-users.ts. Sample data only.
 */

const API = (process.env.API_URL ?? 'http://localhost:5000').replace(/\/$/, '') + '/api/v1';
const PROPERTY_NAME = 'Nhà trọ Cô Lan (local)';

const LANDLORD = { id: '0193a000-0000-7000-8000-00000000a001', email: 'chunha.lan@example.com', group: 'landlord', name: 'Cô Lan' };
const tenant = (room, name, email) => ({ id: `0193a000-0000-7000-8000-000000000${room}`, email: email ?? `p${room}@example.com`, group: 'tenant', name });
const MINH_ANH = { id: '0193a000-0000-7000-8000-00000000b204', email: 'minhanh@example.com', group: 'tenant', name: 'Trần Minh Anh' };

/** Period the landlord reviews today (last month), and the two before it. */
function periodsBack(n) {
  const d = new Date();
  d.setDate(1);
  d.setMonth(d.getMonth() - n);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}
const P = periodsBack(1);
const P1 = periodsBack(2);
const P2 = periodsBack(3);

/**
 * One room per review state. elec/water: [value two periods back (P2) or null, previous (P1), AI value this period (P)].
 * ai: what the fake AI returns for this period.
 */
const ROOMS = [
  { code: 'P.101', who: tenant(101, 'Đặng Văn Long'), elec: [null, 2064, 2210], water: [null, 40, 45], ai: 'normal' },
  { code: 'P.102', who: tenant(102, 'Mai Thị Hồng'), elec: [null, 1730, 1876], water: [null, 52, 58], ai: 'normal' },
  { code: 'P.103', who: tenant(103, 'Cao Minh Đức'), elec: [null, 840, 964], water: [null, 31, 36], ai: 'normal' },
  { code: 'P.204', who: MINH_ANH, elec: [935, 1102, 1284], water: [55, 61, 67], ai: 'approved' },
  { code: 'P.305', who: tenant(305, 'Phạm Quốc Huy'), elec: [null, 2620, 2988], water: [null, 70, 77], ai: 'spike' },
  { code: 'P.306', who: tenant(306, 'Lâm Gia Bảo'), elec: [null, 1940, null], water: [null, 88, 94], ai: 'unreadable' },
  { code: 'P.307', who: tenant(307, 'Đỗ Thị Hà'), elec: [null, 1500, null], water: [null, 20, null], ai: 'none' },
];

const header = (u) => `${u.id}|${u.email}|${u.group}|true|${encodeURIComponent(u.name)}`;
let calls = 0;

async function call(user, method, path, body, expect = [200, 201]) {
  calls++;
  const res = await fetch(API + path, {
    method,
    headers: { 'X-Dev-User': header(user), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const json = await res.json().catch(() => null);
  if (!expect.includes(res.status)) {
    throw new Error(`${method} ${path} -> ${res.status} ${json?.errorCode ?? ''} ${json?.message ?? ''}`.trim());
  }
  return json?.data;
}

const SHA = 'n4bQgYhMfWWaL+qgxVrQFaO/TxsrC4Is0V1sFbDwCgg=';

async function reading(roomId, kind, period) {
  const data = await call(LANDLORD, 'POST', `/rooms/${roomId}/meter-readings`, { kind, period, fileName: 'dong-ho.jpg', contentType: 'image/jpeg', size: 1000, sha256: SHA });
  return data.reading.readingId;
}

async function confirmed(roomId, kind, period, value) {
  const id = await reading(roomId, kind, period);
  await call(LANDLORD, 'PATCH', `/meter-readings/${id}`, { value, confirm: true });
}

async function main() {
  const health = await fetch(`${API}/health`).catch(() => null);
  if (!health?.ok) throw new Error(`API không chạy ở ${API}. Xem src/backend/README.md, mục LocalDev.`);

  const existing = await call(LANDLORD, 'GET', '/properties?limit=100');
  if (existing.items.some((p) => p.name === PROPERTY_NAME)) {
    console.log(`Đã có "${PROPERTY_NAME}", bỏ qua. Muốn seed lại: docker compose down -v, docker compose up -d db, chạy lại dotnet ef database update và API.`);
    return;
  }

  const startDate = `${P2}-01`;
  const property = await call(LANDLORD, 'POST', '/properties', { name: PROPERTY_NAME, address: '12 Lê Lợi, Q.1, TP.HCM', electricityRate: 3500, waterRate: 18000, serviceFee: 100000 });
  const propertyId = property.propertyId;

  for (const room of ROOMS) {
    const r = await call(LANDLORD, 'POST', `/properties/${propertyId}/rooms`, { code: room.code, monthlyRent: 3500000, areas: [{ name: 'Phòng ngủ', minPhotos: 4 }] });
    room.id = r.roomId;
    const t = await call(LANDLORD, 'POST', `/rooms/${room.id}/tenancies`);
    await call(LANDLORD, 'PATCH', `/tenancies/${t.tenancyId}/versions/1/terms`, { monthlyRent: 3500000, deposit: 7000000, startDate, endDate: `${Number(P2.slice(0, 4)) + 1}-${P2.slice(5)}-01`, electricityRate: 3500, waterRate: 18000 });
    await call(LANDLORD, 'POST', `/tenancies/${t.tenancyId}/invites`, { email: room.who.email });
    const invite = await call(LANDLORD, 'GET', `/local-dev/invites/latest?email=${encodeURIComponent(room.who.email)}`);
    await call(room.who, 'POST', `/invites/${invite.token}/accept`);
    await call(room.who, 'POST', `/tenancies/${t.tenancyId}/versions/1/confirm`, { decision: 'CONFIRM' });

    for (const [kind, values] of [['ELECTRICITY', room.elec], ['WATER', room.water]]) {
      const [v2, v1, v0] = values;
      if (v2 != null) await confirmed(room.id, kind, P2, v2);
      await confirmed(room.id, kind, P1, v1);
      if (room.ai === 'none') continue;
      if (room.ai === 'approved') {
        await confirmed(room.id, kind, P, v0);
        continue;
      }
      const id = await reading(room.id, kind, P);
      const spike = room.ai === 'spike' && kind === 'ELECTRICITY';
      const unreadable = room.ai === 'unreadable' && kind === 'ELECTRICITY';
      await call(LANDLORD, 'POST', `/local-dev/meter-readings/${id}/ai-result`, unreadable
        ? { aiValue: null, aiConfidence: 0.2, needsReview: true, reviewReason: 'AI_FAILED' }
        : { aiValue: v0, aiConfidence: spike ? 0.92 : 0.96, needsReview: spike, reviewReason: spike ? 'UNUSUAL_USAGE' : null });
    }
    console.log(`  ${room.code} · ${room.who.name} · ${room.ai}`);
  }

  // Invoices for the sample tenant (P.204): last period paid, this period sent.
  for (const period of [P1, P]) {
    const gen = await call(LANDLORD, 'POST', `/properties/${propertyId}/invoices:generate`, { period });
    const mine = gen.created.find((c) => c.roomCode === 'P.204');
    if (!mine) throw new Error(`Không lập được hóa đơn P.204 kỳ ${period}: ${JSON.stringify(gen.skipped)}`);
    await call(LANDLORD, 'POST', `/invoices/${mine.invoiceId}/send`);
    if (period === P1) await call(LANDLORD, 'POST', `/invoices/${mine.invoiceId}/paid`);
  }

  // Smoke checks: what the FE screens read
  const rows = await call(LANDLORD, 'GET', `/properties/${propertyId}/meter-readings?period=${P}`);
  const states = rows.map((r) => `${r.roomCode}:${r.electricity?.status ?? '—'}`).join(' ');
  const invoices = await call(MINH_ANH, 'GET', '/invoices');
  if (invoices.items.length !== 2) throw new Error(`Người thuê phải thấy 2 hóa đơn, thấy ${invoices.items.length}`);
  await call(MINH_ANH, 'GET', `/invoices/${invoices.items[0].invoiceId}`);
  await call(MINH_ANH, 'GET', '/tenant/home');
  await call(MINH_ANH, 'GET', `/properties/${propertyId}/meter-readings?period=${P}`, null, [403]);
  await call(LANDLORD, 'GET', `/local-dev/invites/latest?email=nobody@example.com`, null, [404]);

  console.log(`\nXong: ${calls} lời gọi API. Kỳ duyệt ${P}. Điện: ${states}`);
  console.log('Chạy FE: cd src/frontend/roomledger-web && pnpm start:local-api, mở http://localhost:4200/preview');
}

main().catch((err) => {
  console.error(`LỖI: ${err.message}`);
  process.exit(1);
});
