# RoomLedger

Ứng dụng quản lý phòng trọ: biên bản nhận/trả phòng có ảnh được khóa bằng S3 Object Lock, hai bên xác nhận, AI so ảnh khi trả phòng, ghi điện nước và hợp đồng scan.

## Cấu trúc

```
RoomLedger/
├── .github/            # CI (backend, frontend, infra) và mẫu PR
├── src/
│   ├── backend/        # .NET 8: Api, Core, Infrastructure, Ai, Functions, tests
│   ├── infra/          # AWS CDK (C#), 9 stack
│   ├── frontend/       # Angular
│   ├── contracts/      # openapi.yaml
│   └── evaluation/     # đo AI và thời gian
└── docs/               # tài liệu, chỉ lưu trên máy (.gitignore bỏ qua)
```

## Công nghệ

- Backend: .NET 8, ASP.NET Core trên AWS Lambda, EF Core code first, Amazon RDS for PostgreSQL
- Frontend: Angular
- Hạ tầng: AWS CDK (C#), GitHub Actions (OIDC)
