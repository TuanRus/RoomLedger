import { HttpClient, HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { imageType, putPresigned, scanType, sha256Base64 } from './upload';

describe('upload helpers', () => {
  it('hashes a file as base64, not hex', async () => {
    // SHA-256 of "abc".
    expect(await sha256Base64(new File(['abc'], 'a.txt'))).toBe('ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=');
  });

  it('accepts the content types the API accepts', () => {
    expect(imageType({ name: 'a.jpg', type: 'image/jpeg' })).toBe('image/jpeg');
    expect(imageType({ name: 'IMG_1.HEIC', type: '' })).toBe('image/heic');
    expect(imageType({ name: 'a.webp', type: 'image/webp' })).toBeNull();
    expect(imageType({ name: 'a.pdf', type: 'application/pdf' })).toBeNull();
    expect(scanType({ name: 'a.pdf', type: 'application/pdf' })).toBe('application/pdf');
    expect(scanType({ name: 'a.png', type: 'image/png' })).toBe('image/png');
  });

  it('PUTs to the presigned URL with the listed headers only and reports progress up to 100', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const progress: number[] = [];
    let done = false;
    const file = new File(['x'.repeat(1000)], 'a.jpg', { type: 'image/jpeg' });
    putPresigned(TestBed.inject(HttpClient), { uploadUrl: 'https://s3.test/bucket/key?sig=1', method: 'PUT', headers: { 'Content-Type': 'image/jpeg' } }, file).subscribe({
      next: (p) => progress.push(p),
      complete: () => (done = true),
    });
    const req = http.expectOne('https://s3.test/bucket/key?sig=1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('Content-Type')).toBe('image/jpeg');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.event({ type: HttpEventType.UploadProgress, loaded: 500, total: 1000 });
    req.flush('');
    expect(progress).toEqual([50, 100]);
    expect(done).toBe(true);
    http.verify();
  });
});
