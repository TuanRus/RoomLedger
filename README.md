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

## Trạng thái MD-04 Biên bản nhận phòng (Sang, cập nhật 2026-11-11)

| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| MD-04-FE-01 Mở biên bản nhận phòng | ✅ Xong | `POST /tenancies/{id}/inspections/move-in`, `GET /inspections/{id}`; bản sao khu vực lúc mở (migration `Md04_InspectionAreaSnapshot`) |
| MD-04-FE-02 Gửi ảnh | ✅ Xong | Dùng `POST /uploads` của Tuấn (+ `captureSource`); photo-ingest, photo-derive (HEIC → JPEG, thumbnail, xóa GPS) |
| MD-04-FE-03 Xóa ảnh trước khi khóa | ✅ Xong | `DELETE /photos/{id}`; chỉ người gửi, chỉ trước khi khóa (K1) |
| MD-04-FE-04 Xác nhận và khóa, BR-18 RULE 1–4 | ✅ Xong | `POST /inspections/{id}/confirm`; khóa đúng một lần; evidence-seal chép ảnh sang bucket evidence (Object Lock), kiểm SHA-256 |
| MD-04-FE-04 BR-18 RULE 5 Chỉ số điện nước ban đầu | ✅ Xong | `PUT /inspections/{id}/opening-readings` (hai bên nhập, ghi `tenancy.opening_electricity` / `opening_water` của Tuấn ở #13); đổi số → hủy xác nhận; xác nhận cần đủ hai chỉ số (409 `INCOMPLETE_INSPECTION`). Nhập tay, AI đọc ảnh đồng hồ chưa làm |
| MD-04-FE-04 BR-18 RULE 2 Email báo bên còn lại | ❌ **Chưa làm** | API đã phát sự kiện `InspectionConfirmed`; chưa có Lambda `notify` (SES) để gửi email |
| MD-04-FE-05 Xuất PDF biên bản nhận phòng | ⬜ Stretch | Quyết định ngày 25/11 |

**Chưa kiểm trên AWS (T13):** chưa deploy. Cần Network + Data, StorageStack (bucket `roomledger-{stage}-incoming`, `-derived`, `-evidence` có versioning + Object Lock, bật EventBridge cho incoming) và Api/Auth.

## Trạng thái MD-06 Trả phòng và quyết toán (Sang, cập nhật 2026-11-17)

Nhánh `feature/md-06-move-out-compare`.

| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Danh sách ảnh biên bản | ✅ Xong | `GET /inspections/{id}/photos`: URL xem và thumbnail 5 phút (bucket derived), `canDelete`; dùng cho màn MD-04 và ảnh đối chiếu MD-06 |
| MD-06-FE-01 Mở biên bản trả phòng (BR-25 RULE 1–2) | ✅ Xong | `POST /tenancies/{id}/inspections/move-out`: chỉ người thuê; cần biên bản nhận phòng LOCKED (không thì 409 `NO_MOVE_IN_RECORD`); chép khu vực của biên bản nhận phòng; hợp đồng → MOVING_OUT; `referenceInspectionId` để hiện ảnh nhận phòng |
| MD-06-FE-01 Gửi để so sánh (BR-25 RULE 4) | ✅ Xong | `POST /inspections/{id}/submit`: đủ ảnh → COMPARING đúng một lần, phát sự kiện `MoveOutSubmitted`; biên bản trả phòng không dùng được `confirm` |
| MD-06-FE-01 Ảnh đồng hồ tháng cuối (BR-25 RULE 3) | ✅ Xong | Mở biên bản trả phòng chốt `finalPeriod` (tháng giờ Việt Nam lúc mở, cột `inspection.final_period`); gửi biên bản cần chỉ số điện và nước của kỳ đó mà ảnh đồng hồ đã tới (hoặc đã có số), thiếu → 409 `FINAL_READING_MISSING`. Ảnh đồng hồ vẫn tải qua luồng chỉ số MD-05 của Tuấn |
| MD-06-FE-02 Lọc và ghép cặp ảnh (BR-26 RULE 1–2) | ✅ Xong | Perceptual hash DCT 64 bit; ghép ảnh cùng khu vực gần nhất; dưới ngưỡng (6) → SKIPPED; giới hạn số lần gọi (30) chia đều giữa các khu vực, vượt → NOT_ANALYZED |
| MD-06-FE-02 So ảnh bằng Bedrock (BR-26 RULE 3, EXCEPTION 2) | ✅ Xong | Prompt và schema `compareRoomPhotos`; Sonnet 4.5 → Haiku 4.5 dự phòng; cả hai lỗi → NOT_ANALYZED |
| MD-06-FE-02 Step Functions, lưu kết quả, chỉ số (BR-26) | ✅ Xong (chưa chạy trên AWS) | State machine `roomledger-{stage}-move-out-compare` chạy khi có `MoveOutSubmitted`: đọc ngưỡng (`/roomledger/{stage}/compare/phash-threshold` = 6) và giới hạn (`.../max-calls` = 30) từ SSM → `plan-compare` (VPC, pHash trên thumbnail) → Map `compare-pairs` (ngoài VPC, Bedrock, 3 cặp song song; lỗi → NOT_ANALYZED) → `persist-results` (VPC: dòng `finding` SUGGESTED / SKIPPED / NOT_ANALYZED, COMPARING → REVIEWING, audit `MOVE_OUT_COMPARED`) → chỉ số CloudWatch `RoomLedger/Compare` → sự kiện `MoveOutCompared`. Mọi rule EventBridge có DLQ chung; báo động khi DLQ có tin hoặc lần chạy lỗi |
| MD-06-FE-02 Giải thích gợi ý (`explainFinding`, BR-26 RULE 4) và email chủ nhà (RULE 5) | ⬜ Chưa làm | `explainFinding` chờ Tuấn quyết (đề xuất bỏ); email chờ `notify` nghe `MoveOutCompared` |
| MD-06-FE-03 Chủ nhà duyệt gợi ý (BR-27) | ✅ Xong | Bảng `finding` (thiết kế của Sang, chờ Tuấn duyệt; migration `Md06_Finding`). `GET/POST /inspections/{id}/findings`, `POST /findings/{id}/charge`, `/dismiss`, `POST /inspections/{id}/send-to-tenant`; tổng tiền ≤ tiền cọc (`AMOUNT_EXCEEDS_DEPOSIT`); còn gợi ý chưa xử lý → `PENDING_FINDINGS` |
| MD-06-FE-04 Người thuê đồng ý hoặc khiếu nại (BR-28) | ✅ Xong | `POST /findings/{id}/accept`, `/dispute` (lý do ≥ 10 ký tự, tạo `dispute` FINDING), `/respond` (chủ nhà: CORRECTED / KEPT / REMOVED); người thuê đồng ý lại → RESOLVED. Email báo hai bên chờ `notify` |
| MD-06-FE-05 Quyết toán (BR-29) | ✅ Xong | `GET /tenancies/{id}/settlement` (hai bên xem trước: cọc, tiền trừ ACCEPTED + RESOLVED, hóa đơn chưa trả, tiền trả lại có thể âm, điều kiện còn thiếu); `POST` (chủ nhà): kiểm `FINAL_READING_UNCONFIRMED` → `FINAL_INVOICE_MISSING` → `OPEN_DISPUTES` → `PENDING_FINDINGS` (details liệt kê tất cả), ghi bảng `settlement`, biên bản trả phòng và hợp đồng → SETTLED, phòng → VACANT, phát `TenancySettled`; quyết toán hai lần chỉ ghi một lần. Thiết kế đã được Tuấn duyệt 17/11 (A2, A3, B1–B5) |
| MD-06-FE-05 Biên bản thanh lý PDF và niêm phong ảnh trả phòng | ✅ Xong (chưa chạy trên AWS) | Rule `TenancySettled` → Lambda `render-pdf` (VPC, QuestPDF với font Lato đi kèm gói, đủ dấu tiếng Việt): hai bên, phòng, kỳ, từng khoản trừ cọc, hóa đơn chưa trả, tổng kết (âm → "Người thuê còn phải trả"); ghi `derived/reports/{tenancyId}/thanh-ly-{settlementId}.pdf`, điền `settlement.pdf_key`, API trả `pdfUrl` (5 phút). Cùng rule gọi evidence-seal để niêm phong ảnh biên bản trả phòng. Thay đổi `WorkflowStack.cs` cần Phát duyệt |

**Cần biết:**
- Bedrock thật chưa gọi được: tài khoản 479365371363 chưa gửi form use-case Anthropic (không phải do SCP). Test dùng Bedrock giả.
- Chủ nhà vẫn tải được ảnh vào biên bản trả phòng qua `/uploads` (BR-25 chỉ cho người thuê); cần nhóm quyết định có chặn không.
- Ngưỡng 6 mới kiểm trên ảnh dựng; cần chỉnh trên bộ ảnh thật (đổi trong SSM, không cần deploy).
- Báo động chưa gắn SNS/email: cần chọn người nhận.
- Ảnh đồng hồ: chưa Lambda nào chuyển `photo_status` của chỉ số khỏi PENDING_UPLOAD, nên lúc gửi biên bản chỉ số được tính khi ảnh đã tới **hoặc** đã có số. Cần luồng chỉ số MD-05 cập nhật `photo_status` khi ảnh tới.
- `/src/infra/` do Phát duyệt (CODEOWNERS): thay đổi WorkflowStack cần Phát xem.
- Chưa kiểm trên AWS.
