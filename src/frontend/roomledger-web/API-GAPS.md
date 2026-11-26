# Frontend ↔ API gaps (for API integration)

The Angular frontend (`src/frontend/roomledger-web`) is built against sample data shaped like the
OpenAPI types in `src/contracts/openapi.yaml`. Connecting it to the backend is a separate task (Tuấn).
This note lists what the screens need that the contract does not provide yet, and where the sample
data lives so it can be swapped out.

## Where to connect

| Screen | Store (swap sample data for HTTP here) | Endpoints |
|---|---|---|
| Landlord meter review (LL-04 computer, LM-01/02 phone) | `src/app/core/data/meter-review.store.ts` | `GET /properties/{propertyId}/meter-readings`, `POST /properties/{propertyId}/meter-readings:confirm-all`, `PATCH /meter-readings/{readingId}` |
| Tenant invoice (TD-02 computer, TN-09 phone) | `src/app/core/data/tenant-invoice.store.ts` | `GET /invoices`, `GET /invoices/{invoiceId}` |

Sample data: `src/app/core/data/sample-ledger.ts`. View types: `src/app/core/data/models.ts`
(they reuse the generated schema types where the contract has them).

**Status (2026-11-25):** both stores above call the API when `environment.dataSource` is `'api'`
(`pnpm start:local-api` against the backend's LocalDev mode, or the production build). Fields below that the API
does not send yet are left empty in API mode: tenant name, digit crop, 3-period average and history, captured at,
due date and payment details. Without the 3-period average, bulk approval offers every pending reading at or above
the previous value. The landlord's first property is used and the review period is last month. Meter approvals are
sent (`PATCH /meter-readings/{id}` with `{ value, confirm: true }`) only after the 10-second undo window, because a
confirmed reading cannot be reopened. Review reasons from the contract (`LOW_CONFIDENCE`, `LOWER_THAN_PREVIOUS`,
`UNUSUAL_USAGE`, `AI_FAILED`): a flagged reading with no value shows as "Không đọc được", one with a value as
"Bất thường", and `PENDING_AI` with no value as "Chưa ghi".

**Update (2026-11-25, later):** every landlord and tenant screen now reads the API in API mode, and shows an
empty state, "—" or "Chưa có dữ liệu" for the fields listed below instead of sample values. Sample data is used only
by the default dev build (`pnpm start`) and the unit tests. Still stubs until Cognito sign-in lands (PR #31): sign-in,
forgot password and accepting an invite (disabled in API mode).

## Fields the screens use that the contract does not have

### Meter review (`RoomReadingsResponse` / `MeterReadingResponse`)

- **Tenant name** per room: shown under the room code in the review list.
- **Digit crop URL**: a crop of the digits from the meter photo, shown next to the AI value so the landlord can compare at a glance.
- **Average usage of the last 3 periods** and **the last 4 periods' usage**: used for the "So với 3 kỳ" column, the abnormal flag and the history strip.
- **Captured at** (when the landlord took the photo): shown as "bạn chụp 08:14, 01/12/2026".
- **Review reason values**: the frontend treats `reviewReason = "AI_UNREADABLE"` (with no value) as "Không đọc được" and any other `needsReview` reading as "Bất thường". Please confirm the real reason codes.

### Tenant invoice (`InvoiceDetailResponse`)

- **Due date** (`hạn thanh toán`): not in the response; the receipt shows "Hạn 10/12".
- **Payment details** of the landlord: bank name, account number, account holder, transfer note, and a QR image URL.
  - On a computer the tenant sees the QR code; on a phone only a "Sao chép số tài khoản" button (decided 2026-11-24, see PRODUCT.md).

### Landlord invoices (LL-05, LL-05b/c/d)

- Tenant name, due date, last reminder time, and a correction record (what changed, from/to, when) on invoices.
- Endpoints for reminders, for un-marking a paid invoice, and a period summary (counts, draft total, rooms without an invoice).

### Disputes (LL-08, TN-11, TN-20 / TD-04)

- No `GET /disputes` list (landlord or tenant); disputes only come nested in the invoice detail.
- `DisputeResponse` needs room code, tenant name, period, meter kind, previous / AI / approved / tenant-proposed / corrected values, photo URL and times, rate, and the invoice total before and after a correction.
- `CreateDisputeRequest` needs the value the tenant proposes and must allow a dispute about the whole invoice (`readingId` is required today).

### Landlord meter capture (LM-04 … LM-10)

- Round totals and position ("26/32", "Phòng 27/32"), tenant name, meter serial number, and the previous value when the current reading is null.
- Approving right after the AI read (e.g. `PATCH /meter-readings/{id}` with `{ value, confirm: true }`), a polling rule for the AI result, and reason codes for unreadable photos (blur, glare).
- `CreateMeterReadingRequest` has no `captureSource`; uploads must be retryable for the offline queue.

### Landlord overview, rooms, end of tenancy (LL-02, LL-03, LL-10)

- No dashboard response; today it means combining readings, rooms, invoices (overdue days), tenancies and disputes.
- `RoomResponse` lacks floor, deposit, tenancy start, latest approved readings, contract and current-period reading status, and the pending invite (email, start, expiry). `CreateInviteRequest` has no start date.
- No endpoint to end a tenancy or move rooms: final reading for a date, a final invoice with landlord-entered rent, carried-over balance, the new room's opening readings.

### Landlord settings and contracts (LL-06, LL-07)

- Property settings: reading days, due day, reading reminder, and the period a price change applies from.
- Bank transfer settings: bank, account number, holder, transfer-note template, QR image (fields and upload endpoint).
- Contract versions: uploaded / rejected dates, change note, an endpoint to send a draft to the tenant, and a version type for re-uploading a corrected initial contract. `TenancyFileResponse` has no page count or thumbnail.

### Tenant home, meters, contract, account (TD-01, TD-03, TN-02 … TN-12)

- A tenant endpoint for a room's reading history; `MeterReadingResponse` lacks capturedAt, digitCropUrl, rate, usage and amount.
- The API must hide `value` and `photoUrl` from the tenant until the reading is `CONFIRMED`.
- `TenantHomeResponse` lacks landlord name and email, latest readings, contract confirmation state and date, move data and opening readings; `MeResponse` has no room.
- No notification preference endpoint; the contract scan URL expires after 5 minutes and has no page count.

### Sign-in, forgot password, invite, landing

- Screens are UI only: Cognito sign-in, forgot/confirm password and invite accept are TODOs for the auth work (Kiru). The temporary sign-in stub sends emails containing "lan"/"chunha" to the landlord app, others to the tenant app.
- `InvitePreviewResponse` lacks landlord name and email, start date and sent time. No landlord sign-up contract.

## Not covered by the frontend

The contract still contains move-in/move-out inspections, findings and settlement endpoints. These are
out of scope (PRODUCT.md, decided 2026-11-23), so the frontend has no screens for them.
