import { MeterKind } from '../../../../core/data/models';

/** Where one meter of one room stands in the capture round (before any photo taken in this session). */
export type SlotBase = 'todo' | 'retake' | 'sent' | 'approved';

/** Effective state of a meter once the photos taken in this session are counted. */
export type SlotState = SlotBase | 'sending' | 'review';

/** What the simulated AI returns for a photo of this meter. aiValue null = unreadable (LM-09). */
export interface SlotSample {
  photoUrl: string;
  aiValue: number | null;
  confidence: number | null;
}

/**
 * One meter in the round. previousValue and meterNo come from the last approved reading
 * and the room record; neither is in RoomReadingsResponse yet (see the report / API gaps).
 */
export interface CaptureSlot {
  kind: MeterKind;
  base: SlotBase;
  previousValue: number;
  meterNo: string;
  /** Value approved on the phone in this session (or earlier). */
  value: number | null;
  sample: SlotSample;
}

export interface CaptureRoom {
  roomId: string;
  roomCode: string;
  tenantName: string;
  electricity: CaptureSlot;
  water: CaptureSlot;
}

export type JobPhase = 'uploading' | 'reading' | 'read' | 'unreadable';

/** A photo taken in this session, from the moment it is picked until it is approved or discarded. */
export interface CaptureJob {
  id: number;
  roomId: string;
  kind: MeterKind;
  /** Object URL of the picked file, or a sample path. */
  photoUrl: string;
  /** True when photoUrl is an object URL that must be revoked. */
  ownUrl: boolean;
  sizeBytes: number;
  /** Upload progress 0–100. */
  progress: number;
  phase: JobPhase;
  /** Paused for lack of network (LM-10). */
  held: boolean;
  heldAt: string | null;
  /** ms spent in the reading phase. */
  readingMs: number;
  aiValue: number | null;
  confidence: number | null;
  capturedAt: string;
}

export interface SlotRef {
  roomId: string;
  kind: MeterKind;
}
