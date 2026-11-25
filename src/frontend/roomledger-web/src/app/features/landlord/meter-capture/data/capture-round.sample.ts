import { MeterKind } from '../../../../core/data/models';
import { CaptureRoom, CaptureSlot, SlotBase, SlotSample } from './capture.models';

/**
 * Sample capture round, same story as the mockups (00-system "Câu chuyện dữ liệu mẫu"):
 * Nhà trọ Cô Lan, kỳ 11/2026, 32 rooms, round on 01/12/2026 from 08:00. At 08:01 the landlord
 * has 26/32 rooms recorded; P.204 still needs water, P.306 and P.210 need a retake (AI could not
 * read the electricity photo), P.307, P.308, P.113, P.214 and P.215 are not recorded yet.
 * Names, meter numbers and photos are samples. Rooms without their own sample photo borrow one
 * from another room, with previous readings chosen so the usage stays believable.
 */
export const CAPTURE_PERIOD = '2026-11';
export const ROUND_DATE = '01/12/2026';
/** Sample clock: the first photo of the session is taken at 08:02, 01/12/2026. */
export const ROUND_START = '2026-12-01T08:02';

/** Unit prices from the sample ledger: electricity × 3.500 đ, water × 18.000 đ. */
export const UNIT_PRICE: Record<MeterKind, number> = { ELECTRICITY: 3500, WATER: 18000 };

function slot(
  kind: MeterKind,
  base: SlotBase,
  previousValue: number,
  meterNo: string,
  sample: Partial<SlotSample> & { photoUrl: string },
  value: number | null = null,
): CaptureSlot {
  return {
    kind,
    base,
    previousValue,
    meterNo,
    value,
    sample: { aiValue: null, confidence: null, ...sample },
  };
}

const read = (photoUrl: string, aiValue: number, confidence = 0.95) => ({ photoUrl, aiValue, confidence });
const blurry = { photoUrl: 'sample/elec-blurry.jpg' };

/** A room fully recorded before the session (photos sent, waiting in the Duyệt tab). */
function recorded(no: string, tenantName: string, elecPrev: number, waterPrev: number): CaptureRoom {
  const id = `r-${no}`;
  return {
    roomId: id,
    roomCode: `P.${no}`,
    tenantName,
    electricity: slot('ELECTRICITY', 'sent', elecPrev, `26-1${no}`, read('sample/elec-103.jpg', elecPrev + 128)),
    water: slot('WATER', 'sent', waterPrev, `N-2${no}`, read('sample/water-112.jpg', waterPrev + 6)),
  };
}

function todo(
  no: string,
  tenantName: string,
  elec: [number, string, number],
  water: [number, string, number],
): CaptureRoom {
  return {
    roomId: `r-${no}`,
    roomCode: `P.${no}`,
    tenantName,
    electricity: slot('ELECTRICITY', 'todo', elec[0], `26-1${no}`, read(elec[1], elec[2])),
    water: slot('WATER', 'todo', water[0], `N-2${no}`, read(water[1], water[2], 0.93)),
  };
}

/** Order of the list in LM-04: rooms still to capture first, then retakes, then recorded rooms. */
export function sampleRound(): CaptureRoom[] {
  return [
    {
      roomId: 'r-204',
      roomCode: 'P.204',
      tenantName: 'Trần Minh Anh',
      electricity: slot('ELECTRICITY', 'sent', 1102, '26-1204', read('sample/elec-204.jpg', 1284)),
      water: slot('WATER', 'todo', 61, 'N-2204', read('sample/water-204.jpg', 67, 0.94)),
    },
    todo('307', 'Đỗ Thị Hà', [836, 'sample/elec-103.jpg', 964], [142, 'sample/water-112.jpg', 148]),
    todo('308', 'Lý Văn Tâm', [1307, 'sample/elec-202.jpg', 1440], [60, 'sample/water-204.jpg', 67]),
    todo('113', 'Vương Thu Nga', [1385, 'sample/elec-105.jpg', 1502], [141, 'sample/water-112.jpg', 148]),
    todo('214', 'Hồ Minh Khoa', [2068, 'sample/elec-101.jpg', 2210], [62, 'sample/water-204.jpg', 67]),
    todo('215', 'Kiều Thanh Bình', [1740, 'sample/elec-102.jpg', 1876], [143, 'sample/water-112.jpg', 148]),
    {
      roomId: 'r-306',
      roomCode: 'P.306',
      tenantName: 'Lâm Gia Bảo',
      electricity: slot('ELECTRICITY', 'retake', 1940, '26-3376', blurry),
      water: slot('WATER', 'sent', 88, 'N-2306', read('sample/water-112.jpg', 94)),
    },
    {
      roomId: 'r-210',
      roomCode: 'P.210',
      tenantName: 'Châu Mỹ Linh',
      electricity: slot('ELECTRICITY', 'retake', 1512, '26-1210', blurry),
      water: slot('WATER', 'sent', 74, 'N-2210', read('sample/water-204.jpg', 80)),
    },
    recorded('101', 'Đặng Văn Long', 2064, 92),
    recorded('102', 'Mai Thị Hồng', 1730, 81),
    recorded('103', 'Cao Minh Đức', 840, 77),
    recorded('104', 'Lê Thị Thu', 1215, 69),
    recorded('105', 'Hà Minh Tú', 1360, 95),
    recorded('106', 'Trịnh Văn Nam', 990, 58),
    recorded('107', 'Phan Thị Yến', 1422, 103),
    recorded('108', 'Đinh Quang Vinh', 1876, 66),
    recorded('109', 'Lưu Thị Ngọc', 705, 49),
    recorded('110', 'Tô Văn Hải', 1544, 84),
    recorded('111', 'Kim Thị Lan', 1268, 71),
    recorded('112', 'Vũ Ngọc Mai', 1618, 136),
    recorded('201', 'Nguyễn Hữu Phước', 2960, 112),
    recorded('202', 'Tạ Thu Hà', 1300, 64),
    recorded('203', 'Huỳnh Văn Sơn', 1187, 90),
    recorded('205', 'Võ Thị Kim', 1630, 57),
    recorded('206', 'Dương Minh Trí', 1452, 79),
    recorded('207', 'La Thị Mỹ', 1098, 62),
    recorded('208', 'Mạc Văn Đông', 1764, 101),
    recorded('209', 'Bùi Anh Thư', 1321, 68),
    recorded('301', 'Ông Thị Hạnh', 1905, 87),
    recorded('302', 'Từ Văn Lực', 1043, 55),
    recorded('303', 'Chu Thị Nhàn', 1376, 73),
    recorded('305', 'Phạm Quốc Huy', 2620, 118),
  ];
}
