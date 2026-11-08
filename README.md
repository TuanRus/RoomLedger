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

## Fixed technical choices (do not change without a team decision)

| Choice | Value | Why |
|---|---|---|
| .NET | **8** (`net8.0`, Lambda `dotnet8`), not 9 or 10 | Team choice. EF Core / Npgsql must stay on **8.x**: 9.x installs on net8.0 without any error |
| AWS region | **ap-southeast-2 (Sydney)**, not ap-southeast-1 | The AWS Organization's SCP blocks CloudFormation, S3, Lambda, RDS, etc. in ap-southeast-1, us-east-1 and every other region tested (checked 2026-11-05). CDK can't deploy to us-east-1, so `EdgeStack` (CloudFront WAF) needs another plan |
| Bedrock models | `au.anthropic.claude-sonnet-4-5-20250929-v1:0`, fallback `au.anthropic.claude-haiku-4-5-20251001-v1:0` | In Sydney, Claude only works through inference profiles; newer models say "not available for this account" |
| Lambda concurrency | Account limit is **10** (increase requested) | Don't set reserved concurrency until it's raised |
| Toolchain check | `bash scripts/verify-toolchain.sh` must end with `T0 RESULT: GREEN` | Checks SDK 8, dotnet-ef 8 and that no project resolves EF/Npgsql 9.x |

## Trạng thái MD-04 Biên bản nhận phòng (Sang, cập nhật 2026-11-08)

| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| MD-04-FE-01 Mở biên bản nhận phòng | ✅ Xong | `POST /tenancies/{id}/inspections/move-in`, `GET /inspections/{id}`; bản sao khu vực lúc mở (migration `Md04_InspectionAreaSnapshot`) |
| MD-04-FE-02 Gửi ảnh | ✅ Xong | Dùng `POST /uploads` của Tuấn (+ `captureSource`); photo-ingest, photo-derive (HEIC → JPEG, thumbnail, xóa GPS) |
| MD-04-FE-03 Xóa ảnh trước khi khóa | ✅ Xong | `DELETE /photos/{id}`; chỉ người gửi, chỉ trước khi khóa (K1) |
| MD-04-FE-04 Xác nhận và khóa, BR-18 RULE 1–4 | ✅ Xong | `POST /inspections/{id}/confirm`; khóa đúng một lần; evidence-seal chép ảnh sang bucket evidence (Object Lock), kiểm SHA-256 |
| MD-04-FE-04 BR-18 RULE 5 Chỉ số điện nước ban đầu | ❌ **Chưa làm** | Cần nhóm quyết định: hai bên tự nhập số khi xác nhận, hay AI đọc từ ảnh đồng hồ (MD-05). Kèm cột `tenancy.opening_electricity`, `opening_water` |
| MD-04-FE-04 BR-18 RULE 2 Email báo bên còn lại | ❌ **Chưa làm** | API đã phát sự kiện `InspectionConfirmed`; chưa có Lambda `notify` (SES) để gửi email |
| MD-04-FE-05 Xuất PDF biên bản nhận phòng | ⬜ Stretch | Quyết định ngày 25/11 |

**Chưa kiểm trên AWS (T13):** chưa deploy. Cần Network + Data, StorageStack (bucket `roomledger-{stage}-incoming`, `-derived`, `-evidence` có versioning + Object Lock, bật EventBridge cho incoming) và Api/Auth.
