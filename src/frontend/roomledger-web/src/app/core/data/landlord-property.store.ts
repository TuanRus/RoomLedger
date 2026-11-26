import { Service, computed, signal } from '@angular/core';
import { Observable, Subscription, map, of, shareReplay, switchMap, tap } from 'rxjs';

import type { components } from '../../generated/api-types';
import { PagedResult } from '../../shared/models/api-response.model';
import { injectApi } from '../api/api-client';
import { SAMPLE_PERIOD } from './sample-ledger';

export type PropertyResponse = components['schemas']['PropertyResponse'];
export type RoomResponse = components['schemas']['RoomResponse'];
export type RoomReadingsResponse = components['schemas']['RoomReadingsResponse'];

/** The period the landlord reviews today: meters are read on the 1st–3rd for the month before. "2026-12-02" -> "2026-11". */
export function reviewPeriod(today: Date): string {
  const y = today.getFullYear();
  const m = today.getMonth(); // 0-based, so this is already last month's 1-based number
  return m === 0 ? `${y - 1}-12` : `${y}-${String(m).padStart(2, '0')}`;
}

/** "2026-11" -> "2026-10"; "2026-01" -> "2025-12". */
export function periodBefore(period: string): string {
  const [y, m] = period.split('-').map(Number);
  return m === 1 ? `${y - 1}-12` : `${y}-${String(m - 1).padStart(2, '0')}`;
}

/**
 * The landlord's property and its rooms, shared by every landlord screen (GET /properties, GET /properties/{id}/rooms).
 * API mode only: with sample data `property` stays null and `rooms` empty, the sample stores keep their own data.
 */
@Service()
export class LandlordPropertyStore {
  private readonly api = injectApi();
  /** True with sample data: demo controls and sample values may show. */
  readonly sample = !this.api;

  /** The period the landlord works on (invoices, capture, review): last month, or the sample period. */
  readonly period = signal(this.api ? reviewPeriod(new Date()) : SAMPLE_PERIOD);

  readonly property = signal<PropertyResponse | null>(null);
  readonly rooms = signal<RoomResponse[]>([]);
  /** 'idle' before the first load; sample data is always 'ready'. */
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>(this.api ? 'idle' : 'ready');
  readonly error = signal<string | null>(null);
  /** True once the room list has arrived (the property name can show before that). */
  readonly roomsLoaded = signal(false);

  /** API mode, loaded, and the landlord has no property: every landlord page says so. */
  readonly missing = computed(() => !this.sample && this.status() === 'ready' && !this.property());
  readonly propertyId = computed(() => this.property()?.propertyId ?? null);
  readonly occupiedCount = computed(() => this.rooms().filter((r) => r.status === 'OCCUPIED').length);
  readonly vacantCount = computed(() => this.rooms().filter((r) => r.status === 'VACANT').length);
  readonly tenantNameByRoomId = computed(() => {
    const map = new Map<string, string>();
    for (const r of this.rooms()) if (r.roomId && r.tenantName) map.set(r.roomId, r.tenantName);
    return map;
  });
  readonly roomByCode = computed(() => new Map(this.rooms().flatMap((r) => (r.code ? [[r.code, r] as const] : []))));
  readonly roomById = computed(() => new Map(this.rooms().flatMap((r) => (r.roomId ? [[r.roomId, r] as const] : []))));

  /** Counts room reloads, so a store that built something from the rooms can tell its copy is old. */
  readonly roomsRevision = signal(0);

  private property$: Observable<PropertyResponse | null> | null = null;
  private rooms$: Observable<RoomResponse[]> | null = null;
  private readings$: Observable<RoomReadingsResponse[]> | null = null;
  private loading: Subscription | null = null;

  constructor() {
    this.load();
  }

  /**
   * The landlord's first property, requested once and shared by every store that needs its id.
   * Emits null when the landlord has no property. After an error the next call asks again.
   */
  loadedProperty(): Observable<PropertyResponse | null> {
    const api = this.api;
    if (!api) return of(null);
    this.property$ ??= api.get<PagedResult<PropertyResponse>>('/properties', { limit: 1 }).pipe(
      map((page) => page.items[0] ?? null),
      tap((p) => this.property.set(p)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.property$;
  }

  /** The property's rooms, requested once together with the property and shared. */
  loadedRooms(): Observable<RoomResponse[]> {
    const api = this.api;
    if (!api) return of([]);
    this.rooms$ ??= this.loadedProperty().pipe(
      switchMap((p) => (p?.propertyId ? api.getAll<RoomResponse>(`/properties/${p.propertyId}/rooms`) : of<RoomResponse[]>([]))),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.rooms$;
  }

  /**
   * The readings of the working period for the whole property (GET /properties/{id}/meter-readings), requested once
   * and shared by the review and capture screens. The cache is dropped after an error and by invalidateReadings(),
   * which whoever changes readings (a poll that saw the AI result, an approval) or reloads a screen calls.
   */
  loadedReadings(): Observable<RoomReadingsResponse[]> {
    const api = this.api;
    if (!api) return of([]);
    this.readings$ ??= this.loadedProperty().pipe(
      switchMap((p) =>
        p?.propertyId ? api.get<RoomReadingsResponse[]>(`/properties/${p.propertyId}/meter-readings`, { period: this.period() }) : of<RoomReadingsResponse[]>([]),
      ),
      tap({ error: () => (this.readings$ = null) }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.readings$;
  }

  /** Counts the times the readings changed on the server (or may have), so each screen holding its own copy can tell it is old. */
  readonly readingsRevision = signal(0);

  /**
   * The next loadedReadings() asks the server again. Unless `notify` is false the revision moves on, and the screens
   * that hold a copy (review, capture) reload it.
   */
  invalidateReadings(notify = true): void {
    this.readings$ = null;
    if (notify) this.readingsRevision.update((n) => n + 1);
  }

  /** (Re)loads the property and its rooms; does nothing with sample data. */
  load(): void {
    const api = this.api;
    if (!api) return;
    this.loading?.unsubscribe();
    this.property$ = null;
    this.rooms$ = null;
    this.readings$ = null;
    // Recomputed here so a session that crosses into a new month moves on to the new period.
    this.period.set(reviewPeriod(new Date()));
    this.status.set('loading');
    this.error.set(null);
    this.roomsLoaded.set(false);
    this.loading = this.loadedRooms().subscribe({
        next: (rooms) => {
          this.rooms.set(rooms);
          this.roomsLoaded.set(true);
          this.roomsRevision.update((n) => n + 1);
          this.status.set('ready');
        },
        error: (err: unknown) => {
          this.property$ = null;
          this.rooms$ = null;
          this.error.set(err instanceof Error ? err.message : 'Không tải được nhà trọ.');
          this.status.set('error');
        },
      });
  }

  /** Reloads only the rooms (after an invite or a rent change). */
  reloadRooms(): void {
    const api = this.api;
    const id = this.propertyId();
    if (!api || !id) return;
    api.getAll<RoomResponse>(`/properties/${id}/rooms`).subscribe({
      next: (rooms) => {
        this.rooms.set(rooms);
        this.roomsRevision.update((n) => n + 1);
      },
      error: (err: unknown) => this.error.set(err instanceof Error ? err.message : 'Không tải được danh sách phòng.'),
    });
  }

  /** Replaces one room with the version the server just returned. */
  updateRoom(room: RoomResponse): void {
    this.rooms.update((list) => list.map((r) => (r.roomId === room.roomId ? { ...r, ...room } : r)));
  }
}
