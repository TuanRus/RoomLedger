/** Sample variants of the tenant home, picked with ?sample= (see TenantHomePage). */
export type HomeState = 'approved' | 'pending' | 'moving' | 'new' | 'unassigned';

const STATES: readonly HomeState[] = ['approved', 'pending', 'moving', 'new', 'unassigned'];

export function homeStateOf(value: string | null | undefined): HomeState {
  return STATES.find((s) => s === value) ?? 'approved';
}

export interface OpeningReading {
  kind: 'ELECTRICITY' | 'WATER';
  value: number | null;
  photoUrl: string | null;
  /** Shown under the reading, e.g. "Chủ nhà ghi và duyệt 15/12". */
  note: string;
}

export interface HomeSample {
  firstName: string;
  roomCode: string;
  /** TN-15 move: from, to and the day. */
  move?: { from: string; to: string; date: string; dayMonth: string; finals: OpeningReading[] };
  /** TN-16 first readings of a new tenant. */
  opening?: { date: string; finals: OpeningReading[] };
  /** TN-17 signed-in email. */
  email?: string;
}

export const HOME_SAMPLES: Record<HomeState, HomeSample> = {
  approved: { firstName: 'Minh Anh', roomCode: 'P.204' },
  pending: { firstName: 'Minh Anh', roomCode: 'P.204' },
  moving: {
    firstName: 'Minh Anh',
    roomCode: 'P.204',
    move: {
      from: 'P.204',
      to: 'P.302',
      date: '15/12/2026',
      dayMonth: '15/12',
      finals: [
        { kind: 'ELECTRICITY', value: 1396, photoUrl: 'sample/elec-204.jpg', note: 'Chủ nhà ghi và duyệt 15/12' },
        { kind: 'WATER', value: null, photoUrl: null, note: 'Chủ nhà đang ghi · 15/12' },
      ],
    },
  },
  new: {
    firstName: 'Ngọc Hân',
    roomCode: 'P.209',
    opening: {
      date: '05/12/2026',
      // Sample photos: the image files are P.204's meters; P.209 has none of its own yet.
      finals: [
        { kind: 'ELECTRICITY', value: 1508, photoUrl: 'sample/elec-204.jpg', note: 'Chủ nhà chụp 05/12/2026' },
        { kind: 'WATER', value: 88, photoUrl: 'sample/water-204.jpg', note: 'Chủ nhà chụp 05/12/2026' },
      ],
    },
  },
  unassigned: { firstName: '', roomCode: '', email: 'quanghuy@vidu.vn' },
};
