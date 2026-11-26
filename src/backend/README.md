# Backend RoomLedger (.NET 8)

| Project | Nội dung |
|---|---|
| `RoomLedger.Core` | Entity, enum, luật nghiệp vụ BR-xx (không phụ thuộc AWS hay EF Core) |
| `RoomLedger.Infrastructure` | `AppDbContext`, cấu hình bảng, migration, S3, EventBridge, RDS IAM auth |
| `RoomLedger.Api` | Minimal API trên Lambda (API Gateway REST), middleware lỗi, phân quyền theo group Cognito |
| `RoomLedger.Functions` | Lambda nền: PostConfirmation, migrate, photo-ingest, read-document... |
| `RoomLedger.Ai` | Prompt và JSON schema cho Bedrock (Sang) |

## Chạy trên máy

```bash
cd src/backend
docker compose up -d db                      # PostgreSQL 16, user/pass postgres/postgres
dotnet tool install --global dotnet-ef --version 8.0.10
dotnet build
dotnet test                                  # test tích hợp tự tạo và xóa database roomledger_test_*

# Chạy API local
export ConnectionStrings__RoomLedger="Host=localhost;Database=roomledger;Username=postgres;Password=postgres"
dotnet ef database update -p src/RoomLedger.Infrastructure -s src/RoomLedger.Infrastructure
dotnet run --project src/RoomLedger.Api
```

Chạy API local cần `Cognito__Authority` (vd. `https://cognito-idp.ap-southeast-2.amazonaws.com/<userPoolId>`) và `Cognito__ClientId` của User Pool dev để kiểm ID token. Trên Lambda, Cognito authorizer của API Gateway đã kiểm token.

### LocalDev: chạy API trên máy không cần AWS (nối FE, test API)

Khi chưa có đăng nhập Cognito ở FE (PR #31), bật LocalDev để API nhận người dùng qua header thay cho ID token.
LocalDev chỉ bật khi đủ cả ba: `ASPNETCORE_ENVIRONMENT=Development`, `LocalDev__Enabled=true`, không chạy trên Lambda.
Trên AWS không bao giờ bật (Lambda có `AWS_LAMBDA_FUNCTION_NAME`, môi trường không phải Development); test `LocalDevDisabledTests` kiểm header bị bỏ qua khi tắt.

| Thành phần | Ở LocalDev |
|---|---|
| Đăng nhập | Header `X-Dev-User: sub\|email\|group\|emailVerified\|name` (name mã hóa URL), đổi thành đúng các claim Cognito |
| S3 | Trong bộ nhớ: URL tải lên/xem là `/api/v1/local-dev/files/{bucket}/{key}` (PUT/GET không cần đăng nhập, như presigned URL); file tới thì ảnh đồng hồ, file hợp đồng, ảnh biên bản chuyển RECEIVED. Mất khi khởi động lại API |
| EventBridge | Trong bộ nhớ, ghi log; `GET /api/v1/local-dev/invites/latest?email=` trả token lời mời thay cho email |
| AI đọc đồng hồ | `POST /api/v1/local-dev/meter-readings/{id}/ai-result` ghi `aiValue`, `needsReview`, `reviewReason` như Lambda AI |

```powershell
cd src/backend
docker compose up -d db
$env:ConnectionStrings__RoomLedger = "Host=localhost;Database=roomledger;Username=postgres;Password=postgres"
dotnet ef database update -p src/RoomLedger.Infrastructure -s src/RoomLedger.Infrastructure
$env:ASPNETCORE_ENVIRONMENT = "Development"; $env:LocalDev__Enabled = "true"
dotnet run --project src/RoomLedger.Api --urls http://localhost:5000
```

Terminal khác, tạo dữ liệu mẫu và kiểm luồng API chính (seed kiêm smoke test, dừng với mã lỗi nếu một lời gọi sai):

```powershell
node src/backend/scripts/local-seed.mjs
curl.exe -H "X-Dev-User: 0193a000-0000-7000-8000-00000000a001|chunha.lan@example.com|landlord|true|C%C3%B4%20Lan" http://localhost:5000/api/v1/properties
```

Rồi chạy FE với API này: `pnpm start:local-api` trong `src/frontend/roomledger-web`, mở `http://localhost:4200/preview` và chọn vai.

### Swagger (test API local)

Chạy với `ASPNETCORE_ENVIRONMENT=Development` rồi mở `http://localhost:<port>/swagger` (JSON: `/swagger/v1/swagger.json`). Nút **Authorize** nhận ID token Cognito. Môi trường khác (Lambda, Testing) mặc định tắt; bật/tắt rõ bằng `OpenApi__Enabled=true|false`. Hợp đồng chính thức với FE vẫn là `src/contracts/openapi.yaml`.

Test tích hợp đọc `ROOMLEDGER_TEST_DB` (mặc định `Host=localhost;Port=5432;Username=postgres;Password=postgres`).

## Migration

```bash
dotnet ef migrations add Md05_AddSomething -p src/RoomLedger.Infrastructure -s src/RoomLedger.Infrastructure -o Persistence/Migrations
dotnet ef migrations script --idempotent -p src/RoomLedger.Infrastructure -s src/RoomLedger.Infrastructure -o migrate.sql
```

Không chạy `database update` vào RDS từ máy cá nhân: GitHub Actions sinh script idempotent và chạy qua Lambda `migrate` trong VPC.

## Quy ước đã áp dụng

- Bảng, cột snake_case số ít (`app_user`, `tenancy_version`), khóa chính UUID v7 (`UuidV7.NewGuid()`), tiền `bigint` VND, enum lưu chuỗi UPPER_SNAKE_CASE.
- Cột audit `created_at, updated_at, created_by, updated_by` tự gán trong `AppDbContext.SaveChangesAsync`.
- Global query filter: chủ nhà chỉ thấy dữ liệu của mình, người thuê chỉ thấy hợp đồng của mình; không thuộc về mình trả 404.
- Lỗi luôn qua `ErrorHandlingMiddleware`: `{success:false, message, errorCode, traceId, details}`. Service ném exception có kiểu (`NotFoundException`, `RecordLockedException`, `InvalidStateException`...).
- `Room.Version` là cột hệ thống `xmin` (concurrency token); PATCH sai version trả `VERSION_CONFLICT`.
- Phân trang keyset theo `(created_at, id)`, cursor mờ, `limit` 1–100.
