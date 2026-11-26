import { HttpClient, HttpEventType, HttpHeaders } from '@angular/common/http';
import { Observable, filter, map } from 'rxjs';

import type { components } from '../../../generated/api-types';

type UploadUrlResponse = components['schemas']['UploadUrlResponse'];

/** SHA-256 of a file as base64, the format CreateMeterReadingRequest / CreateUploadRequest expect (not hex). */
export async function sha256Base64(file: Blob): Promise<string> {
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', await file.arrayBuffer()));
  let binary = '';
  for (const byte of hash) binary += String.fromCharCode(byte);
  return btoa(binary);
}

/**
 * PUTs a file to the presigned URL the API returned and emits the progress, 0 to 100, ending with 100.
 * In AWS the URL is a presigned S3 URL, which is not the API, so no interceptor adds a header to it. In LocalDev it is
 * /api/v1/local-dev/files/..., served by the API itself, so the dev interceptor adds X-Dev-User there too (harmless).
 * Only the headers the API listed are sent besides that.
 */
export function putPresigned(http: HttpClient, upload: UploadUrlResponse, file: Blob): Observable<number> {
  return http
    .request(upload.method ?? 'PUT', upload.uploadUrl ?? '', {
      body: file,
      headers: new HttpHeaders(upload.headers ?? {}),
      reportProgress: true,
      observe: 'events',
      responseType: 'text',
    })
    .pipe(
      filter((e) => e.type === HttpEventType.UploadProgress || e.type === HttpEventType.Response),
      map((e) => {
        if (e.type === HttpEventType.Response) return 100;
        const total = e.total ?? file.size;
        // Stay below 100 until the server has answered.
        return total > 0 ? Math.min(99, Math.round((100 * e.loaded) / total)) : 0;
      }),
    );
}

const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/heic', 'image/heif'] as const;
export type UploadImageType = (typeof IMAGE_TYPES)[number];
export type UploadScanType = UploadImageType | 'application/pdf';

/** The content type the API accepts for a picked file ("image/heic" when the browser leaves it empty), or null. */
export function imageType(file: { name: string; type: string }): UploadImageType | null {
  const type = file.type || (/\.heic$/i.test(file.name) ? 'image/heic' : /\.heif$/i.test(file.name) ? 'image/heif' : '');
  return (IMAGE_TYPES as readonly string[]).includes(type) ? (type as UploadImageType) : null;
}

/** Same for a contract scan: a photo or a PDF. */
export function scanType(file: { name: string; type: string }): UploadScanType | null {
  return file.type === 'application/pdf' ? 'application/pdf' : imageType(file);
}
