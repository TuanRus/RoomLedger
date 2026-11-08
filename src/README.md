# Mã nguồn RoomLedger

| Thư mục | Nội dung | Phụ trách |
|---|---|---|
| `backend/` | .NET 8: API (ASP.NET Core trên Lambda), Core, Infrastructure (EF Core + PostgreSQL), AI, các Lambda xử lý | Tuấn, Sang |
| `infra/` | AWS CDK (C#), stage `dev` và `demo` | Tuấn, Sang, Phát |
| `frontend/` | Angular | Kỉu |
| `contracts/` | `openapi.yaml`: hợp đồng API giữa FE và BE | Tuấn |
| `evaluation/` | Bộ ảnh mẫu, script đo AI và thời gian | Sang |

## Chạy nhanh

```bash
# Backend
cd src/backend
dotnet restore
dotnet build
dotnet test

# Tạo migration EF Core (code first)
dotnet ef migrations add Md0X_TenThayDoi -p src/RoomLedger.Infrastructure -s src/RoomLedger.Infrastructure -o Persistence/Migrations

# Hạ tầng
cd src/infra/RoomLedger.Cdk
cdk synth -c stage=dev
```
