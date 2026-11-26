import type { components } from '../../../../generated/api-types';
import { MeterKind } from '../../../../core/data/models';

/** Where one meter of one room stands in the capture round (before any photo taken in this session). */
export type SlotBase = 'todo' | 'retake' | 'sent' | 'approved';

/** Effective state of a meter once the photos taken in this session are counted. */
export type SlotState = SlotBase | 'sending' | 'review';

/** What the simulated AI returns for a photo of this meter. aiValue null = unreadable (LM-09). Sample data only. */
export interface SlotSample {
  photoUrl: string;
  aiValue: number | null;
  confidence: number | null;
}

/**
 * One meter in the round. With the API, previousValue and readingId come from the reading of the period
 * (null while nothing was photographed), and meterNo is always null: the API has no meter serial number.
 */
export interface CaptureSlot {
  kind: MeterKind;
  base: SlotBase;
  previousValue: number | null;
  meterNo: string | null;
  /** Id of this period's reading, once a photo was uploaded (API only). */
  readingId: string | null;
  /** Value approved on the phone in this session (or earlier). */
  value: number | null;
  /** Null with the API: there is no simulated AI. */
  sample: SlotSample | null;
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
  /** ms spent in the reading phase (sample data). */
  readingMs: number;
  aiValue: number | null;
  confidence: number | null;
  capturedAt: string;
  /** Reading created for this photo (API only). */
  readingId: string | null;
  /** Why the upload or the approval failed, in Vietnamese; null when fine. */
  error: string | null;
  /** The picked file, kept so a held or failed upload can be sent again (API only). */
  file: File | null;
  /** The presigned upload the API gave for this photo (API only). */
  upload: components['schemas']['UploadUrlResponse'] | null;
  /** The AI had not answered within a minute: the landlord types the number (API only). */
  aiTimedOut: boolean;
}

export interface SlotRef {
  roomId: string;
  kind: MeterKind;
}
