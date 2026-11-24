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

## Not covered by the frontend

The contract still contains move-in/move-out inspections, findings and settlement endpoints. These are
out of scope (PRODUCT.md, decided 2026-11-23), so the frontend has no screens for them.
