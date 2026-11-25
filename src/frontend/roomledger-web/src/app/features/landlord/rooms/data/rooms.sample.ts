import { ReviewState } from '../../../../core/data/models';
import { ContractState, RoomRow } from './rooms.models';

/**
 * Sample rooms of Nhà trọ Cô Lan on 02/12/2026, 09:00 (same story as the design mockups):
 * 36 rooms, 32 rented, 4 empty (P.210 has an invite waiting). Kỳ 11/2026 electricity:
 * 19 chờ duyệt, 3 bất thường, 2 không đọc được, 5 chưa ghi, 3 đã duyệt.
 */
export const ROOMS_NOW = '2026-12-02T09:00:00+07:00';

/** code, tenant (null = empty), rent, reading state, contract state. */
type Seed = [string, string | null, number, ReviewState | null, ContractState?];

const SEEDS: Seed[] = [
  ['P.101', 'Đặng Văn Long', 2_400_000, 'pending'],
  ['P.102', 'Mai Thị Hồng', 2_400_000, 'pending'],
  ['P.103', 'Cao Minh Đức', 2_400_000, 'pending'],
  ['P.104', 'Bùi Thanh Tùng', 2_400_000, 'pending'],
  ['P.105', 'Hà Minh Tú', 2_400_000, 'pending'],
  ['P.106', 'Ngô Thị Lan Anh', 2_400_000, 'pending'],
  ['P.107', 'Đinh Quang Hải', 2_400_000, 'pending'],
  ['P.108', 'Lương Thị Mỹ', 2_400_000, 'pending'],
  ['P.109', 'Tôn Đức Thịnh', 2_400_000, 'pending'],
  ['P.110', 'Trịnh Văn An', 2_500_000, 'unrecorded'],
  ['P.112', 'Vũ Ngọc Mai', 2_500_000, 'pending'],
  ['P.113', 'Vương Thu Nga', 2_400_000, 'unrecorded'],
  ['P.114', 'Kiều Văn Sơn', 2_400_000, 'abnormal'],
  ['P.201', 'Nguyễn Hữu Phước', 2_700_000, 'approved'],
  ['P.202', 'Tạ Thu Hà', 2_500_000, 'pending'],
  ['P.203', 'Châu Ngọc Diệp', 2_500_000, 'pending'],
  ['P.204', 'Trần Minh Anh', 2_500_000, 'pending'],
  ['P.205', 'Quách Minh Nhật', 2_500_000, 'pending'],
  ['P.206', 'La Thị Thanh', 2_500_000, 'abnormal'],
  ['P.207', 'Phan Thu Trang', 2_500_000, 'approved'],
  ['P.208', 'Mạc Văn Kiên', 2_500_000, 'pending'],
  ['P.209', null, 2_600_000, null, 'DRAFT'],
  ['P.210', 'Lê Quang Vinh', 2_600_000, null, 'INVITED'],
  ['P.211', 'Đoàn Thị Yến', 2_500_000, 'unreadable'],
  ['P.212', 'Hoàng Gia Huy', 2_500_000, 'pending'],
  ['P.213', 'Lục Thị Hương', 2_500_000, 'approved'],
  ['P.214', 'Hồ Minh Khoa', 2_500_000, 'unrecorded'],
  ['P.215', 'Từ Minh Quân', 2_500_000, 'pending'],
  ['P.301', 'Thái Văn Phúc', 2_800_000, 'pending'],
  ['P.302', null, 2_800_000, null, 'NONE'],
  ['P.303', null, 2_800_000, null, 'NONE'],
  ['P.304', 'Giang Thị Ngân', 2_800_000, 'pending'],
  ['P.305', 'Phạm Quốc Huy', 2_800_000, 'abnormal', 'REJECTED'],
  ['P.306', 'Lâm Gia Bảo', 2_800_000, 'unreadable'],
  ['P.307', 'Đỗ Thị Hà', 2_800_000, 'unrecorded'],
  ['P.308', 'Lý Văn Tâm', 2_800_000, 'unrecorded'],
];

/** Readings that other screens show must match: P.204 (LL-10), P.209 (LL-03), P.302 (LL-10). */
const METERS: Record<string, [number, number]> = {
  'P.204': [1284, 65],
  'P.209': [1508, 88],
  'P.302': [2115, 95],
};

const READ_ON = '2026-11-30';

function room([code, tenantName, monthlyRent, reading, contract = 'CONFIRMED']: Seed, i: number): RoomRow {
  const number = code.slice(2);
  const invited = contract === 'INVITED';
  const occupied = tenantName != null && !invited;
  const [elec, water] = METERS[code] ?? [900 + ((i * 137) % 2100), 40 + ((i * 7) % 60)];
  return {
    roomId: `r-${number}`,
    code,
    floor: Number(number[0]),
    monthlyRent,
    deposit: 3_000_000,
    status: occupied ? 'OCCUPIED' : 'VACANT',
    displayStatus: occupied ? 'ACTIVE' : invited ? 'AWAITING_CONFIRM' : 'VACANT',
    tenantName,
    tenancyStart: occupied ? (code === 'P.204' ? '2026-03-01' : `2026-0${1 + (i % 8)}-01`) : null,
    contract,
    reading,
    electricity: { value: elec, date: READ_ON },
    water: { value: water, date: READ_ON },
    invite: invited ? { email: 'quang.vinh@vidu.vn', startDate: '2026-12-01', expiresAt: '2026-12-03T15:20:00+07:00' } : null,
  };
}

export const SAMPLE_ROOMS: RoomRow[] = SEEDS.map(room);
