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

## Trạng thái MD-06 Trả phòng và quyết toán (Sang, cập nhật 2026-11-10)

Nhánh `feature/md-06-move-out-compare`.

| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Danh sách ảnh biên bản | ✅ Xong | `GET /inspections/{id}/photos`: URL xem và thumbnail 5 phút (bucket derived), `canDelete`; dùng cho màn MD-04 và ảnh đối chiếu MD-06 |
| MD-06-FE-01 Mở biên bản trả phòng (BR-25 RULE 1–2) | ✅ Xong | `POST /tenancies/{id}/inspections/move-out`: chỉ người thuê; cần biên bản nhận phòng LOCKED (không thì 409 `NO_MOVE_IN_RECORD`); chép khu vực của biên bản nhận phòng; hợp đồng → MOVING_OUT; `referenceInspectionId` để hiện ảnh nhận phòng |
| MD-06-FE-01 Gửi để so sánh (BR-25 RULE 4) | ✅ Xong | `POST /inspections/{id}/submit`: đủ ảnh → COMPARING đúng một lần, phát sự kiện `MoveOutSubmitted`; biên bản trả phòng không dùng được `confirm` |
| MD-06-FE-01 Ảnh đồng hồ tháng cuối (BR-25 RULE 3) | ❌ **Chưa nối** | Đang dùng upload chỉ số của MD-05 (Tuấn), chưa gắn với biên bản trả phòng |
| MD-06-FE-02 Lọc và ghép cặp ảnh (BR-26 RULE 1–2) | ✅ Xong (logic) | Perceptual hash DCT 64 bit; ghép ảnh cùng khu vực gần nhất; dưới ngưỡng (6) → SKIPPED; giới hạn số lần gọi (30) chia đều giữa các khu vực, vượt → NOT_ANALYZED |
| MD-06-FE-02 So ảnh bằng Bedrock (BR-26 RULE 3, EXCEPTION 2) | ✅ Xong (logic) | Prompt và schema `compareRoomPhotos`; Sonnet 4.5 → Haiku 4.5 dự phòng; cả hai lỗi → NOT_ANALYZED |
| MD-06-FE-02 Lưu kết quả, Step Functions, chỉ số (BR-26 RULE 4–5) | ❌ **Chưa làm** | Cần thống nhất bảng `finding` với Tuấn (đề xuất trong ERD); Lambda compare-pairs, persist-results; rule `MoveOutSubmitted`; ngưỡng và giới hạn trong SSM; CloudWatch; → REVIEWING |
| MD-06-FE-03 Chủ nhà duyệt gợi ý | ⬜ Chưa làm | |
| MD-06-FE-04 Người thuê đồng ý hoặc khiếu nại | ⬜ Chưa làm | |
| MD-06-FE-05 Quyết toán và biên bản thanh lý PDF | ⬜ Chưa làm | |

**Cần biết:**
- Bedrock thật chưa gọi được: tài khoản 479365371363 chưa gửi form use-case Anthropic (không phải do SCP). Test dùng Bedrock giả.
- Chủ nhà vẫn tải được ảnh vào biên bản trả phòng qua `/uploads` (BR-25 chỉ cho người thuê); cần nhóm quyết định có chặn không.
- Ngưỡng 6 mới kiểm trên ảnh dựng; cần chỉnh trên bộ ảnh thật.
- Chưa kiểm trên AWS.
