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

Chạy API local cần `Cognito__Authority` (vd. `https://cognito-idp.ap-southeast-1.amazonaws.com/<userPoolId>`) và `Cognito__ClientId` của User Pool dev để kiểm ID token. Trên Lambda, Cognito authorizer của API Gateway đã kiểm token.

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
